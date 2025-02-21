using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using EzIIOLib;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.ViewModels;
using Newtonsoft.Json;
using System.IO;

namespace UaaSolutionWpf.Motion
{
    public class CommandCoordinator
    {
        private readonly MotionGraphManager _motionGraphManager;
        private readonly Dictionary<string, Func<string, Task>> _moveExecutors;
        private readonly MultiDeviceManager _ioManager;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _hexapodSemaphore = new SemaphoreSlim(1, 1);

        private readonly PneumaticSlideManager _slideManager; // Changed from PneumaticSlideService


        public CommandCoordinator(
                MotionGraphManager motionGraphManager,
                HexapodMovementService leftHexapod,
                HexapodMovementService rightHexapod,
                HexapodMovementService bottomHexapod,
                GantryMovementService gantry,
                MultiDeviceManager ioManager,
                ILogger logger)
        {
            _motionGraphManager = motionGraphManager;
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _slideManager = new PneumaticSlideManager(ioManager);

            var config = LoadConfiguration();
            _slideManager.LoadSlidesFromConfig(config);
            _logger = logger.ForContext<CommandCoordinator>();

            if (!_ioManager.AreAllDevicesConnected())
            {
                throw new InvalidOperationException("EzIIOManager is not connected");
            }

            _moveExecutors = new Dictionary<string, Func<string, Task>>
            {
                { "hex-left", async (position) => await ExecuteHexapodMove(leftHexapod, 0, position) },
                { "hex-right", async (position) => await ExecuteHexapodMove(rightHexapod, 2, position) },
                { "hex-bottom", async (position) => await ExecuteHexapodMove(bottomHexapod, 1, position) },
                { "gantry-main", async (position) => await ExecuteGantryMove(gantry, position) }
            };
        }
        private IOConfiguration LoadConfiguration()
        {
            string configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "IOConfig.json"
            );

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"Configuration file not found: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            return JsonConvert.DeserializeObject<IOConfiguration>(jsonContent);
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
            // Using EzIIOManager for IO control
            bool success;
            if (command.State)
            {
                success = _ioManager.SetOutput(command.PinName); // Changed to use just pinName
                _logger.Debug("Setting output pin {Pin} to ON", command.PinName);
            }
            else
            {
                success = _ioManager.ClearOutput(command.PinName); // Changed to use just pinName
                _logger.Debug("Clearing output pin {Pin} to OFF", command.PinName);
            }

            if (!success)
            {
                throw new InvalidOperationException(
                    $"Failed to set pin {command.PinName} to {(command.State ? "ON" : "OFF")}");
            }
        }

        private async Task ExecuteSlideCommand(CoordinatedCommand command)
        {
            try
            {
                var slide = _slideManager.GetSlide(command.SlideId);
                bool success;

                // Use the SlidePosition enum value directly
                if (command.TargetSlidePosition == SlidePosition.Extended)
                {
                    _logger.Debug("Extending slide {SlideId}", command.SlideId);
                    success = await slide.ExtendAsync();
                }
                else
                {
                    _logger.Debug("Retracting slide {SlideId}", command.SlideId);
                    success = await slide.RetractAsync();
                }

                if (!success)
                {
                    throw new InvalidOperationException(
                        $"Failed to move slide {command.SlideId} to position {command.TargetSlidePosition}");
                }

                // Wait for the slide to reach its target position
                var startTime = DateTime.UtcNow;
                var timeout = command.Timeout ?? TimeSpan.FromSeconds(30); // Default 30 second timeout

                while (true)
                {
                    var position = slide.Position;

                    if (position == command.TargetSlidePosition)
                    {
                        _logger.Information("Slide {SlideId} reached target position {Position}",
                            command.SlideId, position);
                        return;
                    }

                    if (position == SlidePosition.Unknown)
                    {
                        throw new InvalidOperationException(
                            $"Slide {command.SlideId} entered unknown state");
                    }

                    if (DateTime.UtcNow - startTime > timeout)
                    {
                        throw new TimeoutException(
                            $"Timeout waiting for slide {command.SlideId} to reach position {command.TargetSlidePosition}");
                    }

                    await Task.Delay(50); // Poll every 50ms
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing slide command for {SlideId}", command.SlideId);
                throw;
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
                var currentState = _ioManager.GetInputState(command.InputPinName); // Changed to use EzIIOManager method

                if (currentState == command.ExpectedState)
                {
                    _logger.Information("Input pin {Pin} reached expected state {State}",
                        command.InputPinName, command.ExpectedState);
                    return;
                }

                if (DateTime.UtcNow - startTime > timeout)
                {
                    throw new TimeoutException(
                        $"Timeout waiting for pin {command.InputPinName} to be {command.ExpectedState}");
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