﻿using MotionServiceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UaaSolutionWpf.Controls;
using UaaSolutionWpf.Data;
using static SkiaSharp.HarfBuzz.SKShaper;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {
        private async void ExecuteUVButton_Click(object sender, RoutedEventArgs e)
        {
            // Ask if user wants to activate UV
            var result = MessageBox.Show("Do you want to activate the UV curing process?",
                                       "UV Activation",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {


                await ExecuteUVCuringUAA();
            }
            else
            {
                SetStatus("UV activation cancelled");
                _logger.Information("UV activation cancelled by user");
            }

        }

        /// <summary>
        /// Moves to the UV position and prepares for UV curing
        /// </summary>
        private async void MoveToUVButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                tecController.LowCurrent_Click(sender, e);

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

                    SetStatus("Extending UV Head");
                    //activate the UV head
                    await pneumaticSlideManager.GetSlide("UV_Head").ExtendAsync();
                    _logger.Information("UV Head extended successfully");

                    int delaySec = 2;
                    for (int i = 0; i < delaySec; i++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }

                    // Create a task to handle the message box with timeout
                    //bool performAlignment = await ShowMessageBoxWithTimeoutAsync(
                    //    "Do you want to perform alignment?",
                    //    "Alignment",
                    //    MessageBoxButton.YesNo,
                    //    MessageBoxImage.Question,
                    //    5000); // 5000 milliseconds = 5 seconds

                    //if (performAlignment)
                    {
                        // Start the alignment process
                        SetStatus("start Left/Right lens scanning inprogress..");
                        _logger.Information("start Left/Right lens scanning inprogress..");
                        await RunSequentialScan();
                    }
                    //else
                    //{
                    //    SetStatus("Alignment skipped");
                    //    _logger.Information("Alignment skipped by user");
                    //}

                    //show final value after UV peak
                    if (ChannelSelectionComboBox.SelectedItem is RealTimeDataChannel selectedChannel)
                    {
                        _logger.Information("Reading value for Dry peak");
                        MeasurementValue readVal;
                        if (realTimeDataManager.TryGetChannelValue(selectedChannel.ChannelName, out readVal))
                        {
                            DryValueText.Text = MeasurementValueFormatter.FormatValue(readVal);
                            _logger.Information($"Uv peak value: {UvValueText.Text}");
                        }
                        else
                        {
                            DryValueText.Text = "No value";
                            _logger.Warning("No value found for Dry peak");
                        }
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

            await ExecuteUVCuringUAA();
            await UnloadingUAA();
        }

        // Helper method to show a message box with timeout
        private async Task<bool> ShowMessageBoxWithTimeoutAsync(string message, string title,
            MessageBoxButton buttons, MessageBoxImage icon, int timeoutMilliseconds)
        {
            // Create a TaskCompletionSource to handle the result
            var tcs = new TaskCompletionSource<bool>();

            // Create a timer for the timeout
            var timer = new System.Threading.Timer(_ =>
            {
                // When the timer elapses, close the message box and assume Yes
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Try to find the active MessageBox window and close it
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window.Title == title)
                        {
                            window.Close();
                            break;
                        }
                    }

                    // Set the result to true (Yes)
                    tcs.TrySetResult(true);
                    _logger.Information("Alignment message box timed out - proceeding with alignment");
                });
            }, null, timeoutMilliseconds, Timeout.Infinite);

            // Show the message box on the UI thread
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Show the message box
                    var result = MessageBox.Show(message, title, buttons, icon);

                    // Cancel the timer since user responded
                    timer.Change(Timeout.Infinite, Timeout.Infinite);

                    // Set the result based on the user's choice
                    tcs.TrySetResult(result == MessageBoxResult.Yes);

                    _logger.Information($"User selected {result} for alignment message box");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error showing message box with timeout");
                    // Default to true in case of error
                    tcs.TrySetResult(true);
                }
            });

            // Return the result
            return await tcs.Task;
        }
        public static class MeasurementValueFormatter
        {
            public static string FormatValue(MeasurementValue value)
            {
                double displayValue = value.Value;
                string suffix = string.Empty;

                if (displayValue < 1e-6)
                {
                    displayValue *= 1e9;
                    suffix = "n";
                }
                else if (displayValue < 1e-3)
                {
                    displayValue *= 1e6;
                    suffix = "µ";
                }
                else if (displayValue < 1)
                {
                    displayValue *= 1e3;
                    suffix = "m";
                }

                return $"{displayValue:F2} {suffix}{value.Unit}";
            }
        }
        public async Task RunSequentialScan()
        {
            try
            {
                // Set to Fine mode
                AutoAlignmentControl.IsFineModeSelected = true;

                // Set status message
                AutoAlignmentControl.SetStatus("Left lens scanning in progress...");

                // Start left scan and wait for it to complete
                bool leftScanStarted = await AutoAlignmentControl.StartLeftScan();
                if (!leftScanStarted)
                {
                    AutoAlignmentControl.SetStatus("Failed to start left scan");
                    return;
                }

                // After left scan completes, start right scan
                AutoAlignmentControl.SetStatus("Right lens scanning started...");

                // Start right scan and wait for it to complete
                bool rightScanStarted = await AutoAlignmentControl.StartRightScan();
                if (!rightScanStarted)
                {
                    AutoAlignmentControl.SetStatus("Failed to start right scan");
                    return;
                }

                // Both scans complete
                AutoAlignmentControl.SetStatus("Both lens scans completed successfully");
            }
            catch (Exception ex)
            {
                AutoAlignmentControl.SetStatus($"Error during scan sequence: {ex.Message}");
                // Additional error handling as needed
            }
        }


        public async Task ExecuteUVCuringUAA()
        {
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






                int curetimeSecond = 210;
                _logger.Information($"UV activated successfully, typical curing time is {curetimeSecond} seconds");

                for (int i = 0; i < curetimeSecond; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    SetStatus($" - UV curing in progress... {curetimeSecond - i} seconds remaining");
                }



                SetStatus("UV curing completed");
                _logger.Information("UV curing completed");


                //show final value after dry peak
                if (ChannelSelectionComboBox.SelectedItem is RealTimeDataChannel selectedChannel)
                {
                    MeasurementValue readVal;
                    if (realTimeDataManager.TryGetChannelValue(selectedChannel.ChannelName, out readVal))
                    {
                        UvValueText.Text = MeasurementValueFormatter.FormatValue(readVal);
                    }
                    else
                    {
                        UvValueText.Text = "No value";
                    }

                }



            }
            else
            {
                SetStatus("Device manager not initialized, cannot activate UV");
                _logger.Warning("Device manager not initialized, cannot activate UV");
            }
        }
    }
}
