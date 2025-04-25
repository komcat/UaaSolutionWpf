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
		/// Picks and places a lens with the left gripper in a simple sequence
		/// </summary>
		private async void PickLeftLensButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
                // Deselect all devices
                _globalJogControl.DeselectAllDevices();

                // Select a device with ID "hex-left"
                _globalJogControl.SelectDevice("hex-left");

                if (_motionKernel == null || deviceManager == null)
				{
					MessageBox.Show("Motion or IO system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				SetStatus("Starting left lens pick and place sequence...");

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

				// Get the right hexapod device ID
				string hexapodId = null;
				var hexapodDevice = _motionKernel.GetDevices()
					.FirstOrDefault(d => d.Type == MotionDeviceType.Hexapod &&
										 d.Name.Contains("hex-left") &&
										 _motionKernel.IsDeviceConnected(d.Id));

				if (hexapodDevice != null)
				{
					hexapodId = hexapodDevice.Id;
					_logger.Information("Found Left hexapod device: {DeviceName} with ID {DeviceId}",
										hexapodDevice.Name, hexapodId);
				}
				else
				{
					_logger.Warning("Right hexapod device (hex-left) not found or not connected, will proceed without right hexapod movements");
				}

				// 2. Clear left gripper output
				bool clearSuccess = deviceManager.ClearOutput("IOBottom", "L_Gripper");
				if (clearSuccess)
				{
					_logger.Information("Left gripper cleared");
					LeftGripperStatusText.Text = "Not gripping";
				}
				else
				{
					_logger.Warning("Failed to clear left gripper");
				}

				// 3. Move gantry to left grip lens position
				SetStatus("Moving gantry to left lens pickup position...");
				bool gantrySuccess = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeGripCollLens");

				if (!gantrySuccess)
				{
					SetStatus("Failed to move gantry to left lens pickup position");
					_logger.Warning("Failed to move gantry to left lens pickup position");
					return;
				}

				_logger.Information("Successfully moved gantry to left lens pickup position");

				// 4. Move hexapod to grip location (if available)
				if (!string.IsNullOrEmpty(hexapodId))
				{
					SetStatus("Moving hexapod to left grip location...");
					bool hexapodSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "LensGrip");

					if (!hexapodSuccess)
					{
						_logger.Warning("Failed to move hexapod to left grip location");
					}
					else
					{
						_logger.Information("Successfully moved hexapod to left grip location");
					}
				}


				SetStatus("Confirm to grip");
				var gripConfirm = MessageBox.Show("Confirm to grip", "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Information);

				if (gripConfirm == MessageBoxResult.OK)
				{
					// 5. Perform grip
					SetStatus("Activating left gripper...");
					// Small delay to ensure grip is secure
					await Task.Delay(500);
					bool gripSuccess = deviceManager.SetOutput("IOBottom", "L_Gripper");

					if (gripSuccess)
					{
						SetStatus("Left lens gripped successfully");
						LeftGripperStatusText.Text = "Gripping";
						_logger.Information("Left lens gripped successfully");

						// Small delay to ensure grip is secure
						await Task.Delay(500);
						deviceManager.ClearOutput("IOBottom", "L_Gripper");
						await Task.Delay(500);
						deviceManager.SetOutput("IOBottom", "L_Gripper");
						await Task.Delay(500);


						// 6. Move hexapod to place location (if available)
						if (!string.IsNullOrEmpty(hexapodId))
						{
							SetStatus("Moving hexapod to left place location...");
							bool hexapodSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(hexapodId, "LensPlace");

							if (!hexapodSuccess)
							{
								_logger.Warning("Failed to move hexapod to left place location");
							}
							else
							{
								_logger.Information("Successfully moved hexapod to left place location");
							}
						}

						// 7. Move gantry to place location
						SetStatus("Moving gantry to left lens placement position...");
						bool placeSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeCollimateLens");

						if (!placeSuccess)
						{
							SetStatus("Failed to move gantry to left lens placement position");
							_logger.Warning("Failed to move gantry to left lens placement position");
							return;
						}

						_logger.Information("Successfully moved gantry to left lens placement position");


					}
					else
					{
						SetStatus("Failed to activate left gripper");
						_logger.Warning("Failed to activate left gripper");
						return;
					}
				}
				else
				{
					SetStatus("Left lens pick/place sequence cancelled");
				}




			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Error in PickLeftLensButton_Click");
				SetStatus("Error during left lens pick/place operation");
				MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}



		private void SetJogToDefaultLeft()
		{

		}

	}
}
