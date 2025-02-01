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
        private ILogger _logger;
        private HexapodConnectionManager _hexapodConnectionManager;

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

            InitializePositionManagers();
            InitializeHexapod();
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

                _hexapodConnectionManager = new HexapodConnectionManager(controls);
                _logger.Information("Created HexapodConnectionManager instance");

                _hexapodConnectionManager.InitializeConnections();
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
        // Add cleanup in the Window class
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _hexapodConnectionManager?.Dispose();
           
        }
    }
}