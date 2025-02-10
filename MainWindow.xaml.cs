using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UaaSolutionWpf.Controls;
using Path = System.IO.Path;
using Serilog;
using Serilog.Core;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.Motion;
using Newtonsoft.Json;
using UaaSolutionWpf.Config;



namespace UaaSolutionWpf
{


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private GantryPositionsManager gantryPositionsManager;
        private HexapodPositionsManager leftHexapodPositionsManager;
        private HexapodPositionsManager bottomHexapodPositionsManager;
        private HexapodPositionsManager rightHexapodPositionsManager;
        private Dictionary<HexapodConnectionManager.HexapodType, HexapodMovementService> _hexapodMovementServices;

        private SimpleJogControl simpleJogControl;

        private HexapodConnectionManager hexapodConnectionManager;

        private GantryMovementService gantryMovementService;
        private AcsGantryConnectionManager gantryConnectionManager;
        private bool simulationMode;

        private MotionGraphManager motionGraphManager;
        private DevicePositionMonitor devicePositionMonitor;
        private PositionRegistry positionRegistry;

        private ILogger _logger;
        private CameraManagerWpf cameraManagerWpf;
        // Add this field at the class level
        private TECControllerV2 _tecController;
        public MainWindow()
        {
            InitializeComponent();


            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}")
                .WriteTo.File("logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [{Operation}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}")
                .Enrich.FromLogContext()  // This line is important for context properties
                .CreateLogger();
            Log.Logger = _logger;


            // Set names for each Hexapod
            if (LeftHexapodControl != null)
                ((HexapodControl)LeftHexapodControl).RobotName = "Left Hexapod";

            if (BottomHexapodControl != null)
                ((HexapodControl)BottomHexapodControl).RobotName = "Bottom Hexapod";

            if (RightHexapodControl != null)
                ((HexapodControl)RightHexapodControl).RobotName = "Right Hexapod";


            InitializeIOMonitorControls();

            // Initialize the camera control
            // Initialize the camera control
            if (cameraDisplayViewControl != null)
            {
                cameraDisplayViewControl.CameraConnected += OnCameraConnected;
                cameraDisplayViewControl.CameraDisconnected += OnCameraDisconnected;
                cameraDisplayViewControl.LiveViewStarted += OnLiveViewStarted;
                cameraDisplayViewControl.LiveViewStopped += OnLiveViewStopped;
            }

