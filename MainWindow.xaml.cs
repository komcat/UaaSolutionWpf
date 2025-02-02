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
        private bool noMotorMode;

        private MotionGraphManager motionManager;

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

            InitializePositionManagers();



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

        // Add cleanup in the Window class
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            hexapodConnectionManager?.Dispose();
            gantryConnectionManager?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            noMotorMode = false;
            if (noMotorModeCheckBox.IsChecked == true)
            {
                noMotorMode = true;
                logger.Warning("Running with motors OFF");
            }
            logger.Warning("Running with motors ON");


            if (noMotorModeCheckBox.IsChecked == false)
            {
                InitializeHexapod();
                IntiailizeAcsGantry();
                InitializeJogControl();  // Global Jog control
            }
        }

        private async void InitializeMotionGraphManagers()
        {
            // In your main window or controller:
            motionManager = new MotionGraphManager(
                hexapodConnectionManager,
                gantryConnectionManager,
                "Config/motionSystem.json",
                logger
            );

            // Move a specific device
            await motionManager.MoveDeviceToPosition("hex-left", "Home");
            await motionManager.MoveDeviceToPosition("gantry-main", "PreDispense");

            // Get all configured devices
            var devices = motionManager.GetConfiguredDevices();
        }
    }
}