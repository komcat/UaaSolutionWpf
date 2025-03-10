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
        /// Moves to the UV position and prepares for UV curing
        /// </summary>
        private async void MoveToUVButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

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

                // Move to the UV position
                SetStatus("Moving to UV position...");
                bool success = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "UV");

                if (success)
                {
                    SetStatus("Successfully moved to UV position");
                    _logger.Information("Successfully moved to UV position");

                    // Ask if user wants to activate UV
                    var result = MessageBox.Show("Do you want to activate the UV curing process?",
                                               "UV Activation",
                                               MessageBoxButton.YesNo,
                                               MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        //activate the UV head
                        await pneumaticSlideManager.GetSlide("UV_Head").ExtendAsync();

                        // Activate UV
                        SetStatus("Activating UV...");

                        // Activate both UV pins
                        if (deviceManager != null)
                        {
                            //create a rising edge to activate UV
                            deviceManager.ClearOutput("IOBottom", "UV_PLC1");
                            await Task.Delay(200);
                            deviceManager.SetOutput("IOBottom", "UV_PLC1");
                            await Task.Delay(200);
                            deviceManager.ClearOutput("IOBottom", "UV_PLC1");


                            _logger.Information("UV activated successfully");

                            // Wait for the specified duration
                            TimeSpan timeSpan = TimeSpan.FromSeconds(180);
                            await Task.Delay(timeSpan);

                            SetStatus("UV curing completed");
                            _logger.Information("UV curing completed");
                        }
                        else
                        {
                            SetStatus("Device manager not initialized, cannot activate UV");
                            _logger.Warning("Device manager not initialized, cannot activate UV");
                        }
                    }
                    else
                    {
                        SetStatus("UV activation cancelled");
                        _logger.Information("UV activation cancelled by user");
                    }
                }
                else
                {
                    SetStatus("Failed to move to UV position");
                    _logger.Warning("Failed to move to UV position");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveToUVButton_Click");
                SetStatus("Error during UV position movement");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
