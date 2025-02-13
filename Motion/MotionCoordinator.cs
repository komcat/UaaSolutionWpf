using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Motion
{
    public class CoordinatedMovement
    {
        public string DeviceId { get; set; }
        public string TargetPosition { get; set; }
        public int ExecutionOrder { get; set; } // Lower numbers execute first
        public bool WaitForCompletion { get; set; } // Whether to wait for this move to complete before starting next
    }

    public class MotionCoordinator
    {

        private readonly MotionGraphManager _motionGraphManager;
        private readonly Dictionary<string, Func<string, Task>> _moveExecutors;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _hexapodSemaphore = new SemaphoreSlim(1, 1);

        public MotionCoordinator(
            MotionGraphManager motionGraphManager,
            HexapodMovementService leftHexapod,
            HexapodMovementService rightHexapod,
            HexapodMovementService bottomHexapod,
            GantryMovementService gantry,
            ILogger logger)
        {
            _motionGraphManager = motionGraphManager;
            _logger = logger.ForContext<MotionCoordinator>();

            _moveExecutors = new Dictionary<string, Func<string, Task>>
            {
                { "hex-left", async (position) => await ExecuteHexapodMove(leftHexapod, 0, position) },
                { "hex-right", async (position) => await ExecuteHexapodMove(rightHexapod, 2, position) },
                { "hex-bottom", async (position) => await ExecuteHexapodMove(bottomHexapod, 1, position) },
                { "gantry-main", async (position) => await ExecuteGantryMove(gantry, position) }
            };
        }

        public async Task ExecuteCoordinatedMove(List<CoordinatedMovement> movements)
        {
            try
            {
                // Validate all movements first
                var analysisResults = new Dictionary<string, PathAnalysis>();
                foreach (var movement in movements)
                {
                    var analysis = await _motionGraphManager.AnalyzeMovementPath(
                        movement.DeviceId,
                        movement.TargetPosition);

                    if (!analysis.IsValid)
                    {
                        throw new InvalidOperationException(
                            $"Invalid movement path for {movement.DeviceId} to {movement.TargetPosition}: {analysis.Error}");
                    }

                    analysisResults[movement.DeviceId] = analysis;
                }

                // Group movements by execution order
                var orderedMovements = movements
                    .GroupBy(m => m.ExecutionOrder)
                    .OrderBy(g => g.Key);

                foreach (var group in orderedMovements)
                {
                    // Separate hexapod and gantry movements
                    var hexapodMoves = group.Where(m => m.DeviceId.StartsWith("hex-")).ToList();
                    var gantryMoves = group.Where(m => m.DeviceId.StartsWith("gantry-")).ToList();

                    // Execute hexapod movements sequentially
                    foreach (var hexMove in hexapodMoves)
                    {
                        await _hexapodSemaphore.WaitAsync();
                        try
                        {
                            if (_moveExecutors.TryGetValue(hexMove.DeviceId, out var executor))
                            {
                                await executor(hexMove.TargetPosition);
                            }
                        }
                        finally
                        {
                            _hexapodSemaphore.Release();
                        }
                    }

                    // Execute gantry movements in parallel with hexapods
                    var gantryTasks = gantryMoves
                        .Where(m => _moveExecutors.ContainsKey(m.DeviceId))
                        .Select(m => _moveExecutors[m.DeviceId](m.TargetPosition))
                        .ToList();

                    // Wait for all gantry movements to complete
                    if (gantryTasks.Any())
                    {
                        await Task.WhenAll(gantryTasks);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during coordinated movement execution");
                throw;
            }
        }

        private async Task ExecuteHexapodMove(HexapodMovementService service, int hexapodId, string targetPosition)
        {
            try
            {
                _logger.Information("Moving hexapod {HexapodId} to position {Position}", hexapodId, targetPosition);
                await service.MoveToPositionAsync(targetPosition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving hexapod {HexapodId} to {Position}", hexapodId, targetPosition);
                throw;
            }
        }

        private async Task ExecuteGantryMove(GantryMovementService service, string targetPosition)
        {
            try
            {
                _logger.Information("Moving gantry to position {Position}", targetPosition);
                await service.MoveToPositionAsync(targetPosition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving gantry to {Position}", targetPosition);
                throw;
            }
        }

        // Predefined movement sequences
       
    }
}