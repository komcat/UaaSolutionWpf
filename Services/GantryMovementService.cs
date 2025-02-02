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

        public GantryMovementService(AcsGantryConnectionManager connectionManager, ILogger logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger.ForContext<GantryMovementService>();
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