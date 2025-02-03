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
        private IOService _ioService;
        private IOMonitor _ioMonitor;
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

            



        }

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
            devicePositionMonitor = new DevicePositionMonitor(hexapodConnectionManager, gantryConnectionManager, logger,positionRegistry,simulationMode);

            string configPath = Path.Combine("Config", "motionSystem.json");

            motionGraphManager = new MotionGraphManager(devicePositionMonitor,positionRegistry, configPath, logger);


            await InitializeIODevicesAsync();
            await InitializeIOMonitorAsync();
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

        // Add this to your existing field declarations
        public async Task InitializeIODevicesAsync()
        {
            try
            {
                string configPath = Path.Combine("Config", "IOConfig.json");
                _ioService = new IOService(logger, configPath);
                await _ioService.InitializeAsync();

                // Test the connections by trying to set some outputs
                bool bottomResult = _ioService.SetOutput("IOBottom", "L_Gripper", false);
                bool topResult = _ioService.SetOutput("IOTop", "Output0", false);

                if (bottomResult && topResult)
                {
                    logger.Information("IO devices initialized successfully");
                }
                else
                {
                    logger.Warning("Some IO devices failed to initialize properly");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to initialize IO devices");
                MessageBox.Show(
                    "Failed to initialize IO devices. Check the logs for details.",
                    "IO Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task InitializeIOMonitorAsync()
        {
            try
            {
                logger.Information("Starting IO Monitor initialization");

                _ioMonitor = new IOMonitor(logger, _ioService, monitoringIntervalMs: 100);

                // Add input pins
                var inputPinsToMonitor = new[]
                {
            ("IOTop", "UV_Head_Up"),
            ("IOTop", "UV_Head_Down"),
            ("IOTop", "Dispenser_Head_Up"),
            ("IOTop", "Dispenser_Head_Down"),
            ("IOTop", "Pick_Up_Tool_Up"),
            ("IOTop", "Pick_Up_Tool_Down")
        };

                foreach (var (device, pin) in inputPinsToMonitor)
                {
                    _ioMonitor.AddPinToMonitor(device, pin, IOPinType.Input);
                    logger.Information("Added input pin {Pin} on device {Device} to monitoring", pin, device);
                }

                // Add output pins
                var outputPinsToMonitor = new[]
                {
            ("IOBottom", "L_Gripper"),
            ("IOBottom", "UV_Head"),
            ("IOBottom", "Dispenser_Head"),
            ("IOBottom", "Pick_Up_Tool")
        };

                foreach (var (device, pin) in outputPinsToMonitor)
                {
                    _ioMonitor.AddPinToMonitor(device, pin, IOPinType.Output);
                    logger.Information("Added output pin {Pin} on device {Device} to monitoring", pin, device);
                }

                _ioMonitor.PinStateChanged += (sender, pinStatus) =>
                {
                    logger.Information(
                        "[IO {Type} Change] Device: {DeviceName}, Pin: {PinName}, New State: {State}, " +
                        "Last Update: {LastUpdate}, Update Count: {UpdateCount}",
                        pinStatus.PinType,
                        pinStatus.DeviceName,
                        pinStatus.PinName,
                        pinStatus.State,
                        pinStatus.LastUpdateTime.ToString("HH:mm:ss.fff"),
                        pinStatus.UpdateCount
                    );
                };

                logger.Information("Starting IO monitoring...");
                await _ioMonitor.StartMonitoringAsync();

                logger.Information("IO Monitor initialization completed successfully");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error initializing IO Monitor");
                throw;
            }
        }        // Make sure to clean up when the application closes
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            hexapodConnectionManager?.Dispose();
            gantryConnectionManager?.Dispose();
            try
            {
                if (_ioMonitor != null)
                {
                    logger.Information("Disposing IO Monitor...");
                    _ioMonitor.Dispose();
                    logger.Information("IO Monitor disposed successfully");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disposing IO Monitor");
            }
        }
    }
}