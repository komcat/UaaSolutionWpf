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
using Newtonsoft.Json;


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


        }

        #region
        private IOManager _ioManager;

        private void InitializeIOMonitorControls()
        {
            _ioManager = new IOManager(EziioControlBottom, EziioControlTop, logger);
            _ioManager.Initialize();
        }


        //private void InitializeIOMonitorControls()
        //{
        //    try
        //    {
        //        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        //        string inputMappingFilePath = Path.Combine(appDirectory, "IO", "IOTop.json");
        //        string outputMappingFilePath = Path.Combine(appDirectory, "IO", "IOBottom.json");

        //        // Initialize Bottom Eziio (ID 0) with IP 192.168.0.5
        //        string bottomConfigPath = Path.Combine(appDirectory, "IO", "IOTop.json");
        //        var bottomController = new EziioControllerClass(
        //            EziioController.TCP,
        //            0,  // board ID
        //            "192.168.0.5",  // IP: 192.168.0.5
        //            inputMappingFilePath,
        //            outputMappingFilePath,
        //            logger
        //        );

        //        // Initialize Top Eziio (ID 1) with IP 192.168.0.3
        //        string topConfigPath = Path.Combine(appDirectory, "IO", "IOBottom.json");
        //        var topController = new EziioControllerClass(
        //            EziioControllerClass.TCP,
        //            1,  // board ID
        //            "192.168.0.3",  // IP: 192.168.0.3
        //            inputMappingFilePath,
        //            outputMappingFilePath,
        //            logger
        //        );

        //        // Connect the controllers
        //        if (!bottomController.Connect())
        //        {
        //            throw new Exception("Failed to connect to Bottom Eziio controller");
        //        }
        //        if (!topController.Connect())
        //        {
        //            throw new Exception("Failed to connect to Top Eziio controller");
        //        }

        //        // Assign to UI controls
        //        EziioControlBottom.DataContext = bottomController;
        //        EziioControlTop.DataContext = topController;

        //        logger.Information("Successfully initialized IO monitor controls");
        //    }
        //    catch (Exception ex)
        //    {
        //        logger.Error(ex, "Failed to initialize IO monitor controls");
        //        MessageBox.Show(
        //            $"Failed to initialize IO controls: {ex.Message}",
        //            "Initialization Error",
        //            MessageBoxButton.OK,
        //            MessageBoxImage.Error);
        //    }
        //}

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
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "WorkingPositions.json");

            // Initialize Gantry Positions
            var gantryPositionsToShow = new[]
            {
                "Home",
                "Dispense1",
                "Dispense2",
                "PreDispense",
                "UV",
                "SeeCollimateLens",
                "SeeFocusLens",
                "SeeSLED",
                "SeePIC",
                "CamSeeNumber"
            };

            // Initialize Hexapod Positions
            var hexapodPositionsToShow = new[]
            {
                "Home",
                "LensGrip",
                "ApproachLensGrip",
                "LensPlace",
                "ApproachLensPlace",
                "AvoidDispenser",
                "RejectLens",
                "ParkInside"
            };

            // Initialize Gantry Manager
            gantryPositionsManager = new GantryPositionsManager(GantryManualMoveControl, gantryPositionsToShow);
            gantryPositionsManager.LoadPositionsAndCreateButtons(configPath);

            // Initialize Hexapod Managers
            leftHexapodPositionsManager = new HexapodPositionsManager(LeftHexapodManualMoveControl, 0, hexapodPositionsToShow);
            bottomHexapodPositionsManager = new HexapodPositionsManager(BottomHexapodManualMoveControl, 1, hexapodPositionsToShow);
            rightHexapodPositionsManager = new HexapodPositionsManager(RightHexapodManualMoveControl, 2, hexapodPositionsToShow);

            // Load positions for each hexapod
            leftHexapodPositionsManager.LoadPositionsAndCreateButtons(configPath);
            bottomHexapodPositionsManager.LoadPositionsAndCreateButtons(configPath);
            rightHexapodPositionsManager.LoadPositionsAndCreateButtons(configPath);
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



        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _ioManager?.Dispose();
            hexapodConnectionManager?.Dispose();
            gantryConnectionManager?.Dispose();

        }
    }
}