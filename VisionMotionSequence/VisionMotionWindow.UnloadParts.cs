using MotionServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {
        /// <summary>
        /// Gets a motion device by its name
        /// </summary>
        /// <param name="deviceName">The name of the device to find (e.g., "hex-left", "hex-right", "gantry-main")</param>
        /// <returns>The device if found and connected, null otherwise</returns>
        private MotionDevice GetDeviceByName(string deviceName)
        {
            if (_motionKernel == null)
                return null;

            var device = _motionKernel.GetDevices()
                .FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase) &&
                                   _motionKernel.IsDeviceConnected(d.Id));

            if (device != null)
            {
                _logger.Information("Found device: {DeviceName} with ID {DeviceId}", device.Name, device.Id);
                return device;
            }
            else
            {
                _logger.Warning("Device {DeviceName} not found or not connected", deviceName);
                return null;
            }
        }

        /// <summary>
        /// Unloads all parts and returns to home position
        /// </summary>
        private async void UnloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_motionKernel == null || deviceManager == null)
                {
                    MessageBox.Show("Motion or IO system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Confirmation dialog
                var result = MessageBox.Show("This will release all grippers and return all devices to home position. Continue?",
                                           "Confirm Unload",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                SetStatus("Unloading parts...");

                // Release all grippers
                bool leftGripperReleased = deviceManager.ClearOutput("IOBottom", "L_Gripper");
                bool rightGripperReleased = deviceManager.ClearOutput("IOBottom", "R_Gripper");

                if (leftGripperReleased)
                {
                    LeftGripperStatusText.Text = "Not gripping";
                    _logger.Information("Left gripper released");
                }
                else
                {
                    _logger.Warning("Failed to release left gripper");
                }

                if (rightGripperReleased)
                {
                    RightGripperStatusText.Text = "Not gripping";
                    _logger.Information("Right gripper released");
                }
                else
                {
                    _logger.Warning("Failed to release right gripper");
                }

                //deactivate the UV head
                await pneumaticSlideManager.GetSlide("UV_Head").RetractAsync();



                MotionDevice leftHexDevice = GetDeviceByName("hex-left");
                MotionDevice rightHexDevice = GetDeviceByName("hex-right");

                //immediate move direct to approach lens place
                await _motionKernel.MoveToPositionAsync(leftHexDevice.Id, "ApproachLensPlace");
                await _motionKernel.MoveToPositionAsync(rightHexDevice.Id, "ApproachLensPlace");
                await _motionKernel.MoveToDestinationShortestPathAsync(leftHexDevice.Id, "Home");
                await _motionKernel.MoveToDestinationShortestPathAsync(rightHexDevice.Id, "Home");




                // Turn off vacuum and UV
                deviceManager.ClearOutput("IOBottom", "Vacuum_Base");
                deviceManager.ClearOutput("IOBottom", "UV_PLC1");
                deviceManager.ClearOutput("IOBottom", "UV_PLC2");


                // Return all devices to home
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    var gantryDevice = _motionKernel.GetDevices()
                        .FirstOrDefault(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id));

                    if (gantryDevice != null)
                    {
                        gantryId = gantryDevice.Id;
                    }
                }

                if (!string.IsNullOrEmpty(gantryId))
                {
                    bool homeSuccess = await _motionKernel.HomeDeviceAsync(gantryId);

                    if (homeSuccess)
                    {
                        _logger.Information("Gantry successfully homed");
                    }
                    else
                    {
                        _logger.Warning("Failed to home gantry");
                    }
                }

				//show final value after dry peak
				if (ChannelSelectionComboBox.SelectedItem is RealTimeDataChannel selectedChannel)
				{
					MeasurementValue readVal;
					if (realTimeDataManager.TryGetChannelValue(selectedChannel.ChannelName, out readVal))
					{
						UvValueText2.Text = MeasurementValueFormatter.FormatValue(readVal);
					}
					else
					{
						UvValueText2.Text = "No value";
					}

				}


				SetStatus("Parts unloaded and system reset");
                _logger.Information("Parts unloaded and system reset");

                MessageBox.Show("Unload complete. All parts released and devices returned to home position.",
                              "Unload Complete",
                              MessageBoxButton.OK,
                              MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in UnloadButton_Click");
                SetStatus("Error during unload operation");
                MessageBox.Show($"Error during unload: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
