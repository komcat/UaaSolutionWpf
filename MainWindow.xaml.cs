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
        private ILogger _deviceControlLogger;
        private HexapodConnectionManager _hexapodConnectionManager;

        public MainWindow()
        {
            InitializeComponent();


            _deviceControlLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "AARTForm")
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/device_control.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

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
                _hexapodConnectionManager = new HexapodConnectionManager(
                    LeftHexapodControl,
                    BottomHexapodControl,
                    RightHexapodControl
                );

                _deviceControlLogger.Information("Created HexapodConnectionManager instance");

                _hexapodConnectionManager.InitializeConnections();
                _deviceControlLogger.Information("Initialized hexapod connections");
            }
            catch (Exception ex)
            {
                _deviceControlLogger.Error(ex, "Failed to initialize hexapod connections");
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