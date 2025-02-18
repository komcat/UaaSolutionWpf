using System;
using System.Collections.Generic;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Motion
{
    public static class OperationSequences
    {
        public static List<CoordinatedCommand> UVOperation()
        {
            return new List<CoordinatedCommand>
            {
                // Move gantry to UV position
                CoordinatedCommand.CreateMotionCommand(
                    deviceId: "gantry-main",
                    targetPosition: "UV",
                    order: 1,
                    waitForComplete: true),
            
                // Lower UV head (PneumaticSlideService handles all validation)
                CoordinatedCommand.CreateSlideCommand(
                    slideId: "uv_head",
                    targetState: SlideState.Down,
                    order: 2),
            
                // UV exposure time
                CoordinatedCommand.CreateTimerCommand(
                    duration: TimeSpan.FromSeconds(2),
                    order: 3),
            
                // Raise UV head (PneumaticSlideService handles all validation)
                CoordinatedCommand.CreateSlideCommand(
                    slideId: "uv_head",
                    targetState: SlideState.Up,
                    order: 4)
            };
        }
    }
}