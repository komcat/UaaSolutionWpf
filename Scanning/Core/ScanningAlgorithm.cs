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

        public event EventHandler<ScanProgressEventArgs> ProgressUpdated;
        public event EventHandler<ScanCompletedEventArgs> ScanCompleted;
        public event EventHandler<ScanErrorEventArgs> ErrorOccurred;

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

                await InitializeScan(linkedTokenSource.Token);
                await ExecuteScanSequence(linkedTokenSource.Token);
                await FinalizeScan(linkedTokenSource.Token);

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

        private async Task InitializeScan(CancellationToken token)
        {
            _logger.Information("Initializing scan for device {DeviceId}", _deviceId);

            // Record baseline measurement
            var baselinePosition = await _positionMonitor.GetCurrentPosition(_deviceId);
            var baselineValue = await GetMeasurement(token);

            _dataCollector.RecordBaseline(baselineValue, baselinePosition);

            OnProgressUpdated(new ScanProgressEventArgs(0, "Scan initialized"));
        }

        private async Task ExecuteScanSequence(CancellationToken token)
        {
            int totalSteps = _parameters.AxesToScan.Length * _parameters.StepSizes.Length;
            int currentStep = 0;

            foreach (var stepSize in _parameters.StepSizes)
            {
                if (!_isScanningActive || token.IsCancellationRequested) break;

                foreach (var axis in _parameters.AxesToScan)
                {
                    if (!_isScanningActive || token.IsCancellationRequested) break;

                    await ScanAxis(axis, stepSize, token);
                    currentStep++;

                    double progress = (double)currentStep / totalSteps;
                    OnProgressUpdated(new ScanProgressEventArgs(
                        progress,
                        $"Completed {axis} axis scan with {stepSize * 1000:F3} micron steps"));

                    await ReturnToOptimalPosition(token);
                }
            }
        }

        private async Task ScanAxis(string axis, double stepSize, CancellationToken token)
        {
            _logger.Information("Starting {Axis} axis scan with step size {StepSize:F3} microns",
                axis, stepSize * 1000);

            // Scan in positive direction
            await ScanDirection(axis, stepSize, 1, token);

            if (token.IsCancellationRequested) return;

            // Return to start position
            var startPosition = _dataCollector.GetBaselinePosition();
            await MoveToPosition(startPosition, token);

            if (token.IsCancellationRequested) return;

            // Scan in negative direction
            await ScanDirection(axis, stepSize, -1, token);
        }

        private async Task ScanDirection(string axis, double stepSize, int direction, CancellationToken token)
        {
            int consecutiveDecreases = 0;
            double previousValue = await GetMeasurement(token);
            double totalDistance = 0;

            while (!token.IsCancellationRequested &&
                   _isScanningActive &&
                   consecutiveDecreases < _parameters.ConsecutiveDecreasesLimit &&
                   totalDistance < _parameters.MaxTotalDistance)
            {
                await MoveSingleStep(axis, stepSize * direction, token);
                totalDistance += stepSize;

                await Task.Delay(_parameters.MotionSettleTimeMs, token);

                var currentPosition = await _positionMonitor.GetCurrentPosition(_deviceId);
                var currentValue = await GetMeasurement(token);

                _dataCollector.RecordMeasurement(currentValue, currentPosition, axis, stepSize, direction);

                if (currentValue > _dataCollector.GetPeakValue())
                {
                    consecutiveDecreases = 0;
                    _logger.Information("New peak found: {Value:F6}", currentValue);
                }
                else
                {
                    consecutiveDecreases++;
                }

                previousValue = currentValue;
            }
        }

        private async Task ReturnToOptimalPosition(CancellationToken token)
        {
            var optimalPosition = _dataCollector.GetPeakPosition();
            if (optimalPosition == null) return;

            var currentValue = await GetMeasurement(token);
            var peakValue = _dataCollector.GetPeakValue();

            double improvement = (peakValue - currentValue) / currentValue;
            if (improvement > _parameters.ImprovementThreshold)
            {
                _logger.Information("Returning to optimal position (improvement: {Improvement:P2})", improvement);
                await MoveToPosition(optimalPosition, token);
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
                        return measurement.Value;
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

        private async Task MoveSingleStep(string axis, double step, CancellationToken token)
        {
            switch (axis.ToUpper())
            {
                case "X":
                    await _movementService.MoveRelativeAsync(HexapodMovementService.Axis.X, step);
                    break;
                case "Y":
                    await _movementService.MoveRelativeAsync(HexapodMovementService.Axis.Y, step);
                    break;
                case "Z":
                    await _movementService.MoveRelativeAsync(HexapodMovementService.Axis.Z, step);
                    break;
                default:
                    throw new ArgumentException($"Invalid axis: {axis}");
            }
        }

        private async Task MoveToPosition(Position position, CancellationToken token)
        {
            var coordinates = new[]
            {
                position.X, position.Y, position.Z,
                position.U, position.V, position.W
            };

            await _movementService.MoveToAbsolutePosition(coordinates);
        }
        private async Task FinalizeScan(CancellationToken token)
        {
            try
            {
                _logger.Information("Finalizing scan for device {DeviceId}", _deviceId);

                // Return to best position found during scan
                var peakPosition = _dataCollector.GetPeakPosition();
                if (peakPosition != null)
                {
                    _logger.Information("Moving to optimal position found during scan");
                    await MoveToPosition(peakPosition, token);
                    await Task.Delay(_parameters.MotionSettleTimeMs, token);

                    // Verify final position
                    var finalValue = await GetMeasurement(token);
                    var peakValue = _dataCollector.GetPeakValue();

                    double finalImprovement = (finalValue - _dataCollector.GetBaselineValue()) / _dataCollector.GetBaselineValue();

                    _logger.Information(
                        "Scan completed - Initial: {BaselineValue:E3}, Peak: {PeakValue:E3}, Final: {FinalValue:E3}, " +
                        "Total Improvement: {Improvement:P2}",
                        _dataCollector.GetBaselineValue(),
                        peakValue,
                        finalValue,
                        finalImprovement
                    );
                }
                else
                {
                    _logger.Warning("No peak position found during scan");
                }

                // Save scan results
                _dataCollector.SaveResults();

                // Generate final scan report
                var report = GenerateScanReport();
                _logger.Information("Scan Summary:\n{Report}", report);

                OnProgressUpdated(new ScanProgressEventArgs(1.0, "Scan finalized"));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during scan finalization");
                throw;
            }
        }

        private string GenerateScanReport()
        {
            var results = _dataCollector.GetResults();
            var stats = results.Statistics;

            return $"""
        Scan Report:
        =============
        Device: {_deviceId}
        Duration: {stats.TotalDuration:hh\\:mm\\:ss}
        Total Measurements: {stats.TotalMeasurements}
        
        Results:
        --------
        Baseline Value: {results.Baseline.Value:E3}
        Peak Value: {results.Peak.Value:E3}
        Improvement: {((results.Peak.Value - results.Baseline.Value) / results.Baseline.Value):P2}
        
        Statistics:
        -----------
        Min Value: {stats.MinValue:E3}
        Max Value: {stats.MaxValue:E3}
        Average: {stats.AverageValue:E3}
        Std Dev: {stats.StandardDeviation:E3}
        
        Measurements per Axis:
        --------------------
        {string.Join("\n", stats.MeasurementsPerAxis.Select(x => $"{x.Key}: {x.Value}"))}
        """;
        }
        private async Task HandleScanCancellation()
        {
            try
            {
                var optimalPosition = _dataCollector.GetPeakPosition();
                if (optimalPosition != null)
                {
                    _logger.Information("Returning to optimal position after cancellation");
                    await MoveToPosition(optimalPosition, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error returning to optimal position after cancellation");
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