using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Motion
{
    public class CommandCoordinator
    {
        private readonly MotionGraphManager _motionGraphManager;
        private readonly Dictionary<string, Func<string, Task>> _moveExecutors;
        private readonly IOManager _ioManager;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _hexapodSemaphore = new SemaphoreSlim(1, 1);

        private readonly PneumaticSlideService _slideService;

        public CommandCoordinator(
            MotionGraphManager motionGraphManager,
            HexapodMovementService leftHexapod,
            HexapodMovementService rightHexapod,
            HexapodMovementService bottomHexapod,
            GantryMovementService gantry,
            IOManager ioManager,
            PneumaticSlideService slideService,
            ILogger logger)
        {
            _motionGraphManager = motionGraphManager;
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _slideService = slideService ?? throw new ArgumentNullException(nameof(slideService));
            _logger = logger.ForContext<CommandCoordinator>();

            _moveExecutors = new Dictionary<string, Func<string, Task>>
            {
                { "hex-left", async (position) => await ExecuteHexapodMove(leftHexapod, 0, position) },
                { "hex-right", async (position) => await ExecuteHexapodMove(rightHexapod, 2, position) },
                { "hex-bottom", async (position) => await ExecuteHexapodMove(bottomHexapod, 1, position) },
                { "gantry-main", async (position) => await ExecuteGantryMove(gantry, position) }
            };
        }

        public async Task ExecuteCommandSequence(List<CoordinatedCommand> commands)
        {
            try
            {
                // Validate all paths for motion commands first
                var pathAnalyses = new Dictionary<string, PathAnalysis>();
                foreach (var command in commands.Where(c => c.Type == CommandType.Motion))
                {
                    var analysis = await _motionGraphManager.AnalyzeMovementPath(
                        command.DeviceId,
                        command.TargetPosition);

                    if (!analysis.IsValid)
                    {
                        throw new InvalidOperationException(
                            $"Invalid movement path for {command.DeviceId} to {command.TargetPosition}: {analysis.Error}");
                    }

                    pathAnalyses[command.DeviceId] = analysis;
                    _logger.Information("Validated path for {DeviceId} to {Position}: {Path}",
                        command.DeviceId,
                        command.TargetPosition,
                        string.Join(" -> ", analysis.Path));
                }

                // Execute commands by order
                var orderedCommands = commands
                    .GroupBy(c => c.ExecutionOrder)
                    .OrderBy(g => g.Key);

                foreach (var group in orderedCommands)
                {
                    var tasks = new List<Task>();

                    foreach (var command in group)
                    {
                        var task = ExecuteCommand(command, pathAnalyses);

                        if (command.WaitForCompletion)
                        {
                            await task;
                        }
                        else
                        {
                            tasks.Add(task);
                        }
                    }

                    // Wait for any remaining non-waiting tasks to complete
                    if (tasks.Any())
                    {
                        await Task.WhenAll(tasks);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during command sequence execution");
                throw;
            }
        }

        private async Task ExecuteCommand(CoordinatedCommand command, Dictionary<string, PathAnalysis> pathAnalyses)
        {
            _logger.Information("Executing command: {Description}", command.Description);

            try
            {
                switch (command.Type)
                {
                    case CommandType.Motion:
                        await ExecuteMotionCommand(command, pathAnalyses);
                        break;

                    case CommandType.Output:
                        await ExecuteOutputCommand(command);
                        break;

                    case CommandType.SlideMove:
                        await ExecuteSlideCommand(command);
                        break;

                    case CommandType.Timer:
                        await ExecuteTimerCommand(command);
                        break;

                    case CommandType.WaitForInput:
                        await ExecuteWaitForInputCommand(command);
                        break;

                    default:
                        throw new ArgumentException($"Unknown command type: {command.Type}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing command: {Description}", command.Description);
                throw;
            }
        }

        private async Task ExecuteMotionCommand(CoordinatedCommand command, Dictionary<string, PathAnalysis> pathAnalyses)
        {
            if (command.DeviceId.StartsWith("hex"))
            {
                await _hexapodSemaphore.WaitAsync();
                try
                {
                    if (_moveExecutors.TryGetValue(command.DeviceId, out var executor))
                    {
                        var analysis = pathAnalyses[command.DeviceId];
                        foreach (var position in analysis.Path)
                        {
                            await executor(position);
                        }
                    }
                }
                finally
                {
                    _hexapodSemaphore.Release();
                }
            }
            else
            {
                if (_moveExecutors.TryGetValue(command.DeviceId, out var executor))
                {
                    var analysis = pathAnalyses[command.DeviceId];
                    foreach (var position in analysis.Path)
                    {
                        await executor(position);
                    }
                }
            }
        }

        private async Task ExecuteOutputCommand(CoordinatedCommand command)
        {
            // Direct IO control without validation
            bool success;
            if (command.State)
            {
                success = _ioManager.SetOutput(command.DeviceName, command.PinName);
                _logger.Debug("Setting output {Device}.{Pin} without validation", command.DeviceName, command.PinName);
            }
            else
            {
                success = _ioManager.ClearOutput(command.DeviceName, command.PinName);
                _logger.Debug("Clearing output {Device}.{Pin} without validation", command.DeviceName, command.PinName);
            }

            if (!success)
            {
                throw new InvalidOperationException(
                    $"Failed to set {command.DeviceName}.{command.PinName} to {(command.State ? "ON" : "OFF")}");
            }
        }

        private async Task ExecuteSlideCommand(CoordinatedCommand command)
        {
            // Full slide control with validation
            SlideOperationResult result;
            if (command.TargetSlideState == SlideState.Down)
            {
                _logger.Debug("Activating slide {SlideId} with full validation", command.SlideId);
                result = await _slideService.ActivateSlideAsync(command.SlideId);
            }
            else
            {
                _logger.Debug("Deactivating slide {SlideId} with full validation", command.SlideId);
                result = await _slideService.DeactivateSlideAsync(command.SlideId);
            }

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to move slide {command.SlideId} to {command.TargetSlideState}: {result.Error}");
            }
        }

        private async Task ExecuteTimerCommand(CoordinatedCommand command)
        {
            using var timer = new PreciseTimer(_logger);
            var completionSource = new TaskCompletionSource<bool>();

            timer.TimerCompleted += (s, e) => completionSource.SetResult(true);
            timer.TimerError += (s, e) => completionSource.SetException(e);

            await timer.StartAsync(command.Duration);
            await completionSource.Task;
        }

        private async Task ExecuteWaitForInputCommand(CoordinatedCommand command)
        {
            var startTime = DateTime.UtcNow;
            var timeout = command.Timeout ?? TimeSpan.FromSeconds(30); // Default 30 second timeout

            while (true)
            {
                var currentState = _ioManager.GetPinState(command.InputDeviceName, command.InputPinName, true);

                if (currentState == command.ExpectedState)
                {
                    _logger.Information("Input {Device}.{Pin} reached expected state {State}",
                        command.InputDeviceName, command.InputPinName, command.ExpectedState);
                    return;
                }

                if (DateTime.UtcNow - startTime > timeout)
                {
                    throw new TimeoutException(
                        $"Timeout waiting for {command.InputDeviceName}.{command.InputPinName} to be {command.ExpectedState}");
                }

                await Task.Delay(50); // Poll every 50ms
            }
        }

        private async Task ExecuteHexapodMove(HexapodMovementService service, int hexapodId, string targetPosition)
        {
            try
            {
                _logger.Information("Moving hexapod {HexapodId} to position {Position}", hexapodId, targetPosition);
                await service.MoveToPositionAsync(targetPosition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving hexapod {HexapodId} to {Position}", hexapodId, targetPosition);
                throw;
            }
        }

        private async Task ExecuteGantryMove(GantryMovementService service, string targetPosition)
        {
            try
            {
                _logger.Information("Moving gantry to position {Position}", targetPosition);
                await service.MoveToPositionAsync(targetPosition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving gantry to {Position}", targetPosition);
                throw;
            }
        }
    }
}