using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UaaSolutionWpf.IO;
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
            IOManager ioManager,
            PneumaticSlideService slideService,
            ILogger logger,
            HexapodMovementService leftHexapod=null,
            HexapodMovementService rightHexapod = null,
            HexapodMovementService bottomHexapod = null,
            GantryMovementService gantry = null)
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
                slideService: slideService,
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

                    // Lower Pick Up Tool with full validation
                    CoordinatedCommand.CreateSlideCommand(
                        slideId: "pickup_tool",
                        targetState: SlideState.Down,
                        order: 3),

                    // Grip the lens
                    CoordinatedCommand.CreateOutputCommand(
                        deviceName: "IOBottom",
                        pinName: "L_Gripper",
                        state: true,
                        order: 4,
                        waitForComplete: true),

                    // Wait for grip to establish
                    CoordinatedCommand.CreateTimerCommand(
                        duration: TimeSpan.FromSeconds(0.5),
                        order: 5),

                    // Raise Pick Up Tool with full validation
                    CoordinatedCommand.CreateSlideCommand(
                        slideId: "pickup_tool",
                        targetState: SlideState.Up,
                        order: 6)
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