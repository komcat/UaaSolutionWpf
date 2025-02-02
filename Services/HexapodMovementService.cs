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

        public HexapodMovementService(
            HexapodConnectionManager connectionManager,
            HexapodConnectionManager.HexapodType hexapodType,
            ILogger logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _hexapodType = hexapodType;
            _logger = logger.ForContext<HexapodMovementService>();
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