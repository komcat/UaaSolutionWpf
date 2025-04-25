using MotionServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;


namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {
        /// <summary>
        /// Rejects a lens with the left gripper
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RejectLeftButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string gripper = "L_Gripper";
                if (_motionKernel == null || deviceManager == null)
                {
                    MessageBox.Show("Motion or IO system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                SetStatus("Starting left lens reject sequence...");
                // 1. Get device IDs
                // Get the gantry device ID
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    var gantryDevice = _motionKernel.GetDevices()
                        .FirstOrDefault(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id));
                    if (gantryDevice == null)
                    {
                        MessageBox.Show("No connected gantry device found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    gantryId = gantryDevice.Id;
                }
                string hexapodId = null;
                MotionDevice hexapodDevice = null;
                hexapodDevice = GetDeviceByName("hex-left");
                // Get the left hexapod device ID
                if (hexapodDevice != null)
                {
                    hexapodId = hexapodDevice.Id;
                    _logger.Information("Found left hexapod device: {DeviceName} with ID {DeviceId}",
                                        hexapodDevice.Name, hexapodId);
                }
                else
                {
                    _logger.Warning("Left hexapod device (hex-left) not found or not connected, will proceed without left hexapod movements");
                }
                if (hexapodId == null)
                {
                    MessageBox.Show("Left hexapod device not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // 2. Move to the reject position
                SetStatus("Moving to reject position...");
                await _motionKernel.MoveToPositionAsync(hexapodId, "ApproachLensPlace");
                await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "RejectLens");
                // 3. Open the gripper
                SetStatus("Opening gripper...");
                bool gripSuccess = deviceManager.ClearOutput("IOBottom", "L_Gripper");
                await Task.Delay(TimeSpan.FromSeconds(3));
                //await OpenGripper(gripper);
                // 4. Move to the home position
                SetStatus("Moving to home position...");
                await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "Home");
                SetStatus("Left lens reject sequence completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during left lens reject sequence");

            }
        }

        /// <summary>
        /// Rejects a lens with the right gripper
        /// </summary>
        private async void RejectRightButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string gripper = "R_Gripper";
                if (_motionKernel == null || deviceManager == null)
                {
                    MessageBox.Show("Motion or IO system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                SetStatus("Starting right lens reject sequence...");
                // 1. Get device IDs
                // Get the gantry device ID
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    var gantryDevice = _motionKernel.GetDevices()
                        .FirstOrDefault(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id));
                    if (gantryDevice == null)
                    {
                        MessageBox.Show("No connected gantry device found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    gantryId = gantryDevice.Id;
                }
                string hexapodId = null;
                MotionDevice hexapodDevice = null;
                hexapodDevice = GetDeviceByName("hex-right");
                // Get the right hexapod device ID
                if (hexapodDevice != null)
                {
                    hexapodId = hexapodDevice.Id;
                    _logger.Information("Found right hexapod device: {DeviceName} with ID {DeviceId}",
                                        hexapodDevice.Name, hexapodId);
                }
                else
                {
                    _logger.Warning("Right hexapod device (hex-right) not found or not connected, will proceed without right hexapod movements");
                }

                if(hexapodId==null)
                {
                    MessageBox.Show("Right hexapod device not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. Move to the reject position
                SetStatus("Moving to reject position...");
                await _motionKernel.MoveToPositionAsync(hexapodId, "ApproachLensPlace");
                await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "RejectLens");
                // 3. Open the gripper
                SetStatus("Opening gripper...");
                bool gripSuccess = deviceManager.ClearOutput("IOBottom", "L_Gripper");
                await Task.Delay(TimeSpan.FromSeconds(3));
                //await OpenGripper(gripper);
                // 4. Move to the home position
                SetStatus("Moving to home position...");
                await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "Home");
                SetStatus("Right lens reject sequence completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during right lens reject sequence");

            }
        }
    }
}
