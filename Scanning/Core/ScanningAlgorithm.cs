using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf.Scanning.Core
{
    public class ScanningAlgorithm : IDisposable
    {
        private readonly HexapodMovementService _movementService;
        private readonly DevicePositionMonitor _positionMonitor;
        private readonly RealTimeDataManager _dataManager;
        private readonly ILogger _logger;
        private readonly string _deviceId;
        private readonly string _dataChannel;
        private readonly ScanningParameters _parameters;

        private bool _isScanningActive;
        private bool _isHaltRequested;
        private CancellationTokenSource _cancellationSource;
        private ScanDataCollector _dataCollector;
        private GlobalPeakData _globalPeak;
        private BaselineData _baseline;

        // Constants
        private const int MOTION_SETTLE_TIME_MS = 250;
        private const int MAX_CONSECUTIVE_DECREASES = 3;
        private readonly string[] SCAN_AXES = { "X", "Y", "Z" };

        public event EventHandler<ScanProgressEventArgs> ProgressUpdated;
        public event EventHandler<ScanCompletedEventArgs> ScanCompleted;
        public event EventHandler<ScanErrorEventArgs> ErrorOccurred;
        public event EventHandler<(double Value, Position Position)> DataPointAcquired;
        public event EventHandler<GlobalPeakData> GlobalPeakUpdated;

        public ScanningAlgorithm(
            HexapodMovementService movementService,
            DevicePositionMonitor positionMonitor,
            RealTimeDataManager dataManager,
            string deviceId,
            string dataChannel,
            ScanningParameters parameters,
            ILogger logger)
        {
            _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
            _positionMonitor = positionMonitor ?? throw new ArgumentNullException(nameof(positionMonitor));
            _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
            _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            _dataChannel = dataChannel ?? throw new ArgumentNullException(nameof(dataChannel));
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _logger = logger?.ForContext<ScanningAlgorithm>() ?? throw new ArgumentNullException(nameof(logger));

            _dataCollector = new ScanDataCollector(_deviceId);
            _cancellationSource = new CancellationTokenSource();
        }

        public async Task StartScan(CancellationToken cancellationToken)
        {
            try
            {
                _parameters.Validate();
                _isScanningActive = true;
                _isHaltRequested = false;

                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationSource.Token);

                await RecordBaseline(linkedTokenSource.Token);
                await ExecuteScanSequence(linkedTokenSource.Token);

                // Final return to global peak position
                if (_globalPeak != null && !_isHaltRequested)
                {
                    await MoveToPosition(_globalPeak.Position, linkedTokenSource.Token);
                    _logger.Information($"Scan completed. Returned to global peak position with value: {_globalPeak.Value:F6}");
                }

                OnScanCompleted(new ScanCompletedEventArgs(_dataCollector.GetResults()));
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Scan cancelled");
                await HandleScanCancellation();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during scan execution");
                OnErrorOccurred(new ScanErrorEventArgs(ex));
                throw;
            }
            finally
            {
                _isScanningActive = false;
                await Cleanup();
            }
        }
        // Add this helper method to the class
        private Position ConvertToPosition(DevicePosition devicePosition)
        {
            return new Position
            {
                X = devicePosition.X,
                Y = devicePosition.Y,
                Z = devicePosition.Z,
                U = devicePosition.U,
                V = devicePosition.V,
                W = devicePosition.W
            };
        }
        private async Task RecordBaseline(CancellationToken token)
        {
            _logger.Information("Recording baseline for device {DeviceId}", _deviceId);

            var currentPosition = await _positionMonitor.GetCurrentPosition(_deviceId);
            var currentValue = await GetMeasurement(token);

            _baseline = new BaselineData
            {
                Value = currentValue,
                Position = ConvertToPosition(currentPosition),  // Convert DevicePosition to Position
                Timestamp = DateTime.Now
            };

            // Initialize global peak with baseline
            _globalPeak = new GlobalPeakData
            {
                Value = currentValue,
                Position = ConvertToPosition(currentPosition),  // Convert DevicePosition to Position
                Timestamp = DateTime.Now,
                Context = "Initial Position"
            };

            _dataCollector.RecordBaseline(currentValue, currentPosition);
            _logger.Information($"Baseline recorded: Value={currentValue:F6} at position {currentPosition}");
            OnProgressUpdated(new ScanProgressEventArgs(0, "Baseline recorded"));
        }

        private async Task ExecuteScanSequence(CancellationToken token)
        {
            foreach (var stepSize in _parameters.StepSizes)
            {
                if (!_isScanningActive || token.IsCancellationRequested) break;

                // Use AxesToScan from parameters instead of hardcoded SCAN_AXES
                foreach (var axis in _parameters.AxesToScan)  // This now follows Z, X, Y order
                {
                    if (!_isScanningActive || token.IsCancellationRequested) break;

                    _logger.Information($"Starting {axis} axis scan with step size {stepSize * 1000:F3} microns");

                    var currentStep = Array.IndexOf(_parameters.AxesToScan, axis) +
                                    Array.IndexOf(_parameters.StepSizes, stepSize) * _parameters.AxesToScan.Length;
                    var progress = (double)currentStep / (_parameters.AxesToScan.Length * _parameters.StepSizes.Length);

                    OnProgressUpdated(new ScanProgressEventArgs(
                        progress,
                        $"Scanning {axis} axis with {stepSize * 1000:F3} micron steps"));

                    await ScanAxis(axis, stepSize, token);
                    await Task.Delay(100, token);
                    await ReturnToGlobalPeakIfBetter(token);
                }
            }
        }
        // Add this helper method to get position value for specific axis
        private double GetAxisPosition(DevicePosition position, string axis)
        {
            return axis.ToUpper() switch
            {
                "X" => position.X,
                "Y" => position.Y,
                "Z" => position.Z,
                _ => throw new ArgumentException($"Invalid axis: {axis}")
            };
        }
        private async Task<(double maxValue, Position maxPosition)> ScanDirection(
    string axis, double stepSize, int direction, CancellationToken token)
        {
            var currentPosition = await _positionMonitor.GetCurrentPosition(_deviceId);
            var maxValue = await GetMeasurement(token);
            var maxPosition = ConvertToPosition(currentPosition);
            var previousValue = maxValue;
            int consecutiveDecreases = 0;
            double totalDistance = 0;
            bool hasMovedFromMax = false;
            const double SIGNIFICANT_DECREASE_THRESHOLD = 0.05; // 5% threshold

            while (!token.IsCancellationRequested &&
                   _isScanningActive &&
                   consecutiveDecreases < MAX_CONSECUTIVE_DECREASES &&
                   totalDistance < _parameters.MaxTotalDistance)
            {
                var newPosition = ConvertToPosition(currentPosition);

                // Update specific axis
                switch (axis.ToUpper())
                {
                    case "X":
                        newPosition.X += direction * stepSize;
                        break;
                    case "Y":
                        newPosition.Y += direction * stepSize;
                        break;
                    case "Z":
                        newPosition.Z += direction * stepSize;
                        break;
                    default:
                        throw new ArgumentException($"Invalid axis: {axis}");
                }

                await MoveToPosition(newPosition, token);
                await Task.Delay(_parameters.MotionSettleTimeMs, token);

                currentPosition = await _positionMonitor.GetCurrentPosition(_deviceId);
                var currentValue = await GetMeasurement(token);
                totalDistance += stepSize;

                // Calculate relative decrease from previous value
                double relativeDecrease = (previousValue - currentValue) / previousValue;

                // Calculate gradient for monitoring
                double gradient = (currentValue - previousValue) / stepSize;

                _dataCollector.RecordMeasurement(currentValue, currentPosition, axis, stepSize, direction);
                DataPointAcquired?.Invoke(this, (currentValue, ConvertToPosition(currentPosition)));

                string logMessage = $"{axis} {(direction > 0 ? "+" : "-")}: " +
                    $"Pos={GetAxisPosition(currentPosition, axis):F6}mm, Value={currentValue:F6}";

                _logger.Information(logMessage);

                if (currentValue > maxValue)
                {
                    maxValue = currentValue;
                    maxPosition = ConvertToPosition(currentPosition);
                    consecutiveDecreases = 0;
                    hasMovedFromMax = false;
                    _logger.Information($"{logMessage} - New Local Maximum");
                }
                else
                {
                    consecutiveDecreases++;
                    hasMovedFromMax = true;

                    // Check for significant decrease
                    if (relativeDecrease > SIGNIFICANT_DECREASE_THRESHOLD)
                    {
                        _logger.Information($"Significant decrease detected ({relativeDecrease:P2}). Stopping {axis} axis scan in this direction.");
                        break;
                    }

                    // Check if we should return to local max
                    if (consecutiveDecreases >= MAX_CONSECUTIVE_DECREASES)
                    {
                        _logger.Information($"Consecutive decreases limit reached. Returning to local maximum.");
                        break;
                    }
                }

                previousValue = currentValue;
            }

            // Return to local maximum position if we've moved away from it
            if (hasMovedFromMax)
            {
                _logger.Information($"Returning to local maximum position in {axis} axis");
                await MoveToPosition(maxPosition, token);
                await Task.Delay(_parameters.MotionSettleTimeMs, token);

                // Verify we're at the maximum
                var verificationValue = await GetMeasurement(token);
                _logger.Information($"Local maximum position verified: {verificationValue:F6}");

                // Update maxValue in case there was any drift
                if (verificationValue > maxValue)
                {
                    maxValue = verificationValue;
                }
            }

            return (maxValue, maxPosition);
        }

        private async Task ScanAxis(string axis, double stepSize, CancellationToken token)
        {
            _logger.Information($"Starting {axis} axis scan with step size {stepSize * 1000:F3} microns");

            // Store initial position
            var startPosition = await _positionMonitor.GetCurrentPosition(_deviceId);
            var initialValue = await GetMeasurement(token);

            // Scan in positive direction
            var (positiveMaxValue, positiveMaxPosition) = await ScanDirection(axis, stepSize, 1, token);

            if (token.IsCancellationRequested) return;

            // Return to start position after positive scan
            _logger.Information($"Returning to start position for negative {axis} axis scan");
            await MoveToPosition(ConvertToPosition(startPosition), token);
            await Task.Delay(_parameters.MotionSettleTimeMs, token);

            if (token.IsCancellationRequested) return;

            // Scan in negative direction
            var (negativeMaxValue, negativeMaxPosition) = await ScanDirection(axis, stepSize, -1, token);

            // Determine the best position between positive and negative scans
            var bestValue = Math.Max(positiveMaxValue, negativeMaxValue);
            var bestPosition = positiveMaxValue > negativeMaxValue ? positiveMaxPosition : negativeMaxPosition;

            // Update global peak
            UpdateGlobalPeak(
                bestValue,
                bestPosition,
                $"{axis} axis scan with {stepSize * 1000:F3} micron steps"
            );

            // Move to the best position found in this axis scan
            _logger.Information($"Moving to best position found in {axis} axis scan");
            await MoveToPosition(bestPosition, token);
            await Task.Delay(_parameters.MotionSettleTimeMs, token);
        }
        private async Task ReturnToGlobalPeakIfBetter(CancellationToken token)
        {
            if (_globalPeak == null) return;

            double currentValue = await GetMeasurement(token);
            double improvement = _globalPeak.Value - currentValue;
            double relativeImprovement = improvement / currentValue;

            if (relativeImprovement > _parameters.ImprovementThreshold)
            {
                _logger.Information($"Returning to better position (improvement: {relativeImprovement:P2})");
                await MoveToPosition(_globalPeak.Position, token);

                double verificationValue = await GetMeasurement(token);
                _logger.Information($"Position verified with value: {verificationValue:F6}");
            }
        }

        private void UpdateGlobalPeak(double value, Position position, string context)
        {
            if (_globalPeak == null || value > _globalPeak.Value)
            {
                _globalPeak = new GlobalPeakData
                {
                    Value = value,
                    Position = position,
                    Timestamp = DateTime.Now,
                    Context = context
                };

                GlobalPeakUpdated?.Invoke(this, _globalPeak);
                _logger.Information($"New global peak found: Value={value:F6} at position {position}, Context: {context}");
            }
        }

        private async Task<double> GetMeasurement(CancellationToken token)
        {
            using var timeoutCts = new CancellationTokenSource(_parameters.MeasurementTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    if (_dataManager.TryGetChannelValue(_dataChannel, out var measurement))
                    {
                        if (measurement == null)
                        {
                            _logger.Warning("Measurement is null for channel {Channel}", _dataChannel);
                            await Task.Delay(10, linkedCts.Token);
                            continue;
                        }

                        if (!double.TryParse(measurement.Value.ToString(), out double parsedValue))
                        {
                            _logger.Error("Cannot parse measurement value: {Value}", measurement.Value);
                            throw new FormatException($"Cannot convert measurement value '{measurement.Value}' to number");
                        }

                        return parsedValue;
                    }
                    await Task.Delay(10, linkedCts.Token);
                }

                throw new TimeoutException($"Failed to get measurement within {_parameters.MeasurementTimeout.TotalSeconds} seconds");
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                throw new TimeoutException($"Measurement timeout after {_parameters.MeasurementTimeout.TotalSeconds} seconds");
            }
        }

        private async Task MoveToPosition(Position position, CancellationToken token)
        {
            await _movementService.MoveToAbsolutePosition(new[]
            {
                position.X, position.Y, position.Z,
                position.U, position.V, position.W
            });
        }

        private int GetAxisIndex(string axis) => axis.ToUpper() switch
        {
            "X" => 0,
            "Y" => 1,
            "Z" => 2,
            _ => throw new ArgumentException($"Invalid axis: {axis}")
        };

        private async Task HandleScanCancellation()
        {
            try
            {
                if (_globalPeak != null)
                {
                    _logger.Information("Returning to global peak position after cancellation");
                    await MoveToPosition(_globalPeak.Position, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error returning to global peak position after cancellation");
            }
        }

        private async Task Cleanup()
        {
            try
            {
                _dataCollector.SaveResults();
                _logger.Information("Scan results saved");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cleanup");
            }
        }

        public async Task HaltScan()
        {
            _isHaltRequested = true;
            _isScanningActive = false;
            _cancellationSource.Cancel();

            await HandleScanCancellation();
        }

        protected virtual void OnProgressUpdated(ScanProgressEventArgs e)
        {
            ProgressUpdated?.Invoke(this, e);
        }

        protected virtual void OnScanCompleted(ScanCompletedEventArgs e)
        {
            ScanCompleted?.Invoke(this, e);
        }

        protected virtual void OnErrorOccurred(ScanErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        public void Dispose()
        {
            _cancellationSource?.Dispose();
            _dataCollector?.Dispose();
        }
    }

    public class GlobalPeakData
    {
        public double Value { get; set; }
        public Position Position { get; set; }
        public DateTime Timestamp { get; set; }
        public string Context { get; set; }
    }

    public class BaselineData
    {
        public double Value { get; set; }
        public Position Position { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ScanProgressEventArgs : EventArgs
    {
        public double Progress { get; }
        public string Status { get; }

        public ScanProgressEventArgs(double progress, string status)
        {
            Progress = progress;
            Status = status;
        }
    }

    public class ScanCompletedEventArgs : EventArgs
    {
        public ScanResults Results { get; }

        public ScanCompletedEventArgs(ScanResults results)
        {
            Results = results;
        }
    }

    public class ScanErrorEventArgs : EventArgs
    {
        public Exception Error { get; }

        public ScanErrorEventArgs(Exception error)
        {
            Error = error;
        }
    }
}