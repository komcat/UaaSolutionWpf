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

            // Initialize movement executors for each device
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
                // Validate and analyze all movements first
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

                // Execute movements in order
                foreach (var group in orderedMovements)
                {
                    var tasks = new List<Task>();

                    foreach (var movement in group)
                    {
                        if (_moveExecutors.TryGetValue(movement.DeviceId, out var executor))
                        {
                            var task = executor(movement.TargetPosition);

                            if (movement.WaitForCompletion)
                            {
                                // Wait for this movement to complete before continuing
                                await task;
                            }
                            else
                            {
                                tasks.Add(task);
                            }
                        }
                        else
                        {
                            _logger.Error("No executor found for device {DeviceId}", movement.DeviceId);
                        }
                    }

                    // Wait for all parallel movements in this group to complete
                    if (tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks);
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
                // Implementation for hexapod movement
                // You'll need to implement this based on your existing hexapod movement code
                _logger.Information("Moving hexapod {HexapodId} to position {Position}", hexapodId, targetPosition);

                // Example implementation:
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
                // Implementation for gantry movement
                // You'll need to implement this based on your existing gantry movement code
                _logger.Information("Moving gantry to position {Position}", targetPosition);

                // Example implementation:
                await service.MoveToPositionAsync(targetPosition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving gantry to {Position}", targetPosition);
                throw;
            }
        }

        // Predefined movement sequences
        public static class Sequences
        {
            public static List<CoordinatedMovement> HomeSequence()
            {
                return new List<CoordinatedMovement>
                {
                    // Move gantry to safe position first
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "Home",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                    // Move hexapods to approach positions in parallel
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-left",
                        TargetPosition = "Home",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    },
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-right",
                        TargetPosition = "Home",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    },                   
                   
                };
            }

            // Add more predefined sequences as needed
        }
    }
}