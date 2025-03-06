using MotionServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UaaSolutionWpf.Commands;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {

        private async void TestUVAction()
        {
            // Create a new command sequence
            var sequence = new CommandSequence(
                "UV Sequence",
                "Demonstrates motion and IO"
            );

            // Move to UV position
            sequence.AddCommand(new MoveToNamedPositionCommand(
                _motionKernel,
                "3",
                "UV"
            ));

            // Extend UV head down (assuming you have a slide for this)
            // This command will already wait until the slide is fully extended
            sequence.AddCommand(new PneumaticSlideCommand(
                pneumaticSlideManager,
                "UV_Head",  // Replace with your actual slide name
                true       // Extend
            ));

            // Make sure UV_PLC1 is off before triggering
            sequence.AddCommand(new SetOutputPinCommand(
                deviceManager,
                "IOBottom",
                "UV_PLC1",
                false
            ));

            // Short delay to ensure the above command completes
            sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(100)));

            // Trigger UV_PLC1 on for 0.5 seconds
            sequence.AddCommand(new SetOutputPinCommand(
                deviceManager,
                "IOBottom",
                "UV_PLC1",
                true
            ));

            // Wait for 0.5 seconds
            sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(500)));

            // Turn off UV_PLC1
            sequence.AddCommand(new SetOutputPinCommand(
                deviceManager,
                "IOBottom",
                "UV_PLC1",
                false
            ));

            // Delay for 60 seconds
            sequence.AddCommand(new DelayCommand(TimeSpan.FromSeconds(60)));

            // Retract UV head up
            // This command will already wait until the slide is fully retracted
            sequence.AddCommand(new PneumaticSlideCommand(
                pneumaticSlideManager,
                "UV_Head",  // Replace with your actual slide name
                false      // Retract
            ));

            // Execute the sequence
            StatusBarTextBlock.Text = "Running UV sequence...";
            try
            {
                var result = await sequence.ExecuteAsync(CancellationToken.None);

                if (result.Success)
                {
                    StatusBarTextBlock.Text = "UV sequence completed successfully";
                    _logger.Information("UV sequence completed: {ExecutionTime}ms", result.ExecutionTime.TotalMilliseconds);
                }
                else
                {
                    StatusBarTextBlock.Text = $"UV sequence failed: {result.Message}";
                    _logger.Warning("UV sequence failed: {Message}", result.Message);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error in UV sequence";
                _logger.Error(ex, "Error executing UV sequence");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void TestLoopSledAndPic()
        {
            // Create a new command sequence
            var sequence = new CommandSequence(
                "Demo Sequence",
                "Demonstrates a sequence of motion and delay commands"
            );

            // Add commands to the sequence
            sequence.AddCommand(new MoveToNamedPositionCommand(
                _motionKernel,
                "3",
                "SeePIC"
            ));

            sequence.AddCommand(new DelayCommand(TimeSpan.FromSeconds(2)));

            sequence.AddCommand(new MoveToNamedPositionCommand(
                _motionKernel,
                "3",
                "SeeSLED"
            ));

            sequence.AddCommand(new DelayCommand(TimeSpan.FromSeconds(1)));

            sequence.AddCommand(new MoveToNamedPositionCommand(
                _motionKernel,
                "3",
                "SeePIC"
            ));

            // Execute the sequence
            var result = await sequence.ExecuteAsync(CancellationToken.None);

            // Log the result
            _logger.Information("Sequence result: {Result}", result);
        }


        /// <summary>
        /// Moves the gantry along a predefined sequence of positions
        /// </summary>
        /// <param name="sequence">The name of the predefined sequence (e.g., "HomeToInspect", "InspectToPick")</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task MoveGantryAlongPredefinedPath(string sequence)
        {
            try
            {
                // Define common sequences of positions
                Dictionary<string, List<string>> predefinedSequences = new Dictionary<string, List<string>>
        {
            { "HomeToSLED", new List<string> { "Home", "SeeSLED" } },
            { "HomeToDispense", new List<string> { "Home", "Dispense1" } },
            { "InspectionSequence", new List<string> { "Home", "SeeSLED", "SeePIC", "SeeSN" } },
            { "DispensingSequence", new List<string> { "Home", "Dispense1", "UV", "Dispense2" } },
            // Add more predefined sequences as needed
        };

                if (!predefinedSequences.TryGetValue(sequence, out var path))
                {
                    MessageBox.Show($"Predefined sequence '{sequence}' not found.",
                        "Sequence Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Use the main method with the predefined path
                await MoveGantryAlongPath(null, path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveGantryAlongPredefinedPath for sequence {Sequence}", sequence);
                StatusBarTextBlock.Text = $"Error executing predefined path '{sequence}'.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Moves a gantry device along a planned path of multiple positions
        /// </summary>
        /// <param name="deviceId">The ID of the gantry device (optional if there's only one gantry)</param>
        /// <param name="positionNames">The sequence of position names to visit</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task MoveGantryAlongPath(string deviceId = null, IEnumerable<string> positionNames = null)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // If no device ID provided, try to use the active gantry or find one
                if (string.IsNullOrEmpty(deviceId))
                {
                    deviceId = _activeGantryDeviceId;

                    if (string.IsNullOrEmpty(deviceId))
                    {
                        // Try to find a gantry device
                        foreach (var deviceX in _motionKernel.GetDevices())
                        {
                            if (deviceX.Type == MotionDeviceType.Gantry && deviceX.IsEnabled &&
                                _motionKernel.IsDeviceConnected(deviceX.Id))
                            {
                                deviceId = deviceX.Id;
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(deviceId))
                        {
                            MessageBox.Show("No gantry device connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
                else if (!_motionKernel.IsDeviceConnected(deviceId))
                {
                    MessageBox.Show($"Gantry device {deviceId} is not connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // If no positions provided, show a message
                if (positionNames == null || !positionNames.Any())
                {
                    MessageBox.Show("No positions specified for the path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Convert to list to ensure multiple enumeration is possible
                var path = positionNames.ToList();

                // Validate all positions exist
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                {
                    MessageBox.Show($"Device {deviceId} not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                foreach (var position in path)
                {
                    if (!device.Positions.ContainsKey(position))
                    {
                        MessageBox.Show($"Position '{position}' is not defined for gantry {deviceId}.",
                            "Position Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                StatusBarTextBlock.Text = $"Moving gantry {deviceId} along path: {string.Join(" → ", path)}...";
                _logger.Information("Moving gantry {DeviceId} along path with {Count} waypoints", deviceId, path.Count);

                // Create a cancellation token source for potential cancellation
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    // Execute the path movement
                    bool success = await _motionKernel.MoveAlongPathAsync(deviceId, path, cts.Token);

                    StatusBarTextBlock.Text = success
                        ? $"Gantry {deviceId} successfully moved along path."
                        : $"Failed to move gantry {deviceId} along path.";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveGantryAlongPath for device {DeviceId}", deviceId ?? "unknown");
                StatusBarTextBlock.Text = "Error moving gantry along path.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Moves a hexapod device along a planned path of multiple positions
        /// </summary>
        /// <param name="deviceId">The ID of the hexapod device</param>
        /// <param name="positionNames">The sequence of position names to visit</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task MoveHexapodAlongPath(string deviceId, IEnumerable<string> positionNames)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate device ID
                if (string.IsNullOrEmpty(deviceId) || !_motionKernel.IsDeviceConnected(deviceId))
                {
                    MessageBox.Show($"Hexapod device {deviceId} is not connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Convert to list to ensure multiple enumeration is possible
                var path = positionNames.ToList();

                if (path.Count == 0)
                {
                    MessageBox.Show("Path contains no positions.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Validate all positions exist
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                {
                    MessageBox.Show($"Device {deviceId} not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                foreach (var position in path)
                {
                    if (!device.Positions.ContainsKey(position))
                    {
                        MessageBox.Show($"Position '{position}' is not defined for hexapod {deviceId}.",
                            "Position Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                StatusBarTextBlock.Text = $"Moving hexapod {deviceId} along path: {string.Join(" → ", path)}...";
                _logger.Information("Moving hexapod {DeviceId} along path with {Count} waypoints", deviceId, path.Count);

                // Create a cancellation token source for potential cancellation
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    // Execute the path movement
                    bool success = await _motionKernel.MoveAlongPathAsync(deviceId, path, cts.Token);

                    StatusBarTextBlock.Text = success
                        ? $"Hexapod {deviceId} successfully moved along path."
                        : $"Failed to move hexapod {deviceId} along path.";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveHexapodAlongPath for device {DeviceId}", deviceId);
                StatusBarTextBlock.Text = $"Error moving hexapod {deviceId} along path.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task MoveHexapodToPosition(string deviceId, string positionName)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if the device is valid and connected
                if (string.IsNullOrEmpty(deviceId))
                {
                    // Try to find a hexapod device if no device ID is provided
                    foreach (var thisdevice in _motionKernel.GetDevices())
                    {
                        if (thisdevice.Type == MotionDeviceType.Hexapod && thisdevice.IsEnabled &&
                            _motionKernel.IsDeviceConnected(thisdevice.Id))
                        {
                            deviceId = thisdevice.Id;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(deviceId))
                    {
                        MessageBox.Show("No hexapod device connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else if (!_motionKernel.IsDeviceConnected(deviceId))
                {
                    MessageBox.Show($"Hexapod device {deviceId} is not connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if the position exists
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
                if (device == null || !device.Positions.ContainsKey(positionName))
                {
                    // Position doesn't exist, show a message
                    MessageBox.Show($"The position '{positionName}' is not defined for hexapod {deviceId}. You may need to teach this position first.",
                        "Position Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusBarTextBlock.Text = $"Moving hexapod {deviceId} to {positionName} position...";
                bool success = await _motionKernel.MoveToPositionAsync(deviceId, positionName);
                StatusBarTextBlock.Text = success
                    ? $"Hexapod {deviceId} moved to {positionName} position."
                    : $"Failed to move hexapod {deviceId} to {positionName} position.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveHexapodToPosition for device {DeviceId} to position {PositionName}", deviceId, positionName);
                StatusBarTextBlock.Text = $"Error moving hexapod {deviceId} to {positionName} position.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveGantryToPosition(string positionName)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the gantry device ID
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    // Try to find a gantry device
                    foreach (var gantrydevice in _motionKernel.GetDevices())
                    {
                        if (gantrydevice.Type == MotionDeviceType.Gantry && gantrydevice.IsEnabled &&
                            _motionKernel.IsDeviceConnected(gantrydevice.Id))
                        {
                            gantryId = gantrydevice.Id;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(gantryId))
                {
                    MessageBox.Show("No gantry device connected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if the position exists
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == gantryId);
                if (device == null || !device.Positions.ContainsKey(positionName))
                {
                    // Position doesn't exist, show a message
                    MessageBox.Show($"The position '{positionName}' is not defined. You may need to teach this position first.",
                        "Position Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusBarTextBlock.Text = $"Moving gantry to {positionName} position...";
                bool success = await _motionKernel.MoveToPositionAsync(gantryId, positionName);
                StatusBarTextBlock.Text = success ? $"Gantry moved to {positionName} position." : $"Failed to move gantry to {positionName} position.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MoveGantryToPosition");
                StatusBarTextBlock.Text = $"Error moving gantry to {positionName} position.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ControlGripper(string gripperName, bool activate)
        {
            try
            {
                if (deviceManager == null)
                {
                    MessageBox.Show("IO system not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the pin number from the gripper name
                int pinNumber = -1;
                switch (gripperName)
                {
                    case "L_Gripper":
                        pinNumber = 0; // Use actual pin numbers from your configuration
                        break;
                    case "R_Gripper":
                        pinNumber = 2;
                        break;
                    case "Vacuum_Base":
                        pinNumber = 10;
                        break;
                    case "UV_PLC1":
                        pinNumber = 14;
                        break;
                    case "UV_PLC2":
                        pinNumber = 13;
                        break;
                    default:
                        throw new ArgumentException($"Unknown gripper: {gripperName}");
                }

                // Control the output pin
                string deviceName = "IOBottom"; // Or whichever device controls the grippers
                StatusBarTextBlock.Text = $"{(activate ? "Activating" : "Deactivating")} {gripperName}...";

                var device = deviceManager.GetDevice(deviceName);
                if (device != null)
                {
                    if (activate)
                    {
                        device.SetOutput(gripperName);
                    }
                    else
                    {
                        device.ClearOutput(gripperName);
                    }
                    StatusBarTextBlock.Text = $"{gripperName} {(activate ? "activated" : "deactivated")}.";
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to {(activate ? "activate" : "deactivate")} {gripperName}: Device not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in ControlGripper");
                StatusBarTextBlock.Text = $"Error controlling {gripperName}.";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
