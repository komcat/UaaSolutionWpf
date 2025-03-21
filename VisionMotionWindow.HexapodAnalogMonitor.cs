using MotionServiceLib.Controls;
using MotionServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {

        // Helper method to get a hexapod controller
        private HexapodController GetHexapodController(string deviceId)
        {
            try
            {
                // Try to use an existing method first
                var method = typeof(MotionKernel).GetMethod("GetHexapodController",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (method != null)
                {
                    return method.Invoke(_motionKernel, new object[] { deviceId }) as HexapodController;
                }

                // If no direct method exists, try to access the controllers directly
                var field = typeof(MotionKernel).GetField("_controllers",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field != null)
                {
                    var controllers = field.GetValue(_motionKernel) as Dictionary<string, IMotionController>;
                    if (controllers != null && controllers.TryGetValue(deviceId, out var controller))
                    {
                        return controller as HexapodController;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // Make sure to stop monitoring when the window is closing
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Stop monitoring
            if (LeftHexapodMonitor.Controller != null)
                LeftHexapodMonitor.Controller.StopAnalogMonitoring();

            if (RightHexapodMonitor.Controller != null)
                RightHexapodMonitor.Controller.StopAnalogMonitoring();
        }
    }
}
