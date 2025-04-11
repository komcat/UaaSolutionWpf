using MotionServiceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {


        private async void TestLensInspectButtion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the button during execution
                if (sender is System.Windows.Controls.Button button)
                {
                    button.IsEnabled = false;
                    button.Content = "Running...";
                }

                await ExecuteLensInspectionSequenceAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in RunLensInspectionButton_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable the button after execution
                if (sender is System.Windows.Controls.Button button)
                {
                    button.IsEnabled = true;
                    button.Content = "Run Lens Inspection";
                }
            }
        }
        /// <summary>
        /// Executes a lens inspection sequence that:
        /// 1. Moves left hexapod to RejectLens position
        /// 2. Moves right hexapod to RejectLens position
        /// 3. Cycles the gantry between SeeGripCollLens and SeeGripFocusLens positions
        /// 4. Takes photos at each position
        /// 5. Repeats the cycle 5 times
        /// </summary>
        private async Task<bool> ExecuteLensInspectionSequenceAsync()
        {
            try
            {
                if (_motionKernel == null || _cameraManager == null)
                {
                    MessageBox.Show("Motion system or camera not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                SetStatus("Starting lens inspection sequence...");

                // Get device IDs
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    var gantryDevice = _motionKernel.GetDevices()
                        .FirstOrDefault(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id));

                    if (gantryDevice == null)
                    {
                        MessageBox.Show("No connected gantry device found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    gantryId = gantryDevice.Id;
                }

                // Get the left and right hexapod devices
                MotionDevice leftHexapod = GetDeviceByName("hex-left");
                MotionDevice rightHexapod = GetDeviceByName("hex-right");

                if (leftHexapod == null || rightHexapod == null)
                {
                    MessageBox.Show("One or both hexapod devices not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // 1. Move left hexapod to RejectLens position
                SetStatus("Moving left hexapod to RejectLens position...");
                bool leftSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(leftHexapod.Id, "RejectLens");
                if (!leftSuccess)
                {
                    _logger.Warning("Failed to move left hexapod to RejectLens position");
                    SetStatus("Failed to move left hexapod to RejectLens position");
                    return false;
                }
                _logger.Information("Left hexapod moved to RejectLens position");

                // 2. Move right hexapod to RejectLens position
                SetStatus("Moving right hexapod to RejectLens position...");
                bool rightSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(rightHexapod.Id, "RejectLens");
                if (!rightSuccess)
                {
                    _logger.Warning("Failed to move right hexapod to RejectLens position");
                    SetStatus("Failed to move right hexapod to RejectLens position");
                    return false;
                }
                _logger.Information("Right hexapod moved to RejectLens position");

                // Create directory for images if it doesn't exist
                string imageDirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UAAImages", "LensInspection_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(imageDirPath);

                // 3-5. Cycle between SeeGripCollLens and SeeGripFocusLens 5 times
                for (int cycle = 1; cycle <= 5; cycle++)
                {
                    // Move to SeeGripCollLens and take photo
                    SetStatus($"Cycle {cycle}/5: Moving to collimating lens position...");
                    bool collLensSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeGripCollLens");
                    if (!collLensSuccess)
                    {
                        _logger.Warning($"Cycle {cycle}: Failed to move gantry to SeeGripCollLens position");
                        SetStatus($"Cycle {cycle}: Failed to move gantry to SeeGripCollLens position");
                        continue; // Try next cycle
                    }

                    // Wait briefly for stabilization
                    await Task.Delay(500);

                    // Take photo of collimating lens
                    string collImageFileName = $"Cycle{cycle}_CollLens_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string collImagePath = Path.Combine(imageDirPath, collImageFileName);

                    try
                    {
                        _cameraManager.SaveImageToFile(collImagePath);
                        _logger.Information($"Cycle {cycle}: Saved image of collimating lens to {collImagePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Cycle {cycle}: Failed to save image of collimating lens");
                    }

                    // Move to SeeGripFocusLens and take photo
                    SetStatus($"Cycle {cycle}/5: Moving to focusing lens position...");
                    bool focusLensSuccess = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeGripFocusLens");
                    if (!focusLensSuccess)
                    {
                        _logger.Warning($"Cycle {cycle}: Failed to move gantry to SeeGripFocusLens position");
                        SetStatus($"Cycle {cycle}: Failed to move gantry to SeeGripFocusLens position");
                        continue; // Try next cycle
                    }

                    // Wait briefly for stabilization
                    await Task.Delay(500);

                    // Take photo of focusing lens
                    string focusImageFileName = $"Cycle{cycle}_FocusLens_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string focusImagePath = Path.Combine(imageDirPath, focusImageFileName);

                    try
                    {
                        _cameraManager.SaveImageToFile(focusImagePath);
                        _logger.Information($"Cycle {cycle}: Saved image of focusing lens to {focusImagePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Cycle {cycle}: Failed to save image of focusing lens");
                    }
                }

                SetStatus("Lens inspection sequence completed successfully");
                _logger.Information("Lens inspection sequence completed successfully");

                MessageBox.Show($"Lens inspection sequence completed.\nImages saved to: {imageDirPath}",
                    "Sequence Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing lens inspection sequence");
                SetStatus("Error executing lens inspection sequence");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }






    }

}
