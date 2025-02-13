using Serilog;
using System;
using System.Threading.Tasks;
using UaaSolutionWpf.Gantry;

namespace UaaSolutionWpf.Services
{
    public class GantryMovementService
    {
        private readonly AcsGantryConnectionManager _connectionManager;
        private readonly ILogger _logger;

        private readonly PositionRegistry _positionRegistry;

        // Add to constructor
        public GantryMovementService(
            AcsGantryConnectionManager connectionManager,
            PositionRegistry positionRegistry,
            ILogger logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _logger = logger.ForContext<GantryMovementService>();
        }
        public async Task MoveToPositionAsync(string positionName)
        {
            try
            {
                _logger.Information("Moving gantry to position {Position}", positionName);

                if (!_connectionManager.IsConnected)
                {
                    throw new InvalidOperationException("Gantry is not connected");
                }

                // Get target position coordinates
                if (!_positionRegistry.TryGetGantryPosition(4, positionName, out var targetPos))
                {
                    throw new InvalidOperationException($"Position {positionName} not found for gantry");
                }

                // Move each axis to its target position
                var tasks = new[]
                {
                    _connectionManager.MoveToAbsolutePositionAsync(0, targetPos.X),
                    _connectionManager.MoveToAbsolutePositionAsync(1, targetPos.Y),
                    _connectionManager.MoveToAbsolutePositionAsync(2, targetPos.Z)
                };

                await Task.WhenAll(tasks);

                _logger.Information("Successfully moved gantry to position {Position}", positionName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move gantry to position {Position}", positionName);
                throw;
            }
        }
        public async Task MoveRelativeAsync(int axis, double distance)
        {
            try
            {
                _logger.Debug("Initiating relative move - Axis: {Axis}, Distance: {Distance}", axis, distance);

                if (!_connectionManager.IsConnected)
                {
                    _logger.Warning("Cannot move - Gantry is not connected");
                    return;
                }

                // Validate axis enable state
                bool isEnabled = await IsAxisEnabledAsync(axis);
                if (!isEnabled)
                {
                    _logger.Warning("Cannot move - Axis {Axis} is not enabled", axis);
                    return;
                }

                await _connectionManager.MoveRelativeAsync(axis, distance);
                _logger.Information("Completed relative move - Axis: {Axis}, Distance: {Distance}", axis, distance);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute relative move - Axis: {Axis}, Distance: {Distance}", axis, distance);
                throw;
            }
        }

        private async Task<bool> IsAxisEnabledAsync(int axis)
        {
            // This would need to be implemented based on how you track axis enable states
            // For now, returning true as a placeholder
            return true;
        }

        public enum Axis
        {
            X = 0,
            Y = 1,
            Z = 2
        }
    }
}