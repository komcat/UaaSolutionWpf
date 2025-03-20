using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MotionServiceLib;
using EzIIOLib;
using Serilog.Core;
using System.Windows.Controls;

namespace UaaSolutionWpf
{
    // Implementation for VisionMotionWindow.RunSequence.cs
    public partial class VisionMotionWindow
    {

        private string serialnumberDUT= "";

        private void SerialNumberDialog()
        {
            // Create a new window
            Window dialog = new Window
            {
                Title = "Enter Serial Number",
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            // Create the layout
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Create label
            Label label = new Label
            {
                Content = "Serial Number:",
                Margin = new Thickness(10, 10, 5, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(label, 0);
            Grid.SetColumn(label, 0);

            // Create text box
            TextBox textBox = new TextBox
            {
                Margin = new Thickness(5, 10, 10, 5),
                Height = 25,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(textBox, 0);
            Grid.SetColumn(textBox, 1);

            // Create button stack panel
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 10, 10)
            };
            Grid.SetRow(buttonPanel, 2);
            Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 2);

            // Create OK button
            Button okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };

            // Create Cancel button
            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 25,
                IsCancel = true
            };

            // Add buttons to panel
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            // Add controls to grid
            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            // Set grid as content
            dialog.Content = grid;

            // Set dialog result
            bool? result = false;
            okButton.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    // Store the serial number for later use
                    string serialNumber = textBox.Text.Trim();

                    // Here you would typically set a property or variable with the serial number
                    // For example:
                    // this.SerialNumber = serialNumber;
                    this.serialnumberDUT = serialNumber;


