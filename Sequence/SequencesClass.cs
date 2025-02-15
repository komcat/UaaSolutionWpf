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

        public static List<CoordinatedMovement> SeeSLED()
        {
            return new List<CoordinatedMovement>
                {
                    
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "SeeSLED",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                    // Move hexapods to approach positions in parallel
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-right",
                        TargetPosition = "Home",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    }
                };
        }

        public static List<CoordinatedMovement> SeePIC()
        {
            return new List<CoordinatedMovement>
                {
                    // Move gantry to safe position first
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "SeePIC",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                    // Move hexapods to approach positions in parallel
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-right",
                        TargetPosition = "RejectLens",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    }


                };
        }

        public static List<CoordinatedMovement> LeftPlace()
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
                    }


                };
        }

        public static List<CoordinatedMovement> RightPlace()
        {
            return new List<CoordinatedMovement>
                {
                    // Move gantry to safe position first
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "SeeFocusLens",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                    // Move hexapods to approach positions in parallel
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-right",
                        TargetPosition = "LensPlace",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    }


                };
        }

        public static List<CoordinatedMovement> LeftPick()
        {
            return new List<CoordinatedMovement>
                {
                    
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "SeeGripCollLens",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                    
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-left",
                        TargetPosition = "LensGrip",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    }


                };
        }

        public static List<CoordinatedMovement> RightPick()
        {
            return new List<CoordinatedMovement>
                {
                    
                    new CoordinatedMovement
                    {
                        DeviceId = "gantry-main",
                        TargetPosition = "SeeGripFocusLens",
                        ExecutionOrder = 1,
                        WaitForCompletion = true
                    },
                    
                   
                    new CoordinatedMovement
                    {
                        DeviceId = "hex-right",
                        TargetPosition = "LensGrip",
                        ExecutionOrder = 2,
                        WaitForCompletion = false
                    }


                };
        }

    }
}
