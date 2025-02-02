using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Gantry;

namespace UaaSolutionWpf.Services
{
    public class DevicePosition
    {
        public double[] Coordinates { get; set; }
        public string Name { get; set; }
        public int NumAxes { get; set; }

        public DevicePosition(int numAxes)
        {
            NumAxes = numAxes;
            Coordinates = new double[numAxes];
        }

        public double GetAxis(int index)
        {
            return index < NumAxes ? Coordinates[index] : 0;
        }

        public void SetAxis(int index, double value)
        {
            if (index < NumAxes)
            {
                Coordinates[index] = value;
            }
        }

        // Helper properties for common axes
        public double X
        {
            get => GetAxis(0);
            set => SetAxis(0, value);
        }

        public double Y
        {
            get => GetAxis(1);
            set => SetAxis(1, value);
        }

        public double Z
        {
            get => GetAxis(2);
            set => SetAxis(2, value);
        }

        // Additional axes for Hexapod
        public double U
        {
            get => GetAxis(3);
            set => SetAxis(3, value);
        }

        public double V
        {
            get => GetAxis(4);
            set => SetAxis(4, value);
        }

        public double W
        {
            get => GetAxis(5);
            set => SetAxis(5, value);
        }
    }

    public class DevicePositionMonitor
    {
        private readonly ILogger _logger;
        private readonly HexapodConnectionManager _hexapodManager;
        private readonly AcsGantryConnectionManager _gantryManager;
        private bool _simulationMode = false;
        private readonly PositionRegistry _positionRegistry;

        public DevicePositionMonitor(
            HexapodConnectionManager hexapodManager,
            AcsGantryConnectionManager gantryManager,
            ILogger logger,
            PositionRegistry positionRegistry,
            bool simulationMode=false)
        {
            _hexapodManager = hexapodManager;
            _gantryManager = gantryManager;
            _logger = logger.ForContext<DevicePositionMonitor>();
            _simulationMode= simulationMode;
            _positionRegistry = positionRegistry;
        }

        public async Task<DevicePosition> GetCurrentPosition(string deviceId)
        {
            try
            {
                // Parse device type from ID (e.g., "hex-left", "gantry-main")
                var parts = deviceId.Split('-');
                if (parts.Length != 2)
                {
                    throw new ArgumentException($"Invalid device ID format: {deviceId}");
                }

                if (_simulationMode)
                {
                    switch (parts[0].ToLower())
                    {
                        case "hex":
                            int hexapodId = _positionRegistry.GetHexapodIdFromLocation(parts[1]);
                            if (_positionRegistry.TryGetHexapodPosition(hexapodId, "Home", out Position hexPosition))
                            {
                                var devicePosition = new DevicePosition(6); // Hexapod has 6 axes
                                devicePosition.X = hexPosition.X;
                                devicePosition.Y = hexPosition.Y;
                                devicePosition.Z = hexPosition.Z;
                                devicePosition.U = hexPosition.U;
                                devicePosition.V = hexPosition.V;
                                devicePosition.W = hexPosition.W;
                                devicePosition.Name = "Home";
                                return devicePosition;
                            }
                            throw new InvalidOperationException($"Home position not found for hexapod: {parts[1]}");

                        case "gantry":
                            if (_positionRegistry.TryGetGantryPosition(4, "Home", out Position gantryPosition))
                            {
                                var devicePosition = new DevicePosition(3); // Gantry has 3 axes
                                devicePosition.X = gantryPosition.X;
                                devicePosition.Y = gantryPosition.Y;
                                devicePosition.Z = gantryPosition.Z;
                                devicePosition.Name = "Home";
                                return devicePosition;
                            }
                            throw new InvalidOperationException($"Home position not found for gantry: {parts[1]}");

                        default:
                            throw new ArgumentException($"Unknown device type: {parts[0]}");
                    }
                }
                else
                {
                    switch (parts[0].ToLower())
                    {
                        case "hex":
                            return await GetHexapodPosition(parts[1]);
                        case "gantry":
                            return await GetGantryPosition(parts[1]);
                        default:
                            throw new ArgumentException($"Unknown device type: {parts[0]}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting position for device {DeviceId}", deviceId);
                throw;
            }
        }
        private async Task<DevicePosition> GetHexapodPosition(string location)
        {



            HexapodConnectionManager.HexapodType type = location.ToLower() switch
            {
                "left" => HexapodConnectionManager.HexapodType.Left,
                "right" => HexapodConnectionManager.HexapodType.Right,
                "bottom" => HexapodConnectionManager.HexapodType.Bottom,
                _ => throw new ArgumentException($"Unknown hexapod location: {location}")
            };

            var controller = _hexapodManager.GetHexapodController(type);
            if (controller == null)
            {
                throw new InvalidOperationException($"No controller found for hexapod: {location}");
            }

            var position = new DevicePosition(6); // Hexapod has 6 axes
            var coords = controller.GetPosition();
            position.Coordinates = coords;
            position.Name = controller.WhereAmI();

            return position;
        }

        private async Task<DevicePosition> GetGantryPosition(string location)
        {
            if (!_gantryManager.IsConnected)
            {
                throw new InvalidOperationException("Gantry is not connected");
            }

            var position = new DevicePosition(3); // Gantry has 3 axes
            // Implement getting gantry position based on your AcsGantryConnectionManager
            // This is just an example - adjust based on your actual implementation
            var controller = _gantryManager.GetController();
            position.X = controller.GetAxisStatus(0).position;
            position.Y = controller.GetAxisStatus(1).position;
            position.Z = controller.GetAxisStatus(2).position;

            return position;
        }

        // Optional: Add methods for comparing positions, calculating distances, etc.
        public double CalculateDistance(DevicePosition pos1, DevicePosition pos2)
        {
            if (pos1.NumAxes != pos2.NumAxes)
            {
                throw new ArgumentException("Cannot compare positions with different number of axes");
            }

            double sumOfSquares = 0;
            for (int i = 0; i < pos1.NumAxes; i++)
            {
                double diff = pos1.Coordinates[i] - pos2.Coordinates[i];
                sumOfSquares += diff * diff;
            }

            return Math.Sqrt(sumOfSquares);
        }

        public bool ArePositionsClose(DevicePosition pos1, DevicePosition pos2, double tolerance = 0.001)
        {
            if (pos1.NumAxes != pos2.NumAxes)
            {
                return false;
            }

            for (int i = 0; i < pos1.NumAxes; i++)
            {
                if (Math.Abs(pos1.Coordinates[i] - pos2.Coordinates[i]) > tolerance)
                {
                    return false;
                }
            }

            return true;
        }
    }
}