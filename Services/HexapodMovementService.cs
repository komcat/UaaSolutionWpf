using Serilog;
using System;
using System.Threading.Tasks;

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

                // Get hexapod ID from type
                int hexapodId = _hexapodType switch
                {
                    HexapodConnectionManager.HexapodType.Left => 0,
                    HexapodConnectionManager.HexapodType.Bottom => 1,
                    HexapodConnectionManager.HexapodType.Right => 2,
                    _ => throw new ArgumentException($"Invalid hexapod type: {_hexapodType}")
                };

                // Get target position coordinates
                if (!_positionRegistry.TryGetHexapodPosition(hexapodId, positionName, out var targetPos))
                {
                    throw new InvalidOperationException($"Position {positionName} not found for hexapod {_hexapodType}");
                }

                var controller = _connectionManager.GetHexapodController(_hexapodType);
                if (controller == null)
                {
                    throw new InvalidOperationException($"Controller not found for hexapod {_hexapodType}");
                }

                if (!controller.IsConnected())
                {
                    throw new InvalidOperationException($"Hexapod {_hexapodType} is not connected");
                }

                // Convert to position array
                double[] position = new double[]
                {
                    targetPos.X,
                    targetPos.Y,
                    targetPos.Z,
                    targetPos.U,
                    targetPos.V,
                    targetPos.W
                };

                // Move to absolute position
                await Task.Run(() => controller.MoveToAbsoluteTarget(position));

                _logger.Information("Successfully moved {HexapodType} to position {Position}", _hexapodType, positionName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move {HexapodType} to position {Position}", _hexapodType, positionName);
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
}