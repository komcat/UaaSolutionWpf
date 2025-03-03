using System;
using System.Threading.Tasks;
using Serilog;
using EzIIOLib;
using UaaSolutionWpf.Commands;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Command to operate a pneumatic slide
    /// </summary>
    public class PneumaticSlideCommand : Command<PneumaticSlideManager>
    {
        private readonly string _slideName;
        private readonly bool _extend;
        private readonly int _timeoutMs;

        public PneumaticSlideCommand(
            PneumaticSlideManager slideManager,
            string slideName,
            bool extend,
            int timeoutMs = 5000,
            ILogger logger = null)
            : base(
                slideManager,
                $"PneumaticSlide-{slideName}-{(extend ? "Extend" : "Retract")}",
                $"{(extend ? "Extend" : "Retract")} pneumatic slide {slideName}",
                logger)
        {
            _slideName = slideName ?? throw new ArgumentNullException(nameof(slideName));
            _extend = extend;
            _timeoutMs = timeoutMs;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                _logger.Information("{Action} pneumatic slide {SlideName}",
                    _extend ? "Extending" : "Retracting", _slideName);

                PneumaticSlide slide;
                try
                {
                    slide = _context.GetSlide(_slideName);
                }
                catch (ArgumentException ex)
                {
                    _logger.Error(ex, "Failed to find pneumatic slide {SlideName}", _slideName);
                    return CommandResult.Failed($"Slide not found: {ex.Message}", ex);
                }

                // Check if the slide is already in the desired position
                if ((_extend && slide.Position == SlidePosition.Extended) ||
                    (!_extend && slide.Position == SlidePosition.Retracted))
                {
                    _logger.Information("Pneumatic slide {SlideName} is already {Position}",
                        _slideName, _extend ? "extended" : "retracted");
                    return CommandResult.Successful($"Slide {_slideName} is already {(_extend ? "extended" : "retracted")}");
                }

                // Check for cancellation
                _cancellationToken.ThrowIfCancellationRequested();

                // Move the slide - note that ExtendAsync/RetractAsync already have internal
                // timeouts and will wait for the slide to complete its movement
                bool success = _extend ?
                    await slide.ExtendAsync() :
                    await slide.RetractAsync();

                if (!success)
                {
                    _logger.Warning("Failed to {Action} pneumatic slide {SlideName}",
                        _extend ? "extend" : "retract", _slideName);
                    return CommandResult.Failed(
                        $"Failed to {(_extend ? "extend" : "retract")} pneumatic slide {_slideName}");
                }

                // By this point, the slide should have reached its target position
                // as the ExtendAsync/RetractAsync methods already handle waiting for this
                _logger.Information("Successfully {Action} pneumatic slide {SlideName}",
                    _extend ? "extended" : "retracted", _slideName);
                return CommandResult.Successful(
                    $"Pneumatic slide {_slideName} {(_extend ? "extended" : "retracted")} successfully");
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Operation canceled while {Action} pneumatic slide {SlideName}",
                    _extend ? "extending" : "retracting", _slideName);
                return CommandResult.Failed($"Operation canceled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error {Action} pneumatic slide {SlideName}",
                    _extend ? "extending" : "retracting", _slideName);
                return CommandResult.Failed(
                    $"Error {(_extend ? "extending" : "retracting")} pneumatic slide {_slideName}: {ex.Message}",
                    ex);
            }
        }
    }

    /// <summary>
    /// Command to wait for a pneumatic slide to reach a specific position
    /// </summary>
    public class WaitForSlidePositionCommand : Command<PneumaticSlideManager>
    {
        private readonly string _slideName;
        private readonly SlidePosition _targetPosition;
        private readonly int _timeoutMs;

        public WaitForSlidePositionCommand(
            PneumaticSlideManager slideManager,
            string slideName,
            SlidePosition targetPosition,
            int timeoutMs = 5000,
            ILogger logger = null)
            : base(
                slideManager,
                $"WaitForSlide-{slideName}-{targetPosition}",
                $"Wait for pneumatic slide {slideName} to reach position {targetPosition}",
                logger)
        {
            _slideName = slideName ?? throw new ArgumentNullException(nameof(slideName));
            _targetPosition = targetPosition;
            _timeoutMs = timeoutMs;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                _logger.Information("Waiting for pneumatic slide {SlideName} to reach position {Position}",
                    _slideName, _targetPosition);

                PneumaticSlide slide;
                try
                {
                    slide = _context.GetSlide(_slideName);
                }
                catch (ArgumentException ex)
                {
                    _logger.Error(ex, "Failed to find pneumatic slide {SlideName}", _slideName);
                    return CommandResult.Failed($"Slide not found: {ex.Message}", ex);
                }

                // Check if the slide is already in the desired position
                if (slide.Position == _targetPosition)
                {
                    _logger.Information("Pneumatic slide {SlideName} is already in position {Position}",
                        _slideName, _targetPosition);
                    return CommandResult.Successful($"Slide {_slideName} is already in position {_targetPosition}");
                }

                // Set up a task completion source to wait for position change
                var tcs = new TaskCompletionSource<bool>();

                // Handler for the position changed event
                void PositionChangedHandler(object sender, SlidePosition position)
                {
                    if (position == _targetPosition)
                    {
                        tcs.TrySetResult(true);
                    }
                    else if (position == SlidePosition.Unknown)
                    {
                        tcs.TrySetException(new InvalidOperationException("Slide entered unknown state"));
                    }
                }

                // Subscribe to the position changed event
                slide.PositionChanged += PositionChangedHandler;

                try
                {
                    // Check for cancellation
                    _cancellationToken.ThrowIfCancellationRequested();

                    // Create a timeout task
                    var timeoutTask = Task.Delay(_timeoutMs, _cancellationToken);

                    // Wait for either the position to change or timeout
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        _logger.Warning("Timeout waiting for pneumatic slide {SlideName} to reach position {Position}",
                            _slideName, _targetPosition);
                        return CommandResult.Failed(
                            $"Timeout waiting for pneumatic slide {_slideName} to reach position {_targetPosition}");
                    }

                    // Position reached successfully
                    _logger.Information("Pneumatic slide {SlideName} reached position {Position}",
                        _slideName, _targetPosition);
                    return CommandResult.Successful(
                        $"Pneumatic slide {_slideName} reached position {_targetPosition}");
                }
                finally
                {
                    // Unsubscribe from the event
                    slide.PositionChanged -= PositionChangedHandler;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Operation canceled while waiting for pneumatic slide {SlideName}",
                    _slideName);
                return CommandResult.Failed($"Operation canceled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error waiting for pneumatic slide {SlideName}",
                    _slideName);
                return CommandResult.Failed(
                    $"Error waiting for pneumatic slide {_slideName}: {ex.Message}",
                    ex);
            }
        }
    }
}