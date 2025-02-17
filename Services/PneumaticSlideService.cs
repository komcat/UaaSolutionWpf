using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Serilog;
using UaaSolutionWpf.Config;
using UaaSolutionWpf.IO;
using System.Threading;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Services
{
    public class SlideOperationResult
    {
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
        public string Error { get; set; }
        public SlideState FinalState { get; set; }
    }

    public class SlideStateChangedEventArgs : EventArgs
    {
        public string SlideId { get; }
        public SlideState OldState { get; }
        public SlideState NewState { get; }
        public TimeSpan TransitionDuration { get; }

        public SlideStateChangedEventArgs(string slideId, SlideState oldState, SlideState newState, TimeSpan duration)
        {
            SlideId = slideId;
            OldState = oldState;
            NewState = newState;
            TransitionDuration = duration;
        }
    }

    public class PneumaticSlideService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IOManager _ioManager;
        private readonly PneumaticSlideConfigManager _configManager;
        private readonly ConcurrentDictionary<string, SlideState> _slideStates;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _operationCancellations;
        private const int DEFAULT_TIMEOUT_MS = 10000; // 10 seconds

        public event EventHandler<SlideStateChangedEventArgs> SlideStateChanged;

        public PneumaticSlideService(string configPath, IOManager ioManager, ILogger logger)
        {
            _logger = logger?.ForContext<PneumaticSlideService>() ?? throw new ArgumentNullException(nameof(logger));
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _configManager = new PneumaticSlideConfigManager(configPath);
            _slideStates = new ConcurrentDictionary<string, SlideState>();
            _operationCancellations = new ConcurrentDictionary<string, CancellationTokenSource>();

            // Initialize slide states
            foreach (var slide in _configManager.GetSlideConfigurations())
            {
                _slideStates[slide.Id] = GetInitialSlideState(slide);
            }

            // Subscribe to IO state changes
            _ioManager.IOStateChanged += IOManager_IOStateChanged;
        }

        private SlideState GetInitialSlideState(SlideConfiguration slide)
        {
            bool? upSensorState = _ioManager.GetPinState(
                slide.Controls.Sensors.Device,
                slide.Controls.Sensors.UpSensor,
                true);

            bool? downSensorState = _ioManager.GetPinState(
                slide.Controls.Sensors.Device,
                slide.Controls.Sensors.DownSensor,
                true);

            if (upSensorState == true)
                return SlideState.Up;
            if (downSensorState == true)
                return SlideState.Down;
            return SlideState.Unknown;
        }

        private void IOManager_IOStateChanged(object sender, IOStateEventArgs e)
        {
            foreach (var slide in _configManager.GetSlideConfigurations())
            {
                if (e.DeviceName == slide.Controls.Sensors.Device &&
                    (e.PinName == slide.Controls.Sensors.UpSensor ||
                     e.PinName == slide.Controls.Sensors.DownSensor))
                {
                    UpdateSlideState(slide);
                }
            }
        }

        private void UpdateSlideState(SlideConfiguration slide)
        {
            bool? upSensorState = _ioManager.GetPinState(
                slide.Controls.Sensors.Device,
                slide.Controls.Sensors.UpSensor,
                true);

            bool? downSensorState = _ioManager.GetPinState(
                slide.Controls.Sensors.Device,
                slide.Controls.Sensors.DownSensor,
                true);

            SlideState newState = SlideState.Unknown;
            if (upSensorState == true)
                newState = SlideState.Up;
            else if (downSensorState == true)
                newState = SlideState.Down;

            var oldState = _slideStates.GetOrAdd(slide.Id, SlideState.Unknown);
            if (oldState != newState)
            {
                _slideStates[slide.Id] = newState;
                OnSlideStateChanged(slide.Id, oldState, newState, TimeSpan.Zero);
            }
        }

        public async Task<SlideOperationResult> ActivateSlideAsync(string slideId)
        {
            return await ChangeSlideStateAsync(slideId, targetState: SlideState.Down);
        }

        public async Task<SlideOperationResult> DeactivateSlideAsync(string slideId)
        {
            return await ChangeSlideStateAsync(slideId, targetState: SlideState.Up);
        }

        private async Task<SlideOperationResult> ChangeSlideStateAsync(string slideId, SlideState targetState)
        {
            var slide = _configManager.GetSlideConfigurations().FirstOrDefault(s => s.Id == slideId);
            if (slide == null)
            {
                return new SlideOperationResult
                {
                    Success = false,
                    Error = $"Slide with ID {slideId} not found"
                };
            }

            // Cancel any existing operation
            if (_operationCancellations.TryGetValue(slideId, out var existingCts))
            {
                existingCts.Cancel();
                _operationCancellations.TryRemove(slideId, out _);
            }

            var cts = new CancellationTokenSource();
            _operationCancellations[slideId] = cts;

            try
            {
                var startTime = DateTime.Now;
                var currentState = _slideStates.GetOrAdd(slideId, SlideState.Unknown);

                if (currentState == targetState)
                {
                    return new SlideOperationResult
                    {
                        Success = true,
                        Duration = TimeSpan.Zero,
                        FinalState = currentState
                    };
                }

                // Set the output based on target state and configuration
                _logger.Information(
                    "Changing slide {SlideId} - Target: {TargetState}, Device: {Device}, Pin: {Pin}, SetToMoveUp: {SetToMoveUp}",
                    slideId,
                    targetState,
                    slide.Controls.Output.Device,
                    slide.Controls.Output.PinName,
                    slide.Controls.Output.SetToMoveUp);

                bool success=true;
                if (targetState == SlideState.Down)
                {
                    if (slide.Controls.Output.SetToMoveUp)
                    {
                        _logger.Debug("Activating (Down) - Clearing output because SetToMoveUp is true");
                        success = _ioManager.SetOutput(slide.Controls.Output.Device, slide.Controls.Output.PinName);
                    }
                    
                }
                else // Moving Up
                {
                    if (slide.Controls.Output.SetToMoveUp)
                    {
                        _logger.Debug("Deactivating (Up) - Setting output because SetToMoveUp is true");
                        success = _ioManager.ClearOutput(slide.Controls.Output.Device, slide.Controls.Output.PinName);
                    }
                    
                }

                _logger.Information(
                    "IO operation result for {SlideId}: {Success}",
                    slideId,
                    success);

                if (!success)
                {
                    return new SlideOperationResult
                    {
                        Success = false,
                        Error = "Failed to set output state",
                        FinalState = currentState
                    };
                }

                // Wait for sensor confirmation
                using var timeoutCts = new CancellationTokenSource(DEFAULT_TIMEOUT_MS);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cts.Token);

                while (!linkedCts.Token.IsCancellationRequested)
                {
                    var state = _slideStates[slideId];
                    if (state == targetState)
                    {
                        var duration = DateTime.Now - startTime;
                        return new SlideOperationResult
                        {
                            Success = true,
                            Duration = duration,
                            FinalState = state
                        };
                    }

                    await Task.Delay(100, linkedCts.Token);
                }

                if (timeoutCts.Token.IsCancellationRequested)
                {
                    return new SlideOperationResult
                    {
                        Success = false,
                        Error = "Operation timed out",
                        Duration = TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS),
                        FinalState = _slideStates[slideId]
                    };
                }

                return new SlideOperationResult
                {
                    Success = false,
                    Error = "Operation cancelled",
                    Duration = DateTime.Now - startTime,
                    FinalState = _slideStates[slideId]
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error changing slide state for {SlideId} to {TargetState}", slideId, targetState);
                return new SlideOperationResult
                {
                    Success = false,
                    Error = ex.Message,
                    FinalState = _slideStates[slideId]
                };
            }
            finally
            {
                _operationCancellations.TryRemove(slideId, out _);
            }
        }

        protected virtual void OnSlideStateChanged(string slideId, SlideState oldState, SlideState newState, TimeSpan duration)
        {
            SlideStateChanged?.Invoke(this, new SlideStateChangedEventArgs(slideId, oldState, newState, duration));
        }

        public SlideState GetCurrentState(string slideId)
        {
            return _slideStates.GetOrAdd(slideId, SlideState.Unknown);
        }

        public IReadOnlyList<SlideConfiguration> GetSlideConfigurations()
        {
            return _configManager.GetSlideConfigurations();
        }

        public void Dispose()
        {
            foreach (var cts in _operationCancellations.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _operationCancellations.Clear();
            _ioManager.IOStateChanged -= IOManager_IOStateChanged;
        }
    }
}