            //load sensors channel
            InitializeKeithleyControl();
        }
        private async void InitializeKeithleyControl()
        {



            try
            {
                
                if (_KeithleyCurrentControl != null)
                {
                    _KeithleyCurrentControl.SetLogger(_logger);
                    _KeithleyCurrentControl.Init();
                    _logger.Information("Initializing Keithley Current Control");

                    if (!simulationMode)
                    {
                        try
                        {
                            //await _KeithleyCurrentControl.StartMeasuringAsync();
                            _logger.Information("Keithley Current Control automatically connected and started");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to auto-connect Keithley Current Control");
                            MessageBox.Show(
                                "Failed to automatically connect Keithley: " + ex.Message,
                                "Connection Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        _logger.Information("Keithley Current Control in simulation mode - skipping auto-connect");
                    }
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
        #region camera basler 
        private void OnCameraConnected(object sender, CameraConnectionEventArgs e)
        {
            if (e.IsConnected)
            {
                _logger.Information("Camera connected: {0}", e.CameraInfo);
            }
            else
            {
                _logger.Error("Camera connection failed: {0}", e.ErrorMessage);
            }
        }

        private void OnCameraDisconnected(object sender, CameraConnectionEventArgs e)
        {
            _logger.Information("Camera disconnected");
        }

        private void OnLiveViewStarted(object sender, LiveViewEventArgs e)
        {
            if (!e.IsActive)
            {
                _logger.Error("Failed to start live view: {0}", e.ErrorMessage);
            }
        }

        private void OnLiveViewStopped(object sender, LiveViewEventArgs e)
        {
            if (e.ErrorMessage != null)
            {
                _logger.Error("Error stopping live view: {0}", e.ErrorMessage);
            }
        }

        #endregion

        #region
        private IOManager _ioManager;

        private void InitializeIOMonitorControls()
        {
            _ioManager = new IOManager(EziioControlBottom, EziioControlTop, _logger);
            _ioManager.Initialize();
        }



        #endregion

        private void InitializeJogControl()
        {

            if (SimpleJogControl != null)
            {
                SimpleJogControl.Initialize(
                    _hexapodMovementServices[HexapodConnectionManager.HexapodType.Left],
                    _hexapodMovementServices[HexapodConnectionManager.HexapodType.Right],
                    _hexapodMovementServices[HexapodConnectionManager.HexapodType.Bottom],
                    gantryMovementService,
                    _logger
                );
                _logger.Information("SimpleJogControl initialized successfully");
            }
            else
            {
                _logger.Error("SimpleJogControl reference is null");
            }
        }
        private void InitializePositionManagers()
        {
            // First ensure motion system components are initialized
            if (motionGraphManager == null)
            {
                _logger.Error("Motion system not initialized - initializing now");

                string workingPositionsPath = Path.Combine("Config", "WorkingPositions.json");
                positionRegistry = new PositionRegistry(workingPositionsPath, _logger);

                devicePositionMonitor = new DevicePositionMonitor(
                    hexapodConnectionManager,
                    gantryConnectionManager,
                    _logger,
                    positionRegistry,
                    simulationMode);

                string configPath = Path.Combine("Config", "motionSystem.json");
                motionGraphManager = new MotionGraphManager(
                    devicePositionMonitor,
                    positionRegistry,
                    configPath,
                    _logger);
            }

            string positionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "WorkingPositions.json");

            var gantryPositionsToShow = new[]
            {
                "Home", "Dispense1", "Dispense2", "PreDispense", "UV",
                "SeeCollimateLens", "SeeFocusLens", "SeeSLED", "SeePIC",
                        "SeeGripCollLens","SeeGripFocusLens","CamSeeNumber"
            };

            var hexapodPositionsToShow = new[]
            {
                "Home", "LensGrip", "ApproachLensGrip", "LensPlace",
                "ApproachLensPlace", "AvoidDispenser", "RejectLens", "ParkInside"
            };

            // Initialize Gantry Manager with motion system components
            gantryPositionsManager = new GantryPositionsManager(
                GantryManualMoveControl,
                gantryConnectionManager,
                gantryPositionsToShow,
                null, // default button labels
                motionGraphManager,
                positionRegistry,
                _logger);
            gantryPositionsManager.LoadPositionsAndCreateButtons(positionsPath);

            // Initialize Hexapod Managers with all dependencies in correct order
            leftHexapodPositionsManager = new HexapodPositionsManager(
                panel: LeftHexapodManualMoveControl,
                hexapodId: 0,
                hexapodConnectionManager: hexapodConnectionManager,
                positionsToShow: hexapodPositionsToShow,
                customButtonLabels: null,  // use defaults
                motionGraphManager: motionGraphManager,
                positionRegistry: positionRegistry,
                logger: _logger);

            bottomHexapodPositionsManager = new HexapodPositionsManager(
                panel: BottomHexapodManualMoveControl,
                hexapodId: 1,
                hexapodConnectionManager: hexapodConnectionManager,
                positionsToShow: hexapodPositionsToShow,
                customButtonLabels: null,  // use defaults
                motionGraphManager: motionGraphManager,
                positionRegistry: positionRegistry,
                logger: _logger);

            rightHexapodPositionsManager = new HexapodPositionsManager(
                panel: RightHexapodManualMoveControl,
                hexapodId: 2,
                hexapodConnectionManager: hexapodConnectionManager,
                positionsToShow: hexapodPositionsToShow,
                customButtonLabels: null,  // use defaults
                motionGraphManager: motionGraphManager,
                positionRegistry: positionRegistry,
                logger: _logger);

            // Load positions for each hexapod
            leftHexapodPositionsManager.LoadPositionsAndCreateButtons(positionsPath);
            bottomHexapodPositionsManager.LoadPositionsAndCreateButtons(positionsPath);
            rightHexapodPositionsManager.LoadPositionsAndCreateButtons(positionsPath);
        }
        private void InitializeHexapod()
        {
            try
            {
                var controls = new Dictionary<HexapodConnectionManager.HexapodType, HexapodControl>
                {
                    { HexapodConnectionManager.HexapodType.Left, LeftHexapodControl },
                    { HexapodConnectionManager.HexapodType.Bottom, BottomHexapodControl },
                    { HexapodConnectionManager.HexapodType.Right, RightHexapodControl }
                };

                hexapodConnectionManager = new HexapodConnectionManager(controls);
                _logger.Information("Created HexapodConnectionManager instance");

                // Initialize movement services for each hexapod
                _hexapodMovementServices = new Dictionary<HexapodConnectionManager.HexapodType, HexapodMovementService>();

                foreach (var kvp in controls)
                {
                    var movementService = new HexapodMovementService(
                        hexapodConnectionManager,
                        kvp.Key,
                        _logger
                    );
                    _hexapodMovementServices[kvp.Key] = movementService;

                    // Set dependencies for the control
                    kvp.Value.SetDependencies(movementService);
                }

                hexapodConnectionManager.InitializeConnections();
                _logger.Information("Initialized hexapod connections");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize hexapod connections");
                MessageBox.Show(
                    $"Failed to initialize hexapod connections: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void IntiailizeAcsGantry()
        {
            // Create the manager with just a _logger
            gantryConnectionManager = new AcsGantryConnectionManager(GantryControl, _logger);
            gantryMovementService = new GantryMovementService(gantryConnectionManager, _logger);

            // Initialize controls with dependencies
            GantryControl.SetDependencies(gantryMovementService);



            // Initialize with a name
            await gantryConnectionManager.InitializeControllerAsync("MainGantry");
            _logger.Information("Initialized ACS Gantry connections");
        }



        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Show dialog to ask user about motor connection
            var result = MessageBox.Show(
                "Do you want to connect to the motors?\n\nYes - Connect to motors\nNo - Run in simulation mode",
                "Motor Connection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            simulationMode = (result == MessageBoxResult.No);
            noMotorModeCheckBox.IsChecked = simulationMode;



            if (simulationMode)
            {
                //THis is simulation mode
                _logger.Warning("Running with motors OFF (Simulation Mode)");
            }
            else
            {
                //This is real hardware mode.
                _logger.Warning("Running with motors ON");
                try
                {
                    InitializeHexapod();
                    IntiailizeAcsGantry();
                    InitializeJogControl();  // Global Jog control
                    InitializePositionManagers();

                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to initialize motors");
                    MessageBox.Show(
                        $"Failed to initialize motors: {ex.Message}\nSwitching to simulation mode.",
                        "Initialization Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    simulationMode = true;
                    noMotorModeCheckBox.IsChecked = true;
                }
            }
            string workingPositionsPath = Path.Combine("Config", "WorkingPositions.json");
            positionRegistry = new PositionRegistry(workingPositionsPath, _logger);
            devicePositionMonitor = new DevicePositionMonitor(hexapodConnectionManager, gantryConnectionManager, _logger, positionRegistry, simulationMode);

            string configPath = Path.Combine("Config", "motionSystem.json");

            motionGraphManager = new MotionGraphManager(devicePositionMonitor, positionRegistry, configPath, _logger);



            //await InitializeIOMonitorControlsAsync();
            // Initialize camera with automatic connection and live view
            await InitializeCameraAsync();
            InitializeTECController(); // Add this line
        }




        private async Task InitializeCameraAsync()
        {
            try
            {
                if (cameraDisplayViewControl != null)
                {
                    // Wire up events
                    cameraDisplayViewControl.CameraConnected += OnCameraConnected;
                    cameraDisplayViewControl.CameraDisconnected += OnCameraDisconnected;
                    cameraDisplayViewControl.LiveViewStarted += OnLiveViewStarted;
                    cameraDisplayViewControl.LiveViewStopped += OnLiveViewStopped;

                    _logger.Information("Attempting to connect to camera...");

                    // Simulate clicking the connect button
                    var connectButton = cameraDisplayViewControl.FindName("btnConnect") as Button;
                    if (connectButton != null)
                    {
                        connectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                        // Wait a bit for the connection to establish
                        await Task.Delay(1000);

                        // Start live view if connected
                        var startLiveButton = cameraDisplayViewControl.FindName("btnStartLive") as Button;
                        if (startLiveButton != null && startLiveButton.IsEnabled)
                        {
                            startLiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            _logger.Information("Started camera live view");
                        }
                        else
                        {
                            _logger.Warning("Could not start live view - camera may not be connected");
                        }
                    }
                    else
                    {
                        _logger.Error("Could not find camera connect button");
                    }
                }
                else
                {
                    _logger.Error("Camera control not initialized");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing camera");
                MessageBox.Show($"Error initializing camera: {ex.Message}", "Camera Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task StartCameraLiveViewAsync()
        {
            try
            {
                var startLiveButton = cameraDisplayViewControl?.FindName("btnStartLive") as Button;
                if (startLiveButton != null && startLiveButton.IsEnabled)
                {
                    startLiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    _logger.Information("Started camera live view");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting camera live view");
                throw;
            }
        }

        public async Task StopCameraLiveViewAsync()
        {
            try
            {
                var stopLiveButton = cameraDisplayViewControl?.FindName("btnStopLive") as Button;
                if (stopLiveButton != null && stopLiveButton.IsEnabled)
                {
                    stopLiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    _logger.Information("Stopped camera live view");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping camera live view");
                throw;
            }
        }

        public bool IsCameraLiveViewActive()
        {
            var startLiveButton = cameraDisplayViewControl?.FindName("btnStartLive") as Button;
            var stopLiveButton = cameraDisplayViewControl?.FindName("btnStopLive") as Button;

            if (startLiveButton != null && stopLiveButton != null)
            {
                return !startLiveButton.IsEnabled && stopLiveButton.IsEnabled;
            }
            return false;
        }
        //test graph manager
        private async void TestGraph_Click(object sender, RoutedEventArgs e)
        {
            SetupKeithleyDataHandling();
        }
        // In your MainWindow.xaml.cs
        private async void SetupKeithleyDataHandling()
        {
            // Access the data stream
            var dataStream = _KeithleyCurrentControl.DataStream;

            // Subscribe to batch processing events for real-time updates
            //dataStream.BatchProcessed += (sender, batch) =>
            //{
            //    // Handle the batch of measurements
            //    // For example, update charts or perform analysis
            //    foreach (var point in batch)
            //    {
            //        Console.WriteLine($"Channel {point.ChannelNumber}: {point.Value} {point.Unit} at {point.Timestamp}");
            //    }
            //};

            // You can also get the latest data at any time
            int currentSize = dataStream.BufferSize;
            Console.WriteLine($"Current buffer size: {currentSize}");
        }
        private void LogPathAnalysis(PathAnalysis analysis)
        {
            if (analysis.IsValid)
            {
                _logger.Information($"Device: {analysis.DeviceName} ({analysis.DeviceType})");
                _logger.Information($"Starting at: X={analysis.InitialPosition.X:F3}, Y={analysis.InitialPosition.Y:F3}, Z={analysis.InitialPosition.Z:F3}");
                _logger.Information($"Closest Known Position: {analysis.CurrentPosition}");
                _logger.Information($"Requires Initial Move: {analysis.RequiresInitialMove}");
                _logger.Information($"Target Position: {analysis.TargetPosition}");
                _logger.Information($"Path: {string.Join(" -> ", analysis.Path)}");
                _logger.Information($"Number of steps: {analysis.NumberOfSteps}");
            }
            else
            {
                _logger.Error($"Error: {analysis.Error}");
            }
        }


        // Add this method alongside other initialization methods
        private void InitializeTECController()
        {

            try
            {
                // Find the TEC controller in the XAML
                _tecController = this.FindName("tecControllerV2") as TECControllerV2;
                if (_tecController == null)
                {
                    _logger.Warning("Could not find TECControllerV2 in XAML");
                }
                if (_tecController != null)
                {
                    _tecController.SetLogger(_logger);
                    _tecController.InitializeServices();
                    _logger.Information("Initializing TEC Controller");

                    if (!simulationMode)
                    {
                        try
                        {
                            // Optionally attempt auto-connect
                            //_tecController.ConnectButton_Click(null, null);
                            
                            _logger.Information("TEC Controller initialized");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to initialize TEC Controller");
                            MessageBox.Show(
                                "Failed to initialize TEC Controller: " + ex.Message,
                                "Connection Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        _logger.Information("TEC Controller in simulation mode - skipping initialization");
                    }
                }
                else
                {
                    _logger.Warning("TECController reference is null");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize TEC Controller");
                MessageBox.Show(
                    "Failed to initialize TEC Controller: " + ex.Message,
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _ioManager?.Dispose();
            cameraManagerWpf?.Dispose();
            hexapodConnectionManager?.Dispose();
            gantryConnectionManager?.Dispose();
            cameraDisplayViewControl?.Dispose();
            _KeithleyCurrentControl?.Dispose();  // Add this line to ensure proper cleanup
            _tecController?.Dispose();  // Add this line
        }

        //when main window closing.
        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        // Optional: Add method to access TEC controller from other parts of the application
        public TECControllerV2 GetTECController()
        {
            return _tecController;
        }
    }
}