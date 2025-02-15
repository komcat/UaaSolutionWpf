using Serilog;
using System;
using System.Threading.Tasks;
using UaaSolutionWpf.Motion;

namespace UaaSolutionWpf.Services
{
    public class HexapodMovementService
    {
        private readonly HexapodConnectionManager _connectionManager;
        private readonly ILogger _logger;
        private readonly HexapodConnectionManager.HexapodType _hexapodType;
        private readonly PositionRegistry _positionRegistry;

        
        // Add to constructor
        public HexapodMovementService(
            HexapodConnectionManager connectionManager,
            HexapodConnectionManager.HexapodType hexapodType,
            PositionRegistry positionRegistry,
            ILogger logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _hexapodType = hexapodType;
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _logger = logger.ForContext<HexapodMovementService>();
        }
        public async Task MoveToPositionAsync(string positionName)
        {
            try
            {
                _logger.Information("Moving {HexapodType} to position {Position}", _hexapodType, positionName);

                // Get target position coordinates
                if (!_positionRegistry.TryGetHexapodPosition(GetHexapodId(), positionName, out var targetPos))
                {
                    throw new InvalidOperationException($"Position {positionName} not found for hexapod {_hexapodType}");
                }

                var controller = _connectionManager.GetHexapodController(_hexapodType);
                if (controller == null || !controller.IsConnected())
                {
                    throw new InvalidOperationException($"Hexapod {_hexapodType} is not connected");
                }

                // Convert position to array
                double[] targetArray = new double[]
                {
            targetPos.X,
            targetPos.Y,
            targetPos.Z,
            targetPos.U,
            targetPos.V,
            targetPos.W
                };

                // Move to position and wait for completion
                await Task.Run(async () =>
                {
                    await controller.MoveToAbsoluteTarget(targetArray);
                    await controller.WaitForMotionDone();
                });

                _logger.Information("Completed move of {HexapodType} to {Position}", _hexapodType, positionName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving {HexapodType} to {Position}", _hexapodType, positionName);
                throw;
            }
        }
        public async Task MoveRelativeAsync(Axis axis, double distance)
        {
            try
            {
                _logger.Debug("Initiating relative move for {HexapodType} - Axis: {Axis}, Distance: {Distance}",
                    _hexapodType, axis, distance);

                var controller = _connectionManager.GetHexapodController(_hexapodType);
                if (controller == null)
                {
                    _logger.Warning("Cannot move - Hexapod controller not found for type: {HexapodType}", _hexapodType);
                    return;
                }

                if (!controller.IsConnected())
                {
                    _logger.Warning("Cannot move - Hexapod {HexapodType} is not connected", _hexapodType);
                    return;
                }

                // Convert our friendly axis enum to the controller's axis index
                int axisIndex = (int)axis;

                await Task.Run(() => controller.MoveRelative(axisIndex, distance));

                _logger.Information("Completed relative move for {HexapodType} - Axis: {Axis}, Distance: {Distance}",
                    _hexapodType, axis, distance);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute relative move for {HexapodType} - Axis: {Axis}, Distance: {Distance}",
                    _hexapodType, axis, distance);
                throw;
            }
        }
        private int GetHexapodId()
        {
            return _hexapodType switch
            {
                HexapodConnectionManager.HexapodType.Left => 0,
                HexapodConnectionManager.HexapodType.Bottom => 1,
                HexapodConnectionManager.HexapodType.Right => 2,
                _ => throw new ArgumentException($"Invalid hexapod type: {_hexapodType}")
            };
        }
        public async Task<double[]> GetCurrentPositionAsync()
        {
            try
            {
                var controller = _connectionManager.GetHexapodController(_hexapodType);
                if (controller == null)
                {
                    _logger.Warning("Cannot get position - Hexapod controller not found for type: {HexapodType}", _hexapodType);
                    return new double[6]; // Return zeros array
                }

                return await Task.Run(() => controller.GetPosition());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get current position for {HexapodType}", _hexapodType);
                throw;
            }
        }

        public async Task MoveToAbsolutePosition(double[] coordinates)
        {
            try
            {
                if (coordinates == null || coordinates.Length != 6)
                {
                    throw new ArgumentException("Coordinates array must contain exactly 6 values (X,Y,Z,U,V,W)");
                }

                _logger.Debug("Moving {HexapodType} to absolute position: X={X:F6}, Y={Y:F6}, Z={Z:F6}, U={U:F6}, V={V:F6}, W={W:F6}",
                    _hexapodType, coordinates[0], coordinates[1], coordinates[2],
                    coordinates[3], coordinates[4], coordinates[5]);

                var controller = _connectionManager.GetHexapodController(_hexapodType);
                if (controller == null)
                {
                    throw new InvalidOperationException($"Controller not found for hexapod {_hexapodType}");
                }

                if (!controller.IsConnected())
                {
                    throw new InvalidOperationException($"Hexapod {_hexapodType} is not connected");
                }

                // Validate coordinates are within safe limits
                await ValidateCoordinates(coordinates);

                // Move to absolute position
                await Task.Run(() => controller.MoveToAbsoluteTarget(coordinates));

                _logger.Information("Successfully moved {HexapodType} to absolute position", _hexapodType);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move {HexapodType} to absolute position", _hexapodType);
                throw;
            }
        }

        private async Task ValidateCoordinates(double[] coordinates)
        {
            // Get current position for reference
            var currentPosition = await GetCurrentPositionAsync();

            // Maximum allowed movement distance in mm
            const double MAX_SINGLE_MOVE = 5.0;

            // Calculate total distance of movement
            double totalDistance = 0;
            for (int i = 0; i < 6; i++)
            {
                double delta = Math.Abs(coordinates[i] - currentPosition[i]);
                totalDistance += delta * delta;
            }
            totalDistance = Math.Sqrt(totalDistance);

            if (totalDistance > MAX_SINGLE_MOVE)
            {
                throw new InvalidOperationException(
                    $"Requested movement distance ({totalDistance:F3}mm) exceeds safety limit ({MAX_SINGLE_MOVE}mm)");
            }

            // Additional axis-specific limits could be added here
            // For example, checking Z height limits, rotation limits, etc.
        }
        public enum Axis
        {
            X = 0,
            Y = 1,
            Z = 2,
            U = 3,
            V = 4,
            W = 5
        }
    }
    public static class PositionExtensions
    {
        public static double[] ToDoubleArray(this Position position)
        {
            return new double[]
            {
            position.X,
            position.Y,
            position.Z,
            position.U,
            position.V,
            position.W
            };
        }
    }
}