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

        private ILogger logger;
        private CameraManagerWpf cameraManagerWpf;
        private ChannelConfigurationManager channelConfigurationManager;
        public MainWindow()
        {
            InitializeComponent();


            logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}")
                .WriteTo.File("logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [{Operation}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}")
                .Enrich.FromLogContext()  // This line is important for context properties
                .CreateLogger();
            Log.Logger = logger;


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
            LoadChannelConfigurations();
        }

        #region camera basler 
        private void OnCameraConnected(object sender, CameraConnectionEventArgs e)
        {
            if (e.IsConnected)
            {
                logger.Information("Camera connected: {0}", e.CameraInfo);
            }
            else
            {
                logger.Error("Camera connection failed: {0}", e.ErrorMessage);
            }
        }

        private void OnCameraDisconnected(object sender, CameraConnectionEventArgs e)
        {
            logger.Information("Camera disconnected");
        }

        private void OnLiveViewStarted(object sender, LiveViewEventArgs e)
        {
            if (!e.IsActive)
            {
                logger.Error("Failed to start live view: {0}", e.ErrorMessage);
            }
        }

        private void OnLiveViewStopped(object sender, LiveViewEventArgs e)
        {
            if (e.ErrorMessage != null)
            {
                logger.Error("Error stopping live view: {0}", e.ErrorMessage);
            }
        }

        #endregion

        #region
        private IOManager _ioManager;

        private void InitializeIOMonitorControls()
        {
            _ioManager = new IOManager(EziioControlBottom, EziioControlTop, logger);
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
                    logger
                );
                logger.Information("SimpleJogControl initialized successfully");
            }
            else
            {
                logger.Error("SimpleJogControl reference is null");
            }
        }
        private void InitializePositionManagers()
        {
            // First ensure motion system components are initialized
            if (motionGraphManager == null)
            {
                logger.Error("Motion system not initialized - initializing now");

                string workingPositionsPath = Path.Combine("Config", "WorkingPositions.json");
                positionRegistry = new PositionRegistry(workingPositionsPath, logger);

                devicePositionMonitor = new DevicePositionMonitor(
                    hexapodConnectionManager,
                    gantryConnectionManager,
                    logger,
                    positionRegistry,
                    simulationMode);

                string configPath = Path.Combine("Config", "motionSystem.json");
                motionGraphManager = new MotionGraphManager(
                    devicePositionMonitor,
                    positionRegistry,
                    configPath,
                    logger);
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
                logger);
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
                logger: logger);

            bottomHexapodPositionsManager = new HexapodPositionsManager(
                panel: BottomHexapodManualMoveControl,
                hexapodId: 1,
                hexapodConnectionManager: hexapodConnectionManager,
                positionsToShow: hexapodPositionsToShow,
                customButtonLabels: null,  // use defaults
                motionGraphManager: motionGraphManager,
                positionRegistry: positionRegistry,
                logger: logger);

            rightHexapodPositionsManager = new HexapodPositionsManager(
                panel: RightHexapodManualMoveControl,
                hexapodId: 2,
                hexapodConnectionManager: hexapodConnectionManager,
                positionsToShow: hexapodPositionsToShow,
                customButtonLabels: null,  // use defaults
                motionGraphManager: motionGraphManager,
                positionRegistry: positionRegistry,
                logger: logger);

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
                logger.Information("Created HexapodConnectionManager instance");

                // Initialize movement services for each hexapod
                _hexapodMovementServices = new Dictionary<HexapodConnectionManager.HexapodType, HexapodMovementService>();

                foreach (var kvp in controls)
                {
                    var movementService = new HexapodMovementService(
                        hexapodConnectionManager,
                        kvp.Key,
                        logger
                    );
                    _hexapodMovementServices[kvp.Key] = movementService;

                    // Set dependencies for the control
                    kvp.Value.SetDependencies(movementService);
                }

                hexapodConnectionManager.InitializeConnections();
                logger.Information("Initialized hexapod connections");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to initialize hexapod connections");
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
            // Create the manager with just a logger
            gantryConnectionManager = new AcsGantryConnectionManager(GantryControl, logger);
            gantryMovementService = new GantryMovementService(gantryConnectionManager, logger);

            // Initialize controls with dependencies
            GantryControl.SetDependencies(gantryMovementService);



            // Initialize with a name
            await gantryConnectionManager.InitializeControllerAsync("MainGantry");
            logger.Information("Initialized ACS Gantry connections");
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
                logger.Warning("Running with motors OFF (Simulation Mode)");
            }
            else
            {
                //This is real hardware mode.
                logger.Warning("Running with motors ON");
                try
                {
                    InitializeHexapod();
                    IntiailizeAcsGantry();
                    InitializeJogControl();  // Global Jog control
                    InitializePositionManagers();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to initialize motors");
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
            positionRegistry = new PositionRegistry(workingPositionsPath, logger);
            devicePositionMonitor = new DevicePositionMonitor(hexapodConnectionManager, gantryConnectionManager, logger, positionRegistry, simulationMode);

            string configPath = Path.Combine("Config", "motionSystem.json");

            motionGraphManager = new MotionGraphManager(devicePositionMonitor, positionRegistry, configPath, logger);



            //await InitializeIOMonitorControlsAsync();
            // Initialize camera with automatic connection and live view
            await InitializeCameraAsync();
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

                    logger.Information("Attempting to connect to camera...");

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
                            logger.Information("Started camera live view");
                        }
                        else
                        {
                            logger.Warning("Could not start live view - camera may not be connected");
                        }
                    }
                    else
                    {
                        logger.Error("Could not find camera connect button");
                    }
                }
                else
                {
                    logger.Error("Camera control not initialized");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error initializing camera");
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
                    logger.Information("Started camera live view");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error starting camera live view");
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
                    logger.Information("Stopped camera live view");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error stopping camera live view");
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
            // Test case 1: Position exactly at Home
            var exactHomePosition = new DevicePosition(3)
            {
                X = 3.0,
                Y = 3.0,
                Z = 12.0
            };

            logger.Information("Test Case 1: Exactly at Home position");
            var analysis1 = await motionGraphManager.AnalyzeMovementPath("gantry-main", "SeeSLED", exactHomePosition);
            LogPathAnalysis(analysis1);

            // Test case 2: Position slightly off from Home (1.5mm in X)
            var offHomePosition = new DevicePosition(3)
            {
                X = 4.5,  // 1.5mm off from Home X
                Y = 3.0,
                Z = 12.0
            };

            logger.Information("\nTest Case 2: 1.5mm off from Home position");
            var analysis2 = await motionGraphManager.AnalyzeMovementPath("gantry-main", "SeeSLED", offHomePosition);
            LogPathAnalysis(analysis2);

            // Test case 3: Position way off from any known position
            var arbitraryPosition = new DevicePosition(3)
            {
                X = 50.0,
                Y = 50.0,
                Z = 20.0
            };

            logger.Information("\nTest Case 3: Arbitrary position far from known positions");
            var analysis3 = await motionGraphManager.AnalyzeMovementPath("gantry-main", "SeeSLED", arbitraryPosition);
            LogPathAnalysis(analysis3);
        }

        private void LogPathAnalysis(PathAnalysis analysis)
        {
            if (analysis.IsValid)
            {
                logger.Information($"Device: {analysis.DeviceName} ({analysis.DeviceType})");
                logger.Information($"Starting at: X={analysis.InitialPosition.X:F3}, Y={analysis.InitialPosition.Y:F3}, Z={analysis.InitialPosition.Z:F3}");
                logger.Information($"Closest Known Position: {analysis.CurrentPosition}");
                logger.Information($"Requires Initial Move: {analysis.RequiresInitialMove}");
                logger.Information($"Target Position: {analysis.TargetPosition}");
                logger.Information($"Path: {string.Join(" -> ", analysis.Path)}");
                logger.Information($"Number of steps: {analysis.NumberOfSteps}");
            }
            else
            {
                logger.Error($"Error: {analysis.Error}");
            }
        }

        private void LoadChannelConfigurations()
        {
            
            var configurations = ChannelConfigurationManager.LoadConfiguration();
            ChannelsItemsControl.ItemsSource = configurations;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _ioManager?.Dispose();
            cameraManagerWpf?.Dispose();
            hexapodConnectionManager?.Dispose();
            gantryConnectionManager?.Dispose();
            cameraDisplayViewControl?.Dispose();
        }
    }
}