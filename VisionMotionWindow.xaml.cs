﻿using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using MotionServiceLib;
using Serilog;
using UaaSolutionWpf.Controls;
using testConfigurableMachine;
using EzIIOLib;
using EzIIOLibControl.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using UaaSolutionWpf.Data;
using System.Windows.Media;
namespace UaaSolutionWpf
{
    /// <summary>
    /// Interaction logic for VisionMotionWindow.xaml
    /// </summary>
    public partial class VisionMotionWindow : Window
    {
        private MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private MultiDeviceManager deviceManager;
        private List<IOPinToggleSwitch> toggleSwitches;

        // Camera Management
        private CameraManagerWpf _cameraManager;
        private float _currentZoom = 1.0f;
        private bool _isCameraConnected = false;
        private bool _isLiveViewRunning = false;
        private System.Windows.Threading.DispatcherTimer _statsUpdateTimer;
        private RealTimeDataManager realTimeDataManager;
        public VisionMotionWindow()
        {
            InitializeComponent();

            // Configure Serilog for logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/vision_motion.log",
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Get a contextualized logger
            _logger = Log.ForContext<VisionMotionWindow>();

            _logger.Information("VisionMotionWindow initialized");

            // Initialize RealTimeDataManager with config
            string realTimeConfigPath = System.IO.Path.Combine("Config", "RealTimeData.json");
            realTimeDataManager = new RealTimeDataManager(realTimeConfigPath, _logger);
            realTimeDataManager.Data.PropertyChanged += Data_PropertyChanged;

            tecController = new TECControllerV2();
            tecController.SetLogger(_logger);

            InitializeKeithleyControl();

        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize UI state
            StatusBarTextBlock.Text = "Ready to initialize motion system";

            // Initialize IO monitors
            InitializeIOMonitors();

            // Initialize toggle switches for quick access
            InitializeToggleSwitches();

            // Initialize stats update timer
            InitializeStatsUpdateTimer();

            //how to click InitializeMotionSystem_Click??
            // Programmatically invoke the InitializeMotionSystem_Click method
            InitializeMotionSystem_Click(this, new RoutedEventArgs());


            await Task.Delay(3000);







            ConnectCamera_Click(this, new RoutedEventArgs());
            await Task.Delay(2000);





            StartLiveView_Click(this, new RoutedEventArgs());
            await Task.Delay(1000);

            // Initialize the static crosshair
            InitializeCrosshair();

            InitializeSliderEvents();
            LoadRealTimeDataChannels();

            // initialize the AutoAlignmentControl
            AutoAlignmentControl.Initialize(_motionKernel, realTimeDataManager, _logger);
            AutoAlignmentControl.SetDataChannel("Keithley Current");
        }

        private void Data_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName.StartsWith("Measurement_"))
            {
                string channelName = e.PropertyName.Substring("Measurement_".Length);
                if (realTimeDataManager.TryGetChannelValue(channelName, out var measurement))
                {
                    // Update UI elements with the new measurement value
                    Dispatcher.Invoke(() =>
                    {
                        // Check if the current selected channel matches the updated channel
                        if (ChannelSelectionComboBox.SelectedItem is RealTimeDataChannel selectedChannel &&
                            selectedChannel.ChannelName == channelName)
                        {
                            // Format the current value with appropriate prefix
                            var (formattedValue, prefixedUnit) = FormatValueWithPrefix(measurement.Value, measurement.Unit);

                            // Get the target value and format it
                            double targetValue = selectedChannel.Target;
                            var (formattedTargetValue, prefixedTargetUnit) = FormatValueWithPrefix(targetValue, measurement.Unit);

                            // Update the current value display
                            CurrentValueTextBlock.Text = formattedValue;
                            CurrentValueUnitTextBlock.Text = prefixedUnit;

                            // Update the target value display
                            TargetValueTextBlock.Text = formattedTargetValue;
                            TargetValueUnitTextBlock.Text = prefixedTargetUnit;

                            // Calculate percentage of target achieved (avoid division by zero)
                            double percentageOfTarget = targetValue != 0
                                ? (measurement.Value / targetValue) * 100
                                : 0;

                            // Update progress bar value
                            TargetProgressBar.Value = Math.Min(percentageOfTarget, 200); // Cap at 200% for display purposes

                            // Update progress bar color based on percentage
                            UpdateProgressBarColor(TargetProgressBar, percentageOfTarget);

                            // Optionally update the percentage text
                            PercentageTextBlock.Text = $"{percentageOfTarget:F1}%";
                        }
                    });
                }
            }
        }

        private (string formattedValue, string prefixedUnit) FormatValueWithPrefix(double value, string unit)
        {
            // Absolute value for comparison
            double absValue = Math.Abs(value);

            // Format based on magnitude
            if (absValue >= 1)
            {
                // No prefix needed
                return (value.ToString("F2"), unit);
            }
            else if (absValue >= 0.001)
            {
                // milli (m)
                return ((value * 1000).ToString("F2"), "m" + unit);
            }
            else if (absValue >= 0.000001)
            {
                // micro (μ)
                return ((value * 1000000).ToString("F2"), "μ" + unit);
            }
            else if (absValue >= 0.000000001)
            {
                // nano (n)
                return ((value * 1000000000).ToString("F2"), "n" + unit);
            }
            else if (absValue > 0)
            {
                // pico (p)
                return ((value * 1000000000000).ToString("F2"), "p" + unit);
            }
            else
            {
                // Zero value
                return ("0.00", unit);
            }
        }

        private void UpdateProgressBarColor(ProgressBar progressBar, double percentage)
        {
            // Create color based on percentage
            Color progressColor;

            if (percentage >= 100)
            {
                // Green for 100% or more
                progressColor = Colors.Green;
            }
            else
            {
                // Gradient from red to yellow to green
                byte r = percentage < 50
                    ? (byte)255
                    : (byte)(255 - ((percentage - 50) * 5.1)); // Fade red from 255 to 0

                byte g = percentage < 50
                    ? (byte)(percentage * 5.1) // Increase green from 0 to 255
                    : (byte)255;

                byte b = 0; // Keep blue at 0

                progressColor = Color.FromRgb(r, g, b);
            }

            // Apply the color to the progress bar's foreground
            progressBar.Foreground = new SolidColorBrush(progressColor);
        }
        private async void InitializeKeithleyControl()
        {
            try
            {
                if (keithleyCurrentControl != null)
                {
                    keithleyCurrentControl.SetDependencies(_logger, realTimeDataManager);
                    keithleyCurrentControl.Init("GPIB0::1::INSTR"); // Specify the GPIB resource name
                    _logger.Information("Initializing Keithley Current Control");

                }
                else
                {
                    _logger.Warning("KeithleyCurrentControl reference is null");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize Keithley Current Control");
                MessageBox.Show(
                    "Failed to initialize Keithley Current Control: " + ex.Message,
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        #region Motion Control
        private string _activeGantryDeviceId;
        private async void InitializeMotionSystem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update UI state
                StatusBarTextBlock.Text = "Initializing motion system...";

                // Create and initialize the motion kernel
                _motionKernel = new MotionKernel();
                await _motionKernel.InitializeAsync();

                // Clear any existing tabs
                DevicesTabControl.Items.Clear();

                // Create tabs for each connected device
                foreach (var device in _motionKernel.GetConnectedDevices())
                {
                    try
                    {
                        // Create a tab for this device
                        var deviceControl = new DeviceControl(_motionKernel, device);

                        var tabItem = new TabItem
                        {
                            Header = $"{device.Name} ({device.Type})",
                            Content = deviceControl
                        };

                        DevicesTabControl.Items.Add(tabItem);
                        //take last device as active gantry device
                        if (device.Type == MotionDeviceType.Gantry)
                        {
                            _activeGantryDeviceId = device.Id;
                        }
                        _logger.Information("Created tab for device {DeviceId} ({DeviceName})", device.Id, device.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error creating tab for device {DeviceId}", device.Id);
                    }
                }

                // Update UI state
                StatusBarTextBlock.Text = "Motion system initialized";

                if (DevicesTabControl.Items.Count == 0)
                {
                    MessageBox.Show("No devices were connected. Check the configuration and try again.",
                        "No Devices", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Add the "No Devices" tab back
                    DevicesTabControl.Items.Add(NoDevicesTab);
                }
                else
                {
                    DevicesTabControl.SelectedIndex = 0;  // Select the first tab
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing motion system");
                StatusBarTextBlock.Text = "Initialization failed";

                MessageBox.Show($"Failed to initialize motion system: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopAllDevices_Click(object sender, RoutedEventArgs e)
        {
            if (_motionKernel == null)
            {
                MessageBox.Show("Please initialize the motion system first.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Update UI state
                StatusBarTextBlock.Text = "Stopping all devices...";

                bool success = await _motionKernel.StopAllDevicesAsync();

                // Update UI state
                if (success)
                {
                    StatusBarTextBlock.Text = "All devices stopped";
                    MessageBox.Show("All devices have been stopped.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = "Failed to stop some devices";
                    MessageBox.Show("Failed to stop one or more devices.", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping all devices");
                StatusBarTextBlock.Text = "Error stopping devices";

                MessageBox.Show($"Error stopping devices: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region IO Device Management

        private void InitializeIOMonitors()
        {
            try
            {
                // Create device manager if it doesn't exist
                if (deviceManager == null)
                {
                    deviceManager = new MultiDeviceManager();

                    // Add devices
                    deviceManager.AddDevice("IOBottom");
                    deviceManager.AddDevice("IOTop");

                    // Connect to devices
                    deviceManager.ConnectAll();
                }

                // Set up pin monitors for IOBottom
                if (outputPinMonitorIOBottom != null)
                {
                    outputPinMonitorIOBottom.DeviceManager = deviceManager;
                    outputPinMonitorIOBottom.DeviceName = "IOBottom";
                    outputPinMonitorIOBottom.PinsSource = deviceManager.GetOutputPins("IOBottom");
                }

                if (inputPinMonitorIOBottom != null)
                {
                    inputPinMonitorIOBottom.DeviceManager = deviceManager;
                    inputPinMonitorIOBottom.DeviceName = "IOBottom";
                    inputPinMonitorIOBottom.PinsSource = deviceManager.GetInputPins("IOBottom");
                }

                // Set up pin monitors for IOTop
                if (outputPinMonitorIOTop != null)
                {
                    outputPinMonitorIOTop.DeviceManager = deviceManager;
                    outputPinMonitorIOTop.DeviceName = "IOTop";
                    outputPinMonitorIOTop.PinsSource = deviceManager.GetOutputPins("IOTop");
                }

                if (inputPinMonitorIOTop != null)
                {
                    inputPinMonitorIOTop.DeviceManager = deviceManager;
                    inputPinMonitorIOTop.DeviceName = "IOTop";
                    inputPinMonitorIOTop.PinsSource = deviceManager.GetInputPins("IOTop");
                }

                _logger.Information("IO monitors initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize IO monitors");
                MessageBox.Show($"Failed to initialize IO monitors: {ex.Message}",
                    "IO Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeToggleSwitches()
        {
            try
            {
                toggleSwitches = new List<IOPinToggleSwitch>();

                var pinConfigs = new[]
                {
                    new { Name = "L_Gripper", Number = 0 },
                    new { Name = "R_Gripper", Number = 2 },
                    new { Name = "Vacuum_Base", Number = 10 },
                    new { Name = "UV_PLC1", Number = 14 },
                    new { Name = "UV_PLC2", Number = 13 }
                };

                // Find or create a panel to add toggle switches to
                StackPanel QuickAccessPanel = null;

                // Find the correct panel in the I/O tab
                var ioTabItem = FindIOTabItem();
                if (ioTabItem != null)
                {
                    // Find or create the quick access panel within the I/O tab
                    var ioGrid = ioTabItem.Content as Grid;
                    if (ioGrid != null)
                    {
                        var quickActionsGroup = FindQuickActionsGroupBox(ioGrid);
                        if (quickActionsGroup != null && quickActionsGroup.Content is WrapPanel wrapPanel)
                        {
                            // Use the existing WrapPanel in the quick actions group
                            wrapPanel.Children.Clear(); // Clear existing buttons

                            foreach (var config in pinConfigs)
                            {
                                var toggleSwitch = CreateToggleSwitch(config.Name, config.Number);
                                wrapPanel.Children.Add(toggleSwitch);
                                toggleSwitches.Add(toggleSwitch);
                            }

                            _logger.Information("Added toggle switches to Quick Actions panel");
                        }
                        else
                        {
                            _logger.Warning("Quick Actions GroupBox or WrapPanel not found");
                        }
                    }
                }

                // Subscribe to device output state changes
                var bottomDevice = deviceManager.GetDevice("IOBottom");
                if (bottomDevice != null)
                {
                    bottomDevice.OutputStateChanged += Device_OutputStateChanged;
                    _logger.Information("Subscribed to OutputStateChanged event");
                }
                else
                {
                    _logger.Warning("IOBottom device not found for OutputStateChanged subscription");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing toggle switches");
                MessageBox.Show($"Error initializing toggle switches: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private IOPinToggleSwitch CreateToggleSwitch(string name, int number)
        {
            var toggleSwitch = new IOPinToggleSwitch
            {
                DeviceName = "IOBottom",
                PinName = name,
                PinNumber = number,
                DeviceManager = deviceManager,
                Margin = new Thickness(5)
            };

            toggleSwitch.PinStateChanged += ToggleSwitch_PinStateChanged;
            toggleSwitch.Error += ToggleSwitch_Error;

            return toggleSwitch;
        }

        private void Device_OutputStateChanged(object sender, (string PinName, bool State) e)
        {
            try
            {
                // Find the toggle switch with the matching pin name
                // Use Dispatcher.Invoke for the entire operation to ensure UI thread access
                Dispatcher.Invoke(() =>
                {
                    var toggle = toggleSwitches.FirstOrDefault(t => t.PinName == e.PinName);
                    if (toggle != null)
                    {
                        toggle.UpdateState(e.State);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating toggle state");
            }
        }
        private void ToggleSwitch_PinStateChanged(object sender, bool newState)
        {
            var toggle = sender as IOPinToggleSwitch;
            _logger.Information("Pin {PinName} state changed to: {State}", toggle?.PinName, newState);
        }

        private void ToggleSwitch_Error(object sender, string errorMessage)
        {
            _logger.Error("Toggle switch error: {Error}", errorMessage);
            MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private TabItem FindIOTabItem()
        {
            // Find the I/O tab in the MotionControlTabControl
            foreach (var item in MotionControlTabControl.Items)
            {
                if (item is TabItem tabItem && tabItem.Header.ToString() == "I/O")
                {
                    return tabItem;
                }
            }
            return null;
        }

        private GroupBox FindQuickActionsGroupBox(Grid ioGrid)
        {
            // Find the "Quick Actions" GroupBox in the I/O tab grid
            foreach (var child in LogicalTreeHelper.GetChildren(ioGrid))
            {
                if (child is StackPanel stackPanel && Grid.GetColumn(stackPanel) == 2)
                {
                    foreach (var stackChild in LogicalTreeHelper.GetChildren(stackPanel))
                    {
                        if (stackChild is GroupBox groupBox && groupBox.Header.ToString() == "Quick Actions")
                        {
                            return groupBox;
                        }
                    }
                }
                else if (child is GroupBox groupBox && groupBox.Header.ToString() == "Quick Actions")
                {
                    return groupBox;
                }
            }
            return null;
        }

        private void CleanupToggleSwitches()
        {
            if (toggleSwitches != null)
            {
                foreach (var toggle in toggleSwitches)
                {
                    toggle.PinStateChanged -= ToggleSwitch_PinStateChanged;
                    toggle.Error -= ToggleSwitch_Error;
                }
                toggleSwitches.Clear();
            }

            if (deviceManager != null)
            {
                var bottomDevice = deviceManager.GetDevice("IOBottom");
                if (bottomDevice != null)
                {
                    bottomDevice.OutputStateChanged -= Device_OutputStateChanged;
                }
            }
        }

        #endregion

        #region Camera Management

        private void InitializeStatsUpdateTimer()
        {
            _statsUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _statsUpdateTimer.Interval = TimeSpan.FromMilliseconds(500); // Update every 500ms
            _statsUpdateTimer.Tick += StatsUpdateTimer_Tick;
        }

        private void StatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_cameraManager != null && _isCameraConnected)
            {
                try
                {
                    // Update only camera FPS
                    CameraStatsTextBlock.Text = $"FPS: {_cameraManager.GetCurrentFps()}";
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error updating camera FPS");
                }
            }
        }

        private void ConnectCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager != null)
                {
                    _logger.Warning("Camera manager already exists. Disconnecting first.");
                    DisconnectCamera();
                }

                _logger.Information("Initializing camera manager");
                _cameraManager = new CameraManagerWpf(CameraImage, _logger);

                // Subscribe to events
                _cameraManager.ImageClicked += CameraManager_ImageClicked;
                _cameraManager.StatsUpdated += CameraManager_StatsUpdated;
                _cameraManager.ImageUpdated += CameraManager_ImageUpdated;

                _logger.Information("Connecting to camera");
                bool connected = _cameraManager.ConnectToCamera();

                if (connected)
                {
                    _isCameraConnected = true;
                    NoCameraTextBlock.Visibility = Visibility.Collapsed;

                    // Update UI
                    ConnectCameraButton.IsEnabled = false;
                    StartLiveViewButton.IsEnabled = true;
                    StopLiveViewButton.IsEnabled = false;
                    DisconnectCameraButton.IsEnabled = true;
                    ZoomInButton.IsEnabled = true;
                    ZoomOutButton.IsEnabled = true;
                    ZoomResetButton.IsEnabled = true;

                    // Update status
                    CameraInfoTextBlock.Text = "Camera: Connected";
                    StatusBarTextBlock.Text = "Camera connected successfully";

                    // Start the stats update timer
                    _statsUpdateTimer.Start();





                }
                else
                {
                    _logger.Error("Failed to connect to camera");
                    MessageBox.Show("Failed to connect to camera. Please check connections and try again.",
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    DisconnectCamera();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to camera");
                MessageBox.Show($"Error connecting to camera: {ex.Message}",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);

                DisconnectCamera();
            }
        }

        private void StartLiveView_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager != null && _isCameraConnected)
                {



                    _cameraManager.StartLiveView();
                    _isLiveViewRunning = true;

                    // Update UI
                    StartLiveViewButton.IsEnabled = false;
                    StopLiveViewButton.IsEnabled = true;
                    StatusBarTextBlock.Text = "Camera live view started";

                    _logger.Information("Camera live view started");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting live view");
                MessageBox.Show($"Error starting live view: {ex.Message}",
                    "Live View Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopLiveView_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager != null && _isLiveViewRunning)
                {
                    _cameraManager.StopLiveView();
                    _isLiveViewRunning = false;

                    // Update UI
                    StartLiveViewButton.IsEnabled = true;
                    StopLiveViewButton.IsEnabled = false;
                    StatusBarTextBlock.Text = "Camera live view stopped";

                    _logger.Information("Camera live view stopped");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping live view");
                MessageBox.Show($"Error stopping live view: {ex.Message}",
                    "Live View Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisconnectCamera_Click(object sender, RoutedEventArgs e)
        {
            DisconnectCamera();
        }

        private void DisconnectCamera()
        {
            try
            {
                if (_cameraManager != null)
                {
                    // Stop live view if running
                    if (_isLiveViewRunning)
                    {
                        _cameraManager.StopLiveView();
                        _isLiveViewRunning = false;
                    }

                    // Unsubscribe from events
                    _cameraManager.ImageClicked -= CameraManager_ImageClicked;
                    _cameraManager.StatsUpdated -= CameraManager_StatsUpdated;
                    _cameraManager.ImageUpdated -= CameraManager_ImageUpdated;

                    // Dispose the camera manager
                    _cameraManager.Dispose();
                    _cameraManager = null;
                    _isCameraConnected = false;

                    // Stop the stats update timer
                    _statsUpdateTimer.Stop();

                    // Update UI
                    NoCameraTextBlock.Visibility = Visibility.Visible;
                    ConnectCameraButton.IsEnabled = true;
                    StartLiveViewButton.IsEnabled = false;
                    StopLiveViewButton.IsEnabled = false;
                    DisconnectCameraButton.IsEnabled = false;
                    ZoomInButton.IsEnabled = false;
                    ZoomOutButton.IsEnabled = false;
                    ZoomResetButton.IsEnabled = false;

                    // Clear image and reset zoom
                    CameraImage.Source = null;
                    _currentZoom = 1.0f;
                    UpdateZoomDisplay();

                    // Update status
                    CameraInfoTextBlock.Text = "Camera: Not connected";
                    CameraStatsTextBlock.Text = "Stats: --";
                    StatusBarTextBlock.Text = "Camera disconnected";

                    _logger.Information("Camera disconnected");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disconnecting camera");
                MessageBox.Show($"Error disconnecting camera: {ex.Message}",
                    "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraManager != null && _isCameraConnected)
            {
                _currentZoom = Math.Min(_currentZoom + 0.1f, 3.0f);
                _cameraManager.SetZoom(_currentZoom);
                UpdateZoomDisplay();
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraManager != null && _isCameraConnected)
            {
                _currentZoom = Math.Max(_currentZoom - 0.1f, 0.5f);
                _cameraManager.SetZoom(_currentZoom);
                UpdateZoomDisplay();
            }
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraManager != null && _isCameraConnected)
            {
                _currentZoom = 1.0f;
                _cameraManager.SetZoom(_currentZoom);
                UpdateZoomDisplay();
            }
        }

        private void UpdateZoomDisplay()
        {
            ZoomTextBlock.Text = $"{_currentZoom * 100:F0}%";
        }


        private async void CameraManager_ImageClicked(object sender, Point point)
        {
            _logger.Information("Image clicked at: X={X}, Y={Y}", point.X, point.Y);


            if (EnableClickToMoveCheckBox.IsChecked == false)
            {
                Log.Debug("Click-to-Move is disabled");
                return;
            }
            // Dynamically find an available gantry device
            var gantryDevice = _motionKernel.GetDevices()
                .FirstOrDefault(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id));

            if (gantryDevice == null)
            {
                _logger.Warning("No connected gantry device found");
                return;
            }

            // Convert pixel coordinates to millimeters
            double pixelToMillimeterFactorX = 0.00248;
            double pixelToMillimeterFactorY = 0.00252;
            double x = point.X * pixelToMillimeterFactorX;
            double y = point.Y * pixelToMillimeterFactorY;

            // Create a 6-element move array with X, Y movement and zeros for other axes
            var move = new double[] { x, y, 0, 0, 0, 0 };

            try
            {
                bool success = await _motionKernel.MoveRelativeAsync(gantryDevice.Id, move);
                if (!success)
                {
                    _logger.Warning("Failed to move relative");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in relative move");
            }
        }

        private void CameraManager_StatsUpdated(object sender, string stats)
        {
            CameraStatsTextBlock.Text = stats;
        }

        // Add this to the CameraManager_ImageUpdated method
        private void CameraManager_ImageUpdated(object sender, ImageUpdatedEventArgs e)
        {
            // Update position marker if we have calibration and motion

        }

        private void UpdateStaticCrosshair()
        {
            if (ImageControlGrid.ActualWidth > 0 && ImageControlGrid.ActualHeight > 0)
            {
                // Center coordinates
                double centerX = ImageControlGrid.ActualWidth / 2;
                double centerY = ImageControlGrid.ActualHeight / 2;

                // Horizontal line
                StaticHorizontalCrosshairLine.X1 = 0;
                StaticHorizontalCrosshairLine.Y1 = centerY;
                StaticHorizontalCrosshairLine.X2 = ImageControlGrid.ActualWidth;
                StaticHorizontalCrosshairLine.Y2 = centerY;

                // Vertical line
                StaticVerticalCrosshairLine.X1 = centerX;
                StaticVerticalCrosshairLine.Y1 = 0;
                StaticVerticalCrosshairLine.X2 = centerX;
                StaticVerticalCrosshairLine.Y2 = ImageControlGrid.ActualHeight;
            }
        }

        // Call this method in the Window_Loaded or after setting the image source
        private void InitializeCrosshair()
        {
            // Update layout first
            ImageControlGrid.UpdateLayout();

            // Set up static crosshair
            UpdateStaticCrosshair();

            // Optional: Adjust for size changes
            ImageControlGrid.SizeChanged += (s, e) =>
            {
                UpdateStaticCrosshair();
            };
        }

        private void ImageControlGrid_MouseMove(object sender, MouseEventArgs e)
        {
            // Existing dynamic crosshair code...
            if (CameraImage.Source == null)
            {
                HideCrosshair();
                return;
            }

            // Get the mouse position relative to the grid
            Point mousePosition = e.GetPosition(ImageControlGrid);

            // Update crosshair lines
            HorizontalCrosshairLine.X1 = 0;
            HorizontalCrosshairLine.Y1 = mousePosition.Y;
            HorizontalCrosshairLine.X2 = ImageControlGrid.ActualWidth;
            HorizontalCrosshairLine.Y2 = mousePosition.Y;

            VerticalCrosshairLine.X1 = mousePosition.X;
            VerticalCrosshairLine.Y1 = 0;
            VerticalCrosshairLine.X2 = mousePosition.X;
            VerticalCrosshairLine.Y2 = ImageControlGrid.ActualHeight;

            // Show crosshair lines
            HorizontalCrosshairLine.Visibility = Visibility.Visible;
            VerticalCrosshairLine.Visibility = Visibility.Visible;
        }

        private void ImageControlGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            HideCrosshair();
        }

        private void HideCrosshair()
        {
            HorizontalCrosshairLine.Visibility = Visibility.Collapsed;
            VerticalCrosshairLine.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Gantry Control Event Handlers

        private async void GantryHomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the gantry device ID
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    // Try to find a gantry device
                    foreach (var device in _motionKernel.GetDevices())
                    {
                        if (device.Type == MotionDeviceType.Gantry && device.IsEnabled &&
                            _motionKernel.IsDeviceConnected(device.Id))
                        {
                            gantryId = device.Id;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(gantryId))
                {
                    MessageBox.Show("No gantry device connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusBarTextBlock.Text = "Moving gantry to home position...";
                bool success = await _motionKernel.HomeDeviceAsync(gantryId);
                StatusBarTextBlock.Text = success ? "Gantry moved to home position." : "Failed to move gantry to home position.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in GantryHomeButton_Click");
                StatusBarTextBlock.Text = "Error moving gantry to home position.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GantryDispense1Button_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("Dispense1");
        }

        private async void GantryDispense2Button_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("Dispense2");
        }

        private async void GantryUVButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("UV");
        }

        private async void GantrySLEDButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("SeeSLED");
        }

        private async void GantryPICButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("SeePIC");
        }

        private async void GantrySNButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("SeeSN");
        }

        private async void DispenseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the settings from the sliders
                double volume = ShotVolumeSlider.Value;
                double time = DispenseTimeSlider.Value;

                StatusBarTextBlock.Text = $"Dispensing with volume {volume:F0}% for {time / 1000:F1} seconds...";

                // Simulate dispense operation - in a real implementation, you would control actual dispense hardware
                // For example, turn on an output pin for the specified time
                if (deviceManager != null)
                {
                    // Turn on dispense pin or control valve
                    await Task.Delay((int)time); // Wait for the specified time
                                                 // Turn off dispense pin or control valve
                }

                StatusBarTextBlock.Text = "Dispense completed.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in DispenseButton_Click");
                StatusBarTextBlock.Text = "Error during dispense operation.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Left Gripper Control Event Handlers

        private async void LeftGripperHomeButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("LeftGripperHome");
        }

        private async void LeftGripLensButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("LeftGripLens");
        }

        private async void LeftReleaseLensButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("LeftReleaseLens");
        }

        private async void LeftRejectLensButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("LeftRejectLens");
        }

        private async void GripLeftButton_Click(object sender, RoutedEventArgs e)
        {
            await ControlGripper("L_Gripper", true);
            LeftGripperStatusText.Text = "Gripping";
        }

        private async void UngripLeftButton_Click(object sender, RoutedEventArgs e)
        {
            await ControlGripper("L_Gripper", false);
            LeftGripperStatusText.Text = "Not gripping";
        }

        #endregion

        #region Right Gripper Control Event Handlers

        private async void RightGripperHomeButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("RightGripperHome");
        }

        private async void RightGripLensButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("RightGripLens");
        }

        private async void RightReleaseLensButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("RightReleaseLens");
        }

        private async void RightRejectLensButton_Click(object sender, RoutedEventArgs e)
        {
            await MoveGantryToPosition("RightRejectLens");
        }

        private async void GripRightButton_Click(object sender, RoutedEventArgs e)
        {
            await ControlGripper("R_Gripper", true);
            RightGripperStatusText.Text = "Gripping";
        }

        private async void UngripRightButton_Click(object sender, RoutedEventArgs e)
        {
            await ControlGripper("R_Gripper", false);
            RightGripperStatusText.Text = "Not gripping";
        }

        #endregion

        #region Vacuum Control Event Handlers

        private async void VacuumOnButton_Click(object sender, RoutedEventArgs e)
        {
            await ControlGripper("Vacuum_Base", true);
        }

        private async void VacuumOffButton_Click(object sender, RoutedEventArgs e)
        {
            await ControlGripper("Vacuum_Base", false);
        }

        #endregion

        #region UV Control Event Handlers

        private async void ActivateUVHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusBarTextBlock.Text = "Activating UV header...";

                // Implement UV header activation logic - could be moving to a position and/or activating pins
                // For example, move to UV position first
                await MoveGantryToPosition("UV");

                StatusBarTextBlock.Text = "UV header activated and ready.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error activating UV header");
                StatusBarTextBlock.Text = "Error activating UV header.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TriggerUV1Button_Click(object sender, RoutedEventArgs e)
        {
            await TriggerUV("UV_PLC1");
        }

        private async void TriggerUV2Button_Click(object sender, RoutedEventArgs e)
        {
            await TriggerUV("UV_PLC2");
        }

        private async void TriggerBothUVButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double intensity = UVIntensitySlider.Value;
                double duration = UVDurationSlider.Value;

                StatusBarTextBlock.Text = $"Triggering both UV1 and UV2 with intensity {intensity:F0}% for {duration / 1000:F1} seconds...";

                // Activate both UV pins
                await ControlGripper("UV_PLC1", true);
                await ControlGripper("UV_PLC2", true);

                // Wait for the specified duration
                await Task.Delay((int)duration);

                // Deactivate both UV pins
                await ControlGripper("UV_PLC1", false);
                await ControlGripper("UV_PLC2", false);

                StatusBarTextBlock.Text = "UV1 and UV2 exposure complete.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error triggering both UV");
                StatusBarTextBlock.Text = "Error during UV exposure.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task TriggerUV(string uvDevice)
        {
            try
            {
                double intensity = UVIntensitySlider.Value;
                double duration = UVDurationSlider.Value;
                string uvName = uvDevice == "UV_PLC1" ? "UV1" : "UV2";

                StatusBarTextBlock.Text = $"Triggering {uvName} with intensity {intensity:F0}% for {duration / 1000:F1} seconds...";

                // Activate UV pin
                await ControlGripper(uvDevice, true);

                // Wait for the specified duration
                await Task.Delay((int)duration);

                // Deactivate UV pin
                await ControlGripper(uvDevice, false);

                StatusBarTextBlock.Text = $"{uvName} exposure complete.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error triggering UV");
                StatusBarTextBlock.Text = "Error during UV exposure.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helper Methods

        private async Task MoveGantryToPosition(string positionName)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the gantry device ID
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    // Try to find a gantry device
                    foreach (var gantrydevice in _motionKernel.GetDevices())
                    {
                        if (gantrydevice.Type == MotionDeviceType.Gantry && gantrydevice.IsEnabled &&
                            _motionKernel.IsDeviceConnected(gantrydevice.Id))
                        {
                            gantryId = gantrydevice.Id;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(gantryId))
                {
                    MessageBox.Show("No gantry device connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if the position exists
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == gantryId);
                if (device == null || !device.Positions.ContainsKey(positionName))
                {
                    // Position doesn't exist, show a message
                    MessageBox.Show($"The position '{positionName}' is not defined. You may need to teach this position first.",
                        "Position Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusBarTextBlock.Text = $"Moving gantry to {positionName} position...";
                bool success = await _motionKernel.MoveToPositionAsync(gantryId, positionName);
                StatusBarTextBlock.Text = success ? $"Gantry moved to {positionName} position." : $"Failed to move gantry to {positionName} position.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveGantryToPosition");
                StatusBarTextBlock.Text = $"Error moving gantry to {positionName} position.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ControlGripper(string gripperName, bool activate)
        {
            try
            {
                if (deviceManager == null)
                {
                    MessageBox.Show("IO system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the pin number from the gripper name
                int pinNumber = -1;
                switch (gripperName)
                {
                    case "L_Gripper":
                        pinNumber = 0; // Use actual pin numbers from your configuration
                        break;
                    case "R_Gripper":
                        pinNumber = 2;
                        break;
                    case "Vacuum_Base":
                        pinNumber = 10;
                        break;
                    case "UV_PLC1":
                        pinNumber = 14;
                        break;
                    case "UV_PLC2":
                        pinNumber = 13;
                        break;
                    default:
                        throw new ArgumentException($"Unknown gripper: {gripperName}");
                }

                // Control the output pin
                string deviceName = "IOBottom"; // Or whichever device controls the grippers
                StatusBarTextBlock.Text = $"{(activate ? "Activating" : "Deactivating")} {gripperName}...";

                var device = deviceManager.GetDevice(deviceName);
                if (device != null)
                {
                    device.SetOutput(gripperName, true);
                    StatusBarTextBlock.Text = $"{gripperName} {(activate ? "activated" : "deactivated")}.";
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to {(activate ? "activate" : "deactivate")} {gripperName}: Device not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in ControlGripper");
                StatusBarTextBlock.Text = $"Error controlling {gripperName}.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        // Add this to the Window_Loaded method to initialize slider value displays
        private void InitializeSliderEvents()
        {
            // Initialize slider value change handlers
            ShotVolumeSlider.ValueChanged += (s, e) =>
            {
                ShotVolumeLabel.Text = $"{e.NewValue:F0}%";
            };

            DispenseTimeSlider.ValueChanged += (s, e) =>
            {
                DispenseTimeLabel.Text = $"{e.NewValue / 1000:F1} sec";
            };

            // UV Control sliders
            UVIntensitySlider.ValueChanged += (s, e) =>
            {
                UVIntensityLabel.Text = $"{e.NewValue:F0}%";
            };

            UVDurationSlider.ValueChanged += (s, e) =>
            {
                UVDurationLabel.Text = $"{e.NewValue / 1000:F1} sec";
            };
        }


        private void LoadRealTimeDataChannels()
        {
            try
            {
                // Load the RealTimeData.json file
                string jsonPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Config", "RealTimeData.json");
                if (System.IO.File.Exists(jsonPath))
                {
                    string jsonContent = System.IO.File.ReadAllText(jsonPath);
                    var realTimeData = System.Text.Json.JsonSerializer.Deserialize<RealTimeDataModel>(jsonContent);

                    if (realTimeData != null && realTimeData.Channels != null)
                    {
                        // Populate the combo box with channel names
                        ChannelSelectionComboBox.ItemsSource = realTimeData.Channels;

                        // Select the first item by default
                        if (realTimeData.Channels.Count > 0)
                        {
                            ChannelSelectionComboBox.SelectedIndex = 0;
                        }

                        _logger.Information("Loaded {Count} channels from RealTimeData.json", realTimeData.Channels.Count);
                    }
                }
                else
                {
                    _logger.Warning("RealTimeData.json file not found at: {Path}", jsonPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading RealTimeData channels");
            }
        }

        private void ChannelSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelSelectionComboBox.SelectedItem is RealTimeDataChannel selectedChannel)
            {
                // Update the display with selected channel information
                CurrentValueTextBlock.Text = selectedChannel.Value.ToString();
                CurrentValueUnitTextBlock.Text = selectedChannel.Unit;

                TargetValueTextBlock.Text = selectedChannel.Target.ToString();
                TargetValueUnitTextBlock.Text = selectedChannel.Unit;

                _logger.Debug("Selected channel: {ChannelName}", selectedChannel.ChannelName);

                AutoAlignmentControl.SetDataChannel(selectedChannel.ChannelName);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Clean up toggle switches
                CleanupToggleSwitches();

                // Cleanup vision/camera
                if (_cameraManager != null)
                {
                    if (_isLiveViewRunning)
                    {
                        _cameraManager.StopLiveView();
                    }
                    _cameraManager.Dispose();
                    _cameraManager = null;
                }

                // Stop stats update timer
                if (_statsUpdateTimer != null)
                {
                    _statsUpdateTimer.Stop();
                    _statsUpdateTimer = null;
                }

                // Dispose of the device manager
                deviceManager?.DisconnectAll();
                deviceManager?.Dispose();

                // Dispose of the motion kernel to clean up resources
                _motionKernel?.Dispose();
                _logger.Information("Window closed, resources cleaned up");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during window closing");
            }
            finally
            {
                base.OnClosed(e);

                // Close the Serilog logger
                Log.CloseAndFlush();
            }
        }

        private void CalibrateVision_Click(object sender, RoutedEventArgs e)
        {

        }

        private void EnableClickToMoveCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void EnableClickToMoveCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {

        }
    }
}