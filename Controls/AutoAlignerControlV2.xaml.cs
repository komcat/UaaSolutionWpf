using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using MotionServiceLib;
using UaaSolutionWpf.Data;
using UaaSolutionWpf.Scanning.Core;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Interaction logic for AutoAlignerControlV2.xaml
    /// </summary>
    public partial class AutoAlignerControlV2 : UserControl
    {
        private ILogger _logger;
        private MotionKernel _motionKernel;
        private RealTimeDataManager _dataManager;
        private ScanningAlgorithmV2 _scanningAlgorithm;
        private CancellationTokenSource _scanCts;

        // Device IDs for left and right hexapods
        private string _leftHexapodId;
        private string _rightHexapodId;

        // Current active hexapod
        private string _activeHexapodId;

        // Data channel to monitor
        private string _dataChannel = "Keithley Current";

        public string DataChannel
        {
            get => _dataChannel;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Data channel cannot be null or empty");
                }
                _dataChannel = value;
                // Optionally, add logging or additional logic
                OnDataChannelChanged();
            }
        }

        // Scanning parameters
        private ScanningParameters _coarseScanParameters;
        private ScanningParameters _fineScanParameters;

        // State tracking
        private bool _isScanActive = false;

        // New property to allow external access to scan mode
        public bool IsFineModeSelected
        {
            get => FineModeRadio.IsChecked == true;
            set
            {
                FineModeRadio.IsChecked = value;
                CoarseModeRadio.IsChecked = !value;
            }
        }

        public AutoAlignerControlV2()
        {
            InitializeComponent();
            // Initialize scan parameter sets
            InitializeScanParameters();
        }


        // Alternative method if you prefer
        /// <summary>
        /// Set the data channel to monitor
        /// </summary>
        /// <param name="channelName"></param>
        public void SetDataChannel(string channelName)
        {
            DataChannel = channelName; // This will use the property setter with validation
            if (_logger != null)
                _logger.Information("Data channel set to {ChannelName}", channelName);
        }
        /// <summary>
        /// when data channel changes
        /// </summary>
        private void OnDataChannelChanged()
        {
            // Any additional logic when channel changes
            // For example, updating UI or logging
        }


        /// <summary>
        /// Initialize the motion kernel, data manager and other dependencies
        /// </summary>
        public void Initialize(MotionKernel motionKernel, RealTimeDataManager dataManager, ILogger logger)
        {
            _logger = logger.ForContext<AutoAlignerControlV2>();
            _motionKernel = motionKernel ?? throw new ArgumentNullException(nameof(motionKernel));
            _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));

            // Find the hexapod devices in the system
            FindHexapodDevices();

            // Log initialization
            _logger.Information("AutoAlignerControlV2 initialized with MotionKernel and RealTimeDataManager");
        }

        /// <summary>
        /// Find hexapod devices in the system
        /// </summary>
        private void FindHexapodDevices()
        {
            var devices = _motionKernel.GetDevices();

            foreach (var device in devices)
            {
                if (device.Type == MotionDeviceType.Hexapod && device.IsEnabled)
                {
                    // Assign device IDs based on name (assuming names contain "left" or "right")
                    if (device.Name.ToLower().Contains("left"))
                    {
                        _leftHexapodId = device.Id;
                        _logger.Information("Left hexapod found: {DeviceName} (ID: {DeviceId})", device.Name, device.Id);
                    }
                    else if (device.Name.ToLower().Contains("right"))
                    {
                        _rightHexapodId = device.Id;
                        _logger.Information("Right hexapod found: {DeviceName} (ID: {DeviceId})", device.Name, device.Id);
                    }
                }
            }

            if (string.IsNullOrEmpty(_leftHexapodId) && string.IsNullOrEmpty(_rightHexapodId))
            {
                _logger.Warning("No hexapod devices found");
            }
            else
            {
                if (!string.IsNullOrEmpty(_leftHexapodId))
                    _logger.Information($"Found left hexapod (ID: {_leftHexapodId})");
                if (!string.IsNullOrEmpty(_rightHexapodId))
                    _logger.Information($"Found right hexapod (ID: {_rightHexapodId})");
            }
        }

        /// <summary>
        /// Initialize scanning parameters for coarse and fine modes
        /// </summary>
        private void InitializeScanParameters()
        {
            // Coarse scan parameters - larger steps for faster alignment
            _coarseScanParameters = new ScanningParameters
            {
                AxesToScan = new[] { "Z", "X", "Y" }, // Z first for optical alignment
                StepSizes = new[] { 0.002, 0.001 },  // 2 microns then 1 micron
                MotionSettleTimeMs = 250,
                ConsecutiveDecreasesLimit = 3,
                ImprovementThreshold = 0.01, // 1% improvement threshold
                MaxTotalDistance = 1.0,      // 1 mm max travel per scan
                MeasurementTimeout = TimeSpan.FromSeconds(2)
            };

            // Fine scan parameters - smaller steps for precision alignment
            _fineScanParameters = new ScanningParameters
            {
                AxesToScan = new[] { "Z", "X", "Y" },
                StepSizes = new[] { 0.0005, 0.0002 }, // 500 nanometers then 200 nanometers
                MotionSettleTimeMs = 400,
                ConsecutiveDecreasesLimit = 5,
                ImprovementThreshold = 0.005, // 0.5% improvement threshold
                MaxTotalDistance = 0.5,       // 0.5 mm max travel per scan
                MeasurementTimeout = TimeSpan.FromSeconds(3)
            };
        }

        /// <summary>
        /// Start a scan on the left hexapod
        /// </summary>
        private async void LeftScanButton_Click(object sender, RoutedEventArgs e)
        {
            await StartLeftScan();
        }

        /// <summary>
        /// Public method to start scan on left hexapod
        /// </summary>
        public async Task<bool> StartLeftScan()
        {
            if (_isScanActive)
            {
                _logger.Warning("Cannot start left scan, scan already in progress.");
                return false;
            }

            if (string.IsNullOrEmpty(_leftHexapodId))
            {
                _logger.Error("Left hexapod not found.");
                return false;
            }

            _activeHexapodId = _leftHexapodId;
            _logger.Information("Starting scan on left hexapod...");
            await StartScan();

            return true;
        }

        /// <summary>
        /// Start a scan on the right hexapod
        /// </summary>
        private async void RightScanButton_Click(object sender, RoutedEventArgs e)
        {
            await StartRightScan();
        }

        /// <summary>
        /// Public method to start scan on right hexapod
        /// </summary>
        public async Task<bool> StartRightScan()
        {
            if (_isScanActive)
            {
                _logger.Warning("Cannot start right scan, scan already in progress.");
                return false;
            }

            if (string.IsNullOrEmpty(_rightHexapodId))
            {
                _logger.Error("Right hexapod not found.");
                return false;
            }

            _activeHexapodId = _rightHexapodId;
            _logger.Information("Starting scan on right hexapod...");
            await StartScan();

            return true;
        }

        /// <summary>
        /// Stop the active scanning process
        /// </summary>
        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await StopScan();
        }

        /// <summary>
        /// Public method to stop the active scan
        /// </summary>
        public async Task StopCurrentScan()
        {
            if (!_isScanActive)
            {
                _logger.Information("No scan in progress to stop.");
                return;
            }

            _logger.Information("Stopping scan...");
            await StopScan();
        }

        /// <summary>
        /// Check if a scan is currently active
        /// </summary>
        public bool IsScanActive => _isScanActive;

        /// <summary>
        /// Handle scan mode change to coarse
        /// </summary>
        private void CoarseModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isScanActive)
            {
                // Check if logger is initialized before using it
                if (_logger != null)
                    _logger.Warning("Cannot change scan mode while scan is active.");

                return;
            }

            // Check if logger is initialized before using it
            if (_logger != null)
                _logger.Information("Scan mode set to Coarse.");

            // Update status text even if logger is not available
            SetStatus("Scan mode set to Coarse.");
        }

        /// <summary>
        /// Handle scan mode change to fine
        /// </summary>
        private void FineModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isScanActive)
            {
                // Check if logger is initialized before using it
                if (_logger != null)
                    _logger.Warning("Cannot change scan mode while scan is active.");

                return;
            }

            // Check if logger is initialized before using it
            if (_logger != null)
                _logger.Information("Scan mode set to Fine.");

            // Update status text even if logger is not available
            SetStatus("Scan mode set to Fine.");
        }

        /// <summary>
        /// Start the alignment scan process
        /// </summary>
        private async Task StartScan()
        {
            try
            {
                if (_isScanActive)
                {
                    _logger.Warning("Tried to start a scan while one is already in progress");
                    return;
                }

                if (_motionKernel == null || _dataManager == null)
                {
                    _logger.Error("Motion kernel or data manager not initialized.");
                    return;
                }

                if (!_motionKernel.IsDeviceConnected(_activeHexapodId))
                {
                    _logger.Error($"Selected hexapod (ID: {_activeHexapodId}) is not connected.");
                    return;
                }

                // Create cancellation token source
                _scanCts = new CancellationTokenSource();

                // Determine scan parameters based on selected mode
                var scanParameters = CoarseModeRadio.IsChecked == true ?
                    _coarseScanParameters : _fineScanParameters;

                // Initialize scanning algorithm (using V2 version)
                _scanningAlgorithm = new ScanningAlgorithmV2(
                    _motionKernel,
                    _dataManager,
                    _activeHexapodId,
                    _dataChannel,
                    scanParameters,
                    _logger);

                // Subscribe to events
                _scanningAlgorithm.ProgressUpdated += OnScanProgressUpdated;
                _scanningAlgorithm.DataPointAcquired += OnDataPointAcquired;
                _scanningAlgorithm.GlobalPeakUpdated += OnGlobalPeakUpdated;
                _scanningAlgorithm.ScanCompleted += OnScanCompleted;
                _scanningAlgorithm.ErrorOccurred += OnScanError;

                // Update UI
                UpdateUiForScanStarted();

                // Start scan in background
                _isScanActive = true;

                // Create a TaskCompletionSource to track when scan is complete
                var scanCompletionSource = new TaskCompletionSource<bool>();

                // Handle scan completion to signal the task completion
                EventHandler<ScanCompletedEventArgs> onComplete = null;
                EventHandler<ScanErrorEventArgs> onError = null;

                onComplete = (s, e) => {
                    _scanningAlgorithm.ScanCompleted -= onComplete;
                    _scanningAlgorithm.ErrorOccurred -= onError;
                    scanCompletionSource.TrySetResult(true);
                };

                onError = (s, e) => {
                    _scanningAlgorithm.ScanCompleted -= onComplete;
                    _scanningAlgorithm.ErrorOccurred -= onError;
                    scanCompletionSource.TrySetException(e.Error);
                };

                _scanningAlgorithm.ScanCompleted += onComplete;
                _scanningAlgorithm.ErrorOccurred += onError;

                // Run the scan
                await _scanningAlgorithm.StartScan(_scanCts.Token);

                // Wait for scan to complete
                await scanCompletionSource.Task;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting scan");
                await StopScan();
                throw;
            }
        }

        /// <summary>
        /// Stop the current scan
        /// </summary>
        private async Task StopScan()
        {
            if (!_isScanActive || _scanningAlgorithm == null)
            {
                return;
            }

            try
            {
                // Cancel the scan
                _scanCts?.Cancel();

                // Halt the scanning algorithm
                await _scanningAlgorithm.HaltScan();

                // Clean up
                _scanningAlgorithm.ProgressUpdated -= OnScanProgressUpdated;
                _scanningAlgorithm.DataPointAcquired -= OnDataPointAcquired;
                _scanningAlgorithm.GlobalPeakUpdated -= OnGlobalPeakUpdated;
                _scanningAlgorithm.ScanCompleted -= OnScanCompleted;
                _scanningAlgorithm.ErrorOccurred -= OnScanError;
                _scanningAlgorithm.Dispose();
                _scanningAlgorithm = null;

                // Update UI
                UpdateUiForScanStopped();

                _logger.Information("Scan stopped.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping scan");
            }
            finally
            {
                _isScanActive = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        /// <summary>
        /// Update UI elements when a scan starts
        /// </summary>
        private void UpdateUiForScanStarted()
        {
            Dispatcher.Invoke(() =>
            {
                // Disable mode selection
                CoarseModeRadio.IsEnabled = false;
                FineModeRadio.IsEnabled = false;

                // Update button state
                LeftScanButton.IsEnabled = false;
                RightScanButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            });
        }

        /// <summary>
        /// Update UI elements when a scan stops
        /// </summary>
        private void UpdateUiForScanStopped()
        {
            Dispatcher.Invoke(() =>
            {
                // Re-enable mode selection
                CoarseModeRadio.IsEnabled = true;
                FineModeRadio.IsEnabled = true;

                // Update button state
                LeftScanButton.IsEnabled = true;
                RightScanButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            });
        }

        /// <summary>
        /// Set status text
        /// </summary>
        public void SetStatus(string message)
        {
            if (Dispatcher == null || TextBoxAlignStatus == null)
                return;

            Dispatcher.Invoke(() =>
            {
                if (TextBoxAlignStatus != null)
                    TextBoxAlignStatus.Text = message;
            });
        }

        #region Event Handlers

        /// <summary>
        /// Handle scan progress updates
        /// </summary>
        private void OnScanProgressUpdated(object sender, ScanProgressEventArgs e)
        {
            SetStatus($"Progress: {e.Progress:P0} - {e.Status}");
        }

        /// <summary>
        /// Handle new data points
        /// </summary>
        private void OnDataPointAcquired(object sender, (double Value, Position Position) data)
        {
            // Data point logging can be very verbose - use Debug level to not overwhelm the UI
            _logger.Debug("Data point: Value={Value:F6} at X={X:F6}, Y={Y:F6}, Z={Z:F6}",
                data.Value, data.Position.X, data.Position.Y, data.Position.Z);
        }

        /// <summary>
        /// Handle global peak updates
        /// </summary>
        private void OnGlobalPeakUpdated(object sender, MotionPeakData peak)
        {
            SetStatus($"New peak found: {peak.Value:F6} at X={peak.Position.X:F6}, Y={peak.Position.Y:F6}, Z={peak.Position.Z:F6}");
            _logger.Information("New peak found: {PeakValue:F6} at X={X:F6}, Y={Y:F6}, Z={Z:F6}",
                peak.Value, peak.Position.X, peak.Position.Y, peak.Position.Z);
        }

        /// <summary>
        /// Handle scan completion
        /// </summary>
        private void OnScanCompleted(object sender, ScanCompletedEventArgs e)
        {
            // Log the scan results
            var results = e.Results;
            var peak = results.Peak;
            var baseline = results.Baseline;
            var improvement = (peak.Value - baseline.Value) / baseline.Value;

            SetStatus($"Scan completed! Baseline: {baseline.Value:F6}, Peak: {peak.Value:F6}");
            _logger.Information("Scan completed. Baseline: {BaselineValue:F6}, Peak: {PeakValue:F6}, Improvement: {Improvement:P2}",
                baseline.Value, peak.Value, improvement);
            _isScanActive = false;
            UpdateUiForScanStopped();
        }

        /// <summary>
        /// Handle scan errors
        /// </summary>
        private void OnScanError(object sender, ScanErrorEventArgs e)
        {
            _logger.Error(e.Error, "Scan error occurred");
            SetStatus($"Scan error: {e.Error.Message}");

            _isScanActive = false;
            UpdateUiForScanStopped();
        }

        #endregion
    }
}