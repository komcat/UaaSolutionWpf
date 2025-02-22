using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Serilog;
using UaaSolutionWpf.Scanning.Core;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.Data;
using System.Windows.Media;

namespace UaaSolutionWpf.Controls
{
    public partial class AutoAlignmentControlWpf : UserControl
    {
        private readonly double[] coarseStepSizes = { 0.0020, 0.0010, 0.0003, 0.0001 };
        private readonly double[] fineStepSizes = { 0.0002, 0.0001 };

        private ILogger _logger;
        private HexapodMovementService _leftHexapodService;
        private HexapodMovementService _rightHexapodService;
        private DevicePositionMonitor _positionMonitor;
        private RealTimeDataManager _realTimeDataManager;
        private CancellationTokenSource _scanCancellation;
        private bool _isScanning;
        private bool _hasLeftHexapod;
        private bool _hasRightHexapod;
        public AutoAlignmentControlWpf()
        {
            InitializeComponent();
            CoarseItem.IsSelected = true;
            UpdateStepSizesDisplay();
            UpdateButtonStates();
        }

        public void Initialize(
    HexapodMovementService leftHexapodService,
    HexapodMovementService rightHexapodService,
    DevicePositionMonitor positionMonitor,
    RealTimeDataManager realTimeDataManager,
    ILogger logger)
        {
            // Store which services are available
            _hasLeftHexapod = leftHexapodService != null;
            _hasRightHexapod = rightHexapodService != null;

            _leftHexapodService = leftHexapodService;
            _rightHexapodService = rightHexapodService;
            _positionMonitor = positionMonitor ?? throw new ArgumentNullException(nameof(positionMonitor));
            _realTimeDataManager = realTimeDataManager ?? throw new ArgumentNullException(nameof(realTimeDataManager));
            _logger = logger?.ForContext<AutoAlignmentControlWpf>();

            // Update UI based on available services
            UpdateServiceAvailability();

            _logger?.Information("AutoAlignmentControlWpf initialized with Left Hexapod: {HasLeft}, Right Hexapod: {HasRight}",
                _hasLeftHexapod, _hasRightHexapod);
        }
        private void UpdateServiceAvailability()
        {
            // Update button states and tooltips
            if (LeftScanButton != null)
            {
                LeftScanButton.IsEnabled = _hasLeftHexapod && !_isScanning;
                LeftScanButton.ToolTip = _hasLeftHexapod ?
                    "Start left hexapod scan" :
                    "Left hexapod is not available";
            }

            if (RightScanButton != null)
            {
                RightScanButton.IsEnabled = _hasRightHexapod && !_isScanning;
                RightScanButton.ToolTip = _hasRightHexapod ?
                    "Start right hexapod scan" :
                    "Right hexapod is not available";
            }

            // Update visual feedback
            if (!_hasLeftHexapod)
            {
                LeftScanButton.Background = Brushes.Gray;
                AddStatus("Left hexapod service is not available");
            }

            if (!_hasRightHexapod)
            {
                RightScanButton.Background = Brushes.Gray;
                AddStatus("Right hexapod service is not available");
            }
        }
        private void ModeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStepSizesDisplay();
        }

        private void UpdateStepSizesDisplay()
        {
            var currentStepSizes = CoarseItem.IsSelected ? coarseStepSizes : fineStepSizes;
            StepSizesText.Text = string.Join(", ", currentStepSizes) + " mm";
        }

        private void UpdateButtonStates()
        {
            bool enableButtons = !_isScanning;
            LeftScanButton.IsEnabled = enableButtons && _hasLeftHexapod;
            RightScanButton.IsEnabled = enableButtons && _hasRightHexapod;
            StopButton.IsEnabled = _isScanning;
            ModeListBox.IsEnabled = enableButtons;
        }

        private async void LeftScanButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("Start left scan");
            await StartScan("left");
        }

        private async void RightScanButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("Start right scan");
            await StartScan("right");
        }


        private async Task StartScan(string direction)
        {
            try
            {
                if (_isScanning)
                {
                    _logger?.Warning("Scan already in progress");
                    return;
                }

                // Validate service availability first
                if (direction == "left" && !_hasLeftHexapod)
                {
                    var message = "Left hexapod service is not available";
                    _logger?.Error(message);
                    AddStatus($"Error: {message}");
                    MessageBox.Show(message, "Service Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                else if (direction == "right" && !_hasRightHexapod)
                {
                    var message = "Right hexapod service is not available";
                    _logger?.Error(message);
                    AddStatus($"Error: {message}");
                    MessageBox.Show(message, "Service Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get appropriate service and device ID
                var hexapodService = direction == "left" ? _leftHexapodService : _rightHexapodService;
                string deviceId = direction == "left" ? "hex-left" : "hex-right";

                _logger?.Information("Starting scan with device {DeviceId}, service available: {ServiceAvailable}",
                    deviceId, hexapodService != null);

                // Prepare scanning parameters
                var scanParameters = ScanningParameters.CreateDefault();
                scanParameters.StepSizes = CoarseItem.IsSelected ? coarseStepSizes : fineStepSizes;

                // Create scanning algorithm
                var scanningAlgorithm = new ScanningAlgorithm(
                    hexapodService,
                    _positionMonitor,
                    _realTimeDataManager,
                    deviceId,
                    "Keithley Current",
                    scanParameters,
                    _logger
                );

                // Subscribe to events
                scanningAlgorithm.ProgressUpdated += OnScanProgressUpdated;
                scanningAlgorithm.ScanCompleted += OnScanCompleted;
                scanningAlgorithm.ErrorOccurred += OnScanError;

                _isScanning = true;
                UpdateButtonStates();
                AddStatus($"Starting {direction} hexapod scan...");

                // Create a new CancellationTokenSource
                _scanCancellation = new CancellationTokenSource();

                try
                {
                    // Start the scan with the cancellation token
                    await scanningAlgorithm.StartScan(_scanCancellation.Token);
                    AddStatus($"{direction} hexapod scan completed successfully");
                }
                catch (OperationCanceledException)
                {
                    _logger?.Information("Scan was cancelled");
                    AddStatus("Scan was cancelled");
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error during scan execution");
                    AddStatus($"Scan error: {ex.Message}");
                    MessageBox.Show(ex.Message, "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // Unsubscribe from events
                    scanningAlgorithm.ProgressUpdated -= OnScanProgressUpdated;
                    scanningAlgorithm.ScanCompleted -= OnScanCompleted;
                    scanningAlgorithm.ErrorOccurred -= OnScanError;

                    _isScanning = false;
                    _scanCancellation?.Dispose();
                    _scanCancellation = null;
                    UpdateButtonStates();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error setting up scan");
                AddStatus($"Setup error: {ex.Message}");
                MessageBox.Show(ex.Message, "Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }        // Event handlers
        private void OnScanProgressUpdated(object sender, ScanProgressEventArgs e)
        {
            AddStatus($"Scan Progress: {e.Progress:P0} - {e.Status}");
        }

        private void OnScanCompleted(object sender, ScanCompletedEventArgs e)
        {
            AddStatus("Scan completed successfully");
            // Optionally process scan results
        }

        private void OnScanError(object sender, ScanErrorEventArgs e)
        {
            AddStatus($"Scan error: {e.Error.Message}");
        }
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _scanCancellation?.Cancel();
                AddStatus("Scan stopped by user");
                _logger?.Information("Scan cancelled by user");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error stopping scan");
                AddStatus($"Error stopping scan: {ex.Message}");
            }
        }

        private void AddStatus(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                StatusTextBox.ScrollToEnd();
            });
        }

        public void Dispose()
        {
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
        }
    }
}