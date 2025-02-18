using Serilog;
using System;
using System.Threading.Tasks;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Motion;

namespace UaaSolutionWpf.Services
{
    public class GantryMovementService
    {
        private readonly AcsGantryConnectionManager _connectionManager;
        private readonly ILogger _logger;
        private readonly PositionRegistry _positionRegistry;
        private readonly MotionGraphManager _motionGraphManager;
        private const string DEVICE_ID = "gantry-main";

        public GantryMovementService(
            AcsGantryConnectionManager connectionManager,
            PositionRegistry positionRegistry,
            ILogger logger,
            MotionGraphManager motionGraphManager = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _logger = logger.ForContext<GantryMovementService>();
            _motionGraphManager = motionGraphManager;
        }
        // Similarly for GantryMovementService
        public async Task MoveToPositionAsync(string positionName)
        {
            try
            {
                _logger.Information("Moving gantry to position {Position}", positionName);

                if (!_connectionManager.IsConnected)
                {
                    throw new InvalidOperationException("Gantry is not connected");
                }

                // If we have a motion graph manager, use path analysis
                if (_motionGraphManager != null)
                {
                    var pathAnalysis = await _motionGraphManager.AnalyzeMovementPath(DEVICE_ID, positionName);
                    if (!pathAnalysis.IsValid)
                    {
                        throw new InvalidOperationException($"Invalid movement path: {pathAnalysis.Error}");
                    }

                    await ExecuteMovementSequence(pathAnalysis);
                }
                else
                {
                    // Fallback to direct movement if no motion graph manager
                    if (!_positionRegistry.TryGetGantryPosition(4, positionName, out var targetPos))
                    {
                        throw new InvalidOperationException($"Position {positionName} not found for gantry");
                    }

                    await MoveToAbsolutePosition(targetPos);
                }

                _logger.Information("Completed move of gantry to {Position}", positionName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving gantry to {Position}", positionName);
                throw;
            }
        }
        private async Task ExecuteMovementSequence(PathAnalysis pathAnalysis)
        {
            try
            {
                // Handle initial move if needed
                if (pathAnalysis.RequiresInitialMove)
                {
                    _logger.Information(
                        "Executing initial move to {Position}, distance: {Distance:F3}mm",
                        pathAnalysis.CurrentPosition,
                        pathAnalysis.InitialMoveDistance);

                    if (!_positionRegistry.TryGetGantryPosition(4, pathAnalysis.CurrentPosition, out var initialPosition))
                    {
                        throw new InvalidOperationException($"Could not find coordinates for position {pathAnalysis.CurrentPosition}");
                    }

                    await MoveToAbsolutePosition(initialPosition);
                    await Task.Delay(100); // Stability delay
                }

                // Execute path movements
                for (int i = 0; i < pathAnalysis.Path.Count - 1; i++)
                {
                    string currentNode = pathAnalysis.Path[i];
                    string nextNode = pathAnalysis.Path[i + 1];

                    _logger.Information("Moving from {From} to {To}", currentNode, nextNode);

                    if (!_positionRegistry.TryGetGantryPosition(4, nextNode, out var targetPosition))
                    {
                        throw new InvalidOperationException($"Could not find coordinates for position {nextNode}");
                    }

                    await MoveToAbsolutePosition(targetPosition);

                    if (i < pathAnalysis.Path.Count - 2)
                    {
                        await Task.Delay(100); // Inter-move delay
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing movement sequence");
                throw;
            }
        }

        private async Task MoveToAbsolutePosition(Position position)
        {
            try
            {
                // Start all axis movements simultaneously
                var moveOperations = new List<Task>
                {
                    _connectionManager.MoveToAbsolutePositionAsync(0, position.X),
                    _connectionManager.MoveToAbsolutePositionAsync(1, position.Y),
                    _connectionManager.MoveToAbsolutePositionAsync(2, position.Z)
                };

                // Wait for all move commands to be initiated
                await Task.WhenAll(moveOperations);

                // Wait for all axes to complete their motion
                await _connectionManager.WaitForAllAxesIdleAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during multi-axis move to position X:{X}, Y:{Y}, Z:{Z}",
                    position.X, position.Y, position.Z);
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

                await _connectionManager.MoveRelativeAsync(axis, distance);
                await _connectionManager.WaitForAllAxesIdleAsync();

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