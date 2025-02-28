using EzIIOLib;
using System;
using System.Collections.Generic;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Motion
{
    public static class OperationSequences
    {

        public static List<CoordinatedCommand> SeeSLED()
        {
            return new List<CoordinatedCommand>
            {
                // Move gantry to UV position
                CoordinatedCommand.CreateMotionCommand(
                    deviceId: "gantry-main",
                    targetPosition: "Fiducial3",
                    order: 1,
                    waitForComplete: true),
               
                CoordinatedCommand.CreateImageCaptureCommand(
                    prefix: "Fid3-1",
                    order: 2),
                // settle image capture
                CoordinatedCommand.CreateTimerCommand(
                    duration: TimeSpan.FromSeconds(3),
                    order: 3),
                CoordinatedCommand.CreateImageCaptureCommand(
                    prefix: "Fid3-2",
                    order: 4),
            };
        }

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
                    slideId: "UV_Head",
                    targetSlidePosition: SlidePosition.Extended,
                    order: 2),
            
                // UV exposure time
                CoordinatedCommand.CreateTimerCommand(
                    duration: TimeSpan.FromSeconds(2),
                    order: 3),

                //set uv off
                CoordinatedCommand.CreateOutputCommand(
                    deviceName: "IOBottom",
                    pinName: "UV_PLC1",
                    state: false,
                    order: 4,
                    waitForComplete: true),

                //wait 0.1sec
                CoordinatedCommand.CreateTimerCommand(
                    duration: TimeSpan.FromSeconds(0.1),
                    order: 5),
                //set trigger on
                CoordinatedCommand.CreateOutputCommand(
                    deviceName: "IOBottom",
                    pinName: "UV_PLC1",
                    state: true,
                    order: 6,
                    waitForComplete: true),

                    //wait 0.1sec
                CoordinatedCommand.CreateTimerCommand(
                    duration: TimeSpan.FromSeconds(0.1),
                    order: 7),

                //alway set uv_plc1 to OFF
                CoordinatedCommand.CreateOutputCommand(
                    deviceName: "IOBottom",
                    pinName: "UV_PLC1",
                    state: false,
                    order: 8,
                    waitForComplete: true),
                //wait 120sec
                CoordinatedCommand.CreateTimerCommand(
                    duration: TimeSpan.FromSeconds(10),
                    order: 7),
                // Raise UV head (PneumaticSlideService handles all validation)
                CoordinatedCommand.CreateSlideCommand(
                    slideId: "UV_Head",
                    targetSlidePosition: SlidePosition.Retracted,
                    order: 9)
            };
        }
    }
}