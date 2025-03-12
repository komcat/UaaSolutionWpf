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
        /// Picks and places a lens with the right gripper in a simple sequence
        /// </summary>
        private async void PickRightLensButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string gripper = "R_Gripper";

                if (_motionKernel == null || deviceManager == null)
                {
                    MessageBox.Show("Motion or IO system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SetStatus("Starting right lens pick and place sequence...");

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
                hexapodDevice=GetDeviceByName("hex-right");

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

                if (hexapodDevice != null)
                {
                    hexapodId = hexapodDevice.Id;
                }
                else
                {
                    _logger.Warning("No connected hexapod device found, will proceed without hexapod movements");
                }

                // 2. Clear right gripper output
                bool clearSuccess = deviceManager.ClearOutput("IOBottom", gripper);
                if (clearSuccess)
                {
                    _logger.Information($"{gripper} gripper cleared");
                    RightGripperStatusText.Text = "Not gripping";
                }
                else
                {
                    _logger.Warning($"Failed to clear {gripper}");
                }

                // 3. Move gantry to right grip lens position
                SetStatus("Moving gantry to right lens pickup position...");
                bool gantrySuccess = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeGripFocusLens");

                if (!gantrySuccess)
                {
                    SetStatus("Failed to move gantry to right lens pickup position");
                    _logger.Warning("Failed to move gantry to right lens pickup position");
                    return;
                }

                _logger.Information("Successfully moved gantry to right lens pickup position");

                // 4. Move hexapod to grip location (if available)
                if (!string.IsNullOrEmpty(hexapodId))
                {
                    SetStatus("Moving hexapod to right grip location...");
                    bool hexapodSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "LensGrip");

                    if (!hexapodSuccess)
                    {
                        _logger.Warning("Failed to move hexapod to right grip location");
                    }
                    else
                    {
                        _logger.Information("Successfully moved hexapod to right grip location");
                    }
                }

                // 5. Perform grip
                // Small delay to ensure grip is secure
                await Task.Delay(500);
                SetStatus("Activating right gripper...");
                bool gripSuccess = deviceManager.SetOutput("IOBottom", gripper);

                if (gripSuccess)
                {
                    SetStatus("Right lens gripped successfully");
                    RightGripperStatusText.Text = "Gripping";
                    _logger.Information("Right lens gripped successfully");

                    var gripConfirm = MessageBox.Show("Confirm to grip", "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Information);

                    if (gripConfirm == MessageBoxResult.OK)
                    {

                        deviceManager.ClearOutput("IOBottom", gripper);
                        await Task.Delay(1000);
                        deviceManager.SetOutput("IOBottom", gripper);
                        await Task.Delay(500);
                    }
                }
                else
                {
                    SetStatus($"Failed to activate right {gripper}");
                    _logger.Warning($"Failed to activate right {gripper}");
                    return;
                }

                // 6. Move hexapod to place location (if available)
                if (!string.IsNullOrEmpty(hexapodId))
                {
                    SetStatus("Moving hexapod to right place location...");
                    bool hexapodSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "LensPlace");

                    if (!hexapodSuccess)
                    {
                        _logger.Warning("Failed to move hexapod to right place location");
                    }
                    else
                    {
                        _logger.Information("Successfully moved hexapod to right place location");
                    }
                }

                // 7. Move gantry to place location
                SetStatus("Moving gantry to right lens placement position...");
                bool placeSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeFocusLens");

                if (!placeSuccess)
                {
                    SetStatus("Failed to move gantry to right lens placement position");
                    _logger.Warning("Failed to move gantry to right lens placement position");
                    return;
                }

                _logger.Information("Successfully moved gantry to right lens placement position");

                
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in PickRightLensButton_Click");
                SetStatus("Error during right lens pick/place operation");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
