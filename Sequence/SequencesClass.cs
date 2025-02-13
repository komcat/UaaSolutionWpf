using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UaaSolutionWpf.Motion;

namespace UaaSolutionWpf.Sequence
{
    public static class MotionSequences
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

        public static List<CoordinatedMovement> LeftLensPlace()
        {
            return new List<CoordinatedMovement>
                {
                    // Move gantry to safe position first
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "SeeCollimateLens",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                    // Move hexapods to approach positions in parallel
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-left",
                        TargetPosition = "LensPlace",
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
