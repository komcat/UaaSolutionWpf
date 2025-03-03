using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using UaaSolutionWpf.Commands;

namespace UaaSolutionWpf
{
    // Example methods to add to your VisionMotionWindow class
    public partial class VisionMotionWindow
    {
        private async void CaptureImageForRecording_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager == null || !_isCameraConnected)
                {
                    MessageBox.Show("Camera is not connected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a command to capture an image for recording
                var command = new CameraImageCaptureCommand(
                    _cameraManager,
                    "Test",        // Prefix
                    true,          // isForRecording = true for rolling filenames
                    null,          // No specific filename (auto-generate)
                    _logger
                );

                // Execute the command
                StatusBarTextBlock.Text = "Capturing image for recording...";
                var result = await command.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "Image captured for recording";
                    _logger.Information("Image capture result: {Result}", result.Message);

                    // Optionally show the image path to the user
                    MessageBox.Show($"Image captured: {result.Message}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to capture image: {result.Message}";
                    _logger.Warning("Image capture failed: {Message}", result.Message);
                    MessageBox.Show($"Failed to capture image: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error capturing image";
                _logger.Error(ex, "Error in CaptureImageForRecording_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CaptureImageForReference_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager == null || !_isCameraConnected)
                {
                    MessageBox.Show("Camera is not connected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a command to capture an image for reference
                var command = new CameraImageCaptureCommand(
                    _cameraManager,
                    "Alignment",  // Prefix
                    false,        // isForRecording = false for datestamp filenames
                    null,         // No specific filename (auto-generate)
                    _logger
                );

                // Execute the command
                StatusBarTextBlock.Text = "Capturing image for reference...";
                var result = await command.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "Image captured for reference";
                    _logger.Information("Image capture result: {Result}", result.Message);

                    // Optionally show the image path to the user
                    MessageBox.Show($"Reference image captured: {result.Message}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to capture reference image: {result.Message}";
                    _logger.Warning("Reference image capture failed: {Message}", result.Message);
                    MessageBox.Show($"Failed to capture reference image: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error capturing reference image";
                _logger.Error(ex, "Error in CaptureImageForReference_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CaptureBurstOfImages_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager == null || !_isCameraConnected)
                {
                    MessageBox.Show("Camera is not connected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a command to capture a burst of images
                var command = new CameraImageBurstCaptureCommand(
                    _cameraManager,
                    "Scan",                     // Prefix
                    10,                         // Capture 10 images
                    TimeSpan.FromMilliseconds(200),  // 200ms between captures
                    _logger
                );

                // Execute the command
                StatusBarTextBlock.Text = "Capturing burst of images...";
                var result = await command.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "Burst capture completed";
                    _logger.Information("Burst capture result: {Result}", result.Message);
                    MessageBox.Show($"Burst capture completed: {result.Message}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to complete burst capture: {result.Message}";
                    _logger.Warning("Burst capture failed: {Message}", result.Message);
                    MessageBox.Show($"Failed to complete burst capture: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error during burst capture";
                _logger.Error(ex, "Error in CaptureBurstOfImages_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TestCameraCaptureCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraManager == null || !_isCameraConnected)
                {
                    MessageBox.Show("Camera is not connected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a command sequence that includes camera capture
                var sequence = new CommandSequence(
                    "Camera Capture Sequence",
                    "Demonstrates camera capture with motion"
                );

                // Move to a position first
                sequence.AddCommand(new MoveToNamedPositionCommand(
                    _motionKernel,
                    "3",       // Device ID
                    "SeePIC"   // Position name
                ));

                // Wait for motion to settle
                sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(3000)));

                // Capture an image
                sequence.AddCommand(new CameraImageCaptureCommand(
                    _cameraManager,
                    "PIC",     // Prefix
                    true       // isForRecording
                ));

                // Move to another position
                sequence.AddCommand(new MoveToNamedPositionCommand(
                    _motionKernel,
                    "3",        // Device ID
                    "SeeSLED"   // Position name
                ));

                // Wait for motion to settle
                sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(3000)));

                // Capture another image
                sequence.AddCommand(new CameraImageCaptureCommand(
                    _cameraManager,
                    "SLED",    // Prefix
                    true       // isForRecording
                ));

                // Execute the sequence
                StatusBarTextBlock.Text = "Running camera capture sequence...";
                var result = await sequence.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "Camera capture sequence completed";
                    _logger.Information("Camera capture sequence result: {Result}", result.Message);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Camera capture sequence failed: {result.Message}";
                    _logger.Warning("Camera capture sequence failed: {Message}", result.Message);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error in camera capture sequence";
                _logger.Error(ex, "Error in TestCameraCaptureCommand_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}