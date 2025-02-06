using System;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;

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
        private readonly PositionRegistry _positionRegistry;
        private bool _simulationMode = false;

        private const double POSITION_TOLERANCE = 0.1; // 0.1mm tolerance for position matching

        public DevicePositionMonitor(
            HexapodConnectionManager hexapodManager,
            AcsGantryConnectionManager gantryManager,
            ILogger logger,
            PositionRegistry positionRegistry,
            bool simulationMode = false)
        {
            _hexapodManager = hexapodManager;
            _gantryManager = gantryManager;
            _logger = logger.ForContext<DevicePositionMonitor>();
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _simulationMode = simulationMode;
        }

        public async Task<DevicePosition> GetCurrentPosition(string deviceId)
        {
            try
            {
                var parts = deviceId.Split('-');
                if (parts.Length != 2)
                {
                    throw new ArgumentException($"Invalid device ID format: {deviceId}");
                }

                if (_simulationMode)
                {
                    return await GetSimulatedPosition(parts[0], parts[1]);
                }

                return parts[0].ToLower() switch
                {
                    "hex" => await GetHexapodPosition(parts[1]),
                    "gantry" => await GetGantryPosition(parts[1]),
                    _ => throw new ArgumentException($"Unknown device type: {parts[0]}")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting position for device {DeviceId}", deviceId);
                throw;
            }
        }

        private async Task<DevicePosition> GetSimulatedPosition(string deviceType, string location)
        {
            switch (deviceType.ToLower())
            {
                case "hex":
                    int hexapodId = _positionRegistry.GetHexapodIdFromLocation(location);
                    if (_positionRegistry.TryGetHexapodPosition(hexapodId, "Home", out Position hexPosition))
                    {
                        return CreateDevicePosition(hexPosition, 6, "Home");
                    }
                    throw new InvalidOperationException($"Home position not found for hexapod: {location}");

                case "gantry":
                    if (_positionRegistry.TryGetGantryPosition(4, "Home", out Position gantryPosition))
                    {
                        return CreateDevicePosition(gantryPosition, 3, "Home");
                    }
                    throw new InvalidOperationException($"Home position not found for gantry: {location}");

                default:
                    throw new ArgumentException($"Unknown device type: {deviceType}");
            }
        }

        private DevicePosition CreateDevicePosition(Position position, int numAxes, string name)
        {
            var devicePosition = new DevicePosition(numAxes)
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Name = name
            };

            if (numAxes > 3)
            {
                devicePosition.U = position.U;
                devicePosition.V = position.V;
                devicePosition.W = position.W;
            }

            return devicePosition;
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

            var currentCoords = controller.GetPosition();
            var position = new DevicePosition(6)
            {
                Coordinates = currentCoords
            };

            // Find the closest named position in the WorkingPositions.json registry
            int hexapodId = (int)type;
            position.Name = FindClosestPosition(hexapodId, position);

            return position;
        }

        private async Task<DevicePosition> GetGantryPosition(string location)
        {
            if (!_gantryManager.IsConnected)
            {
                throw new InvalidOperationException("Gantry is not connected");
            }

            var position = new DevicePosition(3);
            var controller = _gantryManager.GetController();

            position.X = controller.GetAxisStatus(0).position;
            position.Y = controller.GetAxisStatus(1).position;
            position.Z = controller.GetAxisStatus(2).position;

            // Find the closest named position in the WorkingPositions.json registry
            position.Name = FindClosestGantryPosition(position);

            return position;
        }

        private string FindClosestPosition(int hexapodId, DevicePosition currentPosition)
        {
            string closestPosition = null;
            double minDistance = double.MaxValue;

            var allPositions = _positionRegistry.GetAllHexapodPositions(hexapodId);
            foreach (var position in allPositions)
            {
                double distance = CalculateDistance(
                    currentPosition,
                    new double[] { position.Value.X, position.Value.Y, position.Value.Z,
                                  position.Value.U, position.Value.V, position.Value.W }
                );

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestPosition = position.Key;
                }
            }

            _logger.Debug(
                "Hexapod {HexapodId} closest to position {Position} (distance: {Distance:F3}mm)",
                hexapodId, closestPosition, minDistance
            );

            return closestPosition;
        }

        private string FindClosestGantryPosition(DevicePosition currentPosition)
        {
            string closestPosition = null;
            double minDistance = double.MaxValue;

            var allPositions = _positionRegistry.GetAllGantryPositions(4);
            foreach (var position in allPositions)
            {
                double distance = CalculateDistance(
                    currentPosition,
                    new double[] { position.Value.X, position.Value.Y, position.Value.Z }
                );

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestPosition = position.Key;
                }
            }

            _logger.Debug(
                "Gantry closest to position {Position} (distance: {Distance:F3}mm)",
                closestPosition, minDistance
            );

            return closestPosition;
        }

        public double CalculateDistance(DevicePosition pos1, double[] coords2)
        {
            double sumOfSquares = 0;
            int dimensions = Math.Min(pos1.NumAxes, coords2.Length);

            for (int i = 0; i < dimensions; i++)
            {
                double diff = pos1.Coordinates[i] - coords2[i];
                sumOfSquares += diff * diff;
            }

            return Math.Sqrt(sumOfSquares);
        }

        public bool ArePositionsClose(DevicePosition pos1, DevicePosition pos2, double tolerance = POSITION_TOLERANCE)
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