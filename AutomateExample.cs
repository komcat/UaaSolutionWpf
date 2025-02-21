using EzIIOLib;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf
{
    public class AutomationExample
    {
        private readonly CommandCoordinator _coordinator;
        private readonly ILogger _logger;

        public AutomationExample(
            MotionGraphManager motionGraphManager,
            HexapodMovementService leftHexapod,
            HexapodMovementService rightHexapod,
            HexapodMovementService bottomHexapod,
            GantryMovementService gantry,
            MultiDeviceManager ioManager,
            ILogger logger)
        {
            _logger = logger.ForContext<AutomationExample>();

            // Initialize the coordinator with all required services
            _coordinator = new CommandCoordinator(
                motionGraphManager: motionGraphManager,
                leftHexapod: leftHexapod,
                rightHexapod: rightHexapod,
                bottomHexapod: bottomHexapod,
                gantry: gantry,
                ioManager: ioManager,
                logger: logger);
        }

        public async Task RunUVOperation()
        {
            try
            {
                _logger.Information("Starting UV operation sequence");

                var sequence = OperationSequences.UVOperation();
                await _coordinator.ExecuteCommandSequence(sequence);

                _logger.Information("UV operation sequence completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing UV operation sequence");
                throw;
            }
        }

        public async Task RunDispenserOperation()
        {
            try
            {
                _logger.Information("Starting dispenser operation sequence");

                //var sequence = OperationSequences.DispenserOperation();
                //await _coordinator.ExecuteCommandSequence(sequence);

                _logger.Information("Dispenser operation sequence completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing dispenser operation sequence");
                throw;
            }
        }

        // Example of creating a custom operation sequence
        public async Task RunCustomOperation()
        {
            try
            {
                _logger.Information("Starting custom operation sequence");

                var sequence = new List<CoordinatedCommand>
                {
                    // Move gantry to position
                    CoordinatedCommand.CreateMotionCommand(
                        deviceId: "gantry-main",
                        targetPosition: "Home",
                        order: 1,
                        waitForComplete: true),

                    // Move left hexapod
                    CoordinatedCommand.CreateMotionCommand(
                        deviceId: "hex-left",
                        targetPosition: "LensGrip",
                        order: 2,
                        waitForComplete: true),

                    // Lower Pick Up Tool
                    CoordinatedCommand.CreateSlideCommand(
                        slideId: "pickup_tool",
                        targetSlidePosition: SlidePosition.Extended,  // Instead of SlideState.Down
                        order: 3),

                    

                    // Wait for grip to establish
                    CoordinatedCommand.CreateTimerCommand(
                        duration: TimeSpan.FromSeconds(0.5),
                        order: 10),

                    // Raise Pick Up Tool with full validation
                    CoordinatedCommand.CreateSlideCommand(
                        slideId: "pickup_tool",
                        targetSlidePosition: SlidePosition.Retracted,  // Instead of SlideState.Up
                        order: 11)
                };

                await _coordinator.ExecuteCommandSequence(sequence);

                _logger.Information("Custom operation sequence completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing custom operation sequence");
                throw;
            }
        }
    }
}