                    result = true;
                    dialog.Close();
                }
                else
                {
                    MessageBox.Show("Please enter a valid serial number.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            cancelButton.Click += (s, e) =>
            {
                result = false;
                dialog.Close();
            };

            // Set focus to the text box
            dialog.Loaded += (s, e) => textBox.Focus();

            // Show dialog
            dialog.ShowDialog();


        }

        private void InitializeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                serialnumberDUT="";
                // Popup dialog box to enter serial number
                SerialNumberDialog();

                if (serialnumberDUT.Length>=1)
                {
                    SerialNumberText.Text = serialnumberDUT;
                    SetStatus($"Serial Number entered {serialnumberDUT}.");
                }
                else
                {
                    SetStatus("Serial Number not entered.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in InitializeButton_Click");
                SetStatus("Error initializing motion system");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        //// <summary>
        /// Resets all motion devices (gantry and hexapods) to their home positions using shortest path
        /// </summary>
        private async void RestHomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Update status on UI thread
                SetStatus("Homing all devices...");

                await pneumaticSlideManager.GetSlide("UV_Head").RetractAsync();
                await pneumaticSlideManager.GetSlide("Dispenser_Head").RetractAsync();
                await pneumaticSlideManager.GetSlide("Pick_Up_Tool").RetractAsync();


                // Find all connected gantry devices
                var gantryDevices = _motionKernel.GetDevices()
                    .Where(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id))
                    .ToList();

                // Find hexapod devices (both left and right)
                var hexapodDevices = _motionKernel.GetDevices()
                    .Where(d => d.Type == MotionDeviceType.Hexapod && _motionKernel.IsDeviceConnected(d.Id))
                    .ToList();

                // Find left and right hexapods specifically
                string leftHexapodId = null;
                string rightHexapodId = null;

                foreach (var device in hexapodDevices)
                {
                    if (device.Name.ToLower().Contains("left"))
                    {
                        leftHexapodId = device.Id;
                        _logger.Information("Found left hexapod with ID {DeviceId}", leftHexapodId);
                    }
                    else if (device.Name.ToLower().Contains("right"))
                    {
                        rightHexapodId = device.Id;
                        _logger.Information("Found right hexapod with ID {DeviceId}", rightHexapodId);
                    }
                }

                if (gantryDevices.Count == 0 && hexapodDevices.Count == 0)
                {
                    MessageBox.Show("No connected motion devices found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Track overall success
                bool allSuccess = true;
                List<Task> homingTasks = new List<Task>();

                // Create tasks for all devices to move to home
                // Home all gantry devices
                foreach (var device in gantryDevices)
                {
                    var task = Task.Run(async () =>
                    {
                        // Use Dispatcher to update UI from background thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            SetStatus($"(Not actual) Homing gantry device {device.Name}...");
                        });

                        bool success = await _motionKernel.MoveToDestinationShortestPathAsync(device.Id, "Home");
                        if (!success)
                        {
                            _logger.Warning("Failed to home gantry device {DeviceId}", device.Id);
                            allSuccess = false;
                        }
                        else
                        {
                            _logger.Information("Successfully homed gantry device {DeviceId}", device.Id);
                        }
                    });
                    homingTasks.Add(task);
                }

                // Home left hexapod if found
                if (!string.IsNullOrEmpty(leftHexapodId))
                {
                    var leftHexapodTask = Task.Run(async () =>
                    {
                        try
                        {
                            // Use Dispatcher to update UI from background thread
                            await Dispatcher.InvokeAsync(() =>
                            {
                                SetStatus("Moving left hexapod to home position...");
                            });

                            // First check if there's a "Home" position defined
                            var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == leftHexapodId);
                            if (device != null && device.Positions.ContainsKey("Home"))
                            {
                                // Use MoveToDestinationShortestPathAsync to shortest path
                                bool success = await _motionKernel.MoveToDestinationShortestPathAsync(leftHexapodId, "Home");
                                if (!success)
                                {
                                    _logger.Warning("Failed to move left hexapod to Home position");
                                    allSuccess = false;
                                }
                                else
                                {
                                    _logger.Information("Successfully moved left hexapod to Home position");
                                }
                            }
                            else
                            {
                                // Fall back to HomeAsync method
                                bool success = await _motionKernel.HomeDeviceAsync(leftHexapodId);
                                if (!success)
                                {
                                    _logger.Warning("Failed to home left hexapod {DeviceId}", leftHexapodId);
                                    allSuccess = false;
                                }
                                else
                                {
                                    _logger.Information("Successfully homed left hexapod {DeviceId}", leftHexapodId);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error homing left hexapod {DeviceId}", leftHexapodId);
                            allSuccess = false;
                        }
                    });
                    homingTasks.Add(leftHexapodTask);
                }

                // Home right hexapod if found
                if (!string.IsNullOrEmpty(rightHexapodId))
                {
                    var rightHexapodTask = Task.Run(async () =>
                    {
                        try
                        {
                            // Use Dispatcher to update UI from background thread
                            await Dispatcher.InvokeAsync(() =>
                            {
                                SetStatus("Moving right hexapod to home position...");
                            });

                            // First check if there's a "Home" position defined
                            var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == rightHexapodId);
                            if (device != null && device.Positions.ContainsKey("Home"))
                            {
                                // Use MoveToDestinationShortestPathAsync to shortest path
                                bool success = await _motionKernel.MoveToDestinationShortestPathAsync(rightHexapodId, "Home");
                                if (!success)
                                {
                                    _logger.Warning("Failed to move right hexapod to Home position");
                                    allSuccess = false;
                                }
                                else
                                {
                                    _logger.Information("Successfully moved right hexapod to Home position");
                                }
                            }
                            else
                            {
                                // Fall back to HomeAsync method
                                bool success = await _motionKernel.HomeDeviceAsync(rightHexapodId);
                                if (!success)
                                {
                                    _logger.Warning("Failed to home right hexapod {DeviceId}", rightHexapodId);
                                    allSuccess = false;
                                }
                                else
                                {
                                    _logger.Information("Successfully homed right hexapod {DeviceId}", rightHexapodId);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error homing right hexapod {DeviceId}", rightHexapodId);
                            allSuccess = false;
                        }
                    });
                    homingTasks.Add(rightHexapodTask);
                }

                // Wait for all homing operations to complete
                await Task.WhenAll(homingTasks);

                // Update UI based on result (on UI thread)
                if (allSuccess)
                {
                    SetStatus("All devices successfully homed");
                    _logger.Information("All devices successfully homed");
                    MessageBox.Show("All devices successfully homed", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    SetStatus("Some devices failed to home. Check logs for details.");
                    _logger.Warning("Some devices failed to home");
                    MessageBox.Show("Some devices failed to home. Check logs for details.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in RestHomeButton_Click");
                SetStatus("Error homing devices");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Helper method to safely update the status text on the UI thread
        /// </summary>
        private void SetStatus(string status)
        {
            // If we're already on the UI thread, update directly
            if (Dispatcher.CheckAccess())
            {
                StatusBarTextBlock.Text = status;
                _logger.Information("<status>:"+status);
            }
            else
            {
                // Otherwise, invoke on the UI thread
                Dispatcher.Invoke(() => StatusBarTextBlock.Text = status);
                _logger.Information("<status>:" + status);
            }
        }

        /// <summary>
        /// Moves the gantry to the probes position for SLED and PIC
        /// </summary>
        private async void MovetoProbesButton_Click(object sender, RoutedEventArgs e)
        {

            MovetoProbesButton.Background = System.Windows.Media.Brushes.Orange;
            try
            {
               

                if (_motionKernel == null)
                {
                    MessageBox.Show("Motion system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the active gantry device ID
                string gantryId = _activeGantryDeviceId;
                if (string.IsNullOrEmpty(gantryId))
                {
                    // Try to find a gantry device
                    var gantryDevice = _motionKernel.GetDevices()
                        .FirstOrDefault(d => d.Type == MotionDeviceType.Gantry && _motionKernel.IsDeviceConnected(d.Id));

                    if (gantryDevice == null)
                    {
                        MessageBox.Show("No connected gantry device found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    gantryId = gantryDevice.Id;
                }
                bool gripSuccess = deviceManager.SetOutput("IOBottom", "Vacuum_Base");
                SetStatus("Set Vacuum Base ON");
                await Task.Delay(1000);


                MotionDevice leftHexapodDevice = GetDeviceByName("hex-left");
                MotionDevice rightHexapodDevice = GetDeviceByName("hex-right");
                await _motionKernel.MoveToDestinationShortestPathAsync(leftHexapodDevice.Id, "LeftSeeProbe");
                await _motionKernel.MoveToDestinationShortestPathAsync(rightHexapodDevice.Id, "RightSeeProbe");
                // First move to the SLED position
                SetStatus("Moving to SLED probe position...");
                bool success = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeeSLED");

                if (success)
                {
                    _logger.Information("Successfully moved to SLED probe position");
                    // Wait for 2 seconds to allow for observation
                    await Task.Delay(2000);

                    var resultSeeSled = MessageBox.Show("Manual probe the SLED, when finish click Yes?",
                                                      "SLED Probe",
                                                      MessageBoxButton.YesNo,
                                                      MessageBoxImage.Question);
                    if (resultSeeSled == MessageBoxResult.No)
                    {
                        SetStatus("User cancelled SLED probe");
                        _logger.Information("User cancelled SLED probe");
                        return;
                    }

                    // Then move to the PIC position
                    SetStatus("Moving to PIC probe position...");
                    success = await _motionKernel.MoveToDestinationShortestPathAsync(gantryId, "SeePIC");

                    if (success)
                    {
                        SetStatus("Successfully moved to PIC probe position");
                        _logger.Information("Successfully moved to PIC probe position");

                        var resultSeePIC = MessageBox.Show("Manual probe the PIC, when finish click Yes?",
                                                         "PIC Probe",
                                                         MessageBoxButton.YesNo,
                                                         MessageBoxImage.Question);
                        if (resultSeePIC == MessageBoxResult.No)
                        {
                            SetStatus("User cancelled PIC probe");
                            _logger.Information("User cancelled PIC probe");
                            return;
                        }
                    }
                    else
                    {
                        SetStatus("Failed to move to PIC probe position");
                        _logger.Warning("Failed to move to PIC probe position");
                    }

                    SetStatus("Turning on High SLED current ");
                    tecController.HighCurrent_Click(sender, e);

                }
                else
                {
                    SetStatus("Failed to move to SLED probe position");
                    _logger.Warning("Failed to move to SLED probe position");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in MovetoProbesButton_Click");
                SetStatus("Error moving to probe positions");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            MovetoProbesButton.Background = System.Windows.Media.Brushes.LightGreen;
        }
    }
}