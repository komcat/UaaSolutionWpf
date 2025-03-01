using System;
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

        }

        #region Motion Control

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
                    // Update camera statistics
                    CameraStatsTextBlock.Text = _cameraManager.GetCameraInfo();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error updating camera stats");
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


            if(EnableClickToMoveCheckBox.IsChecked == false)
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