using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using UaaSolutionWpf.Commands;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf
{
    // Example methods to add to your VisionMotionWindow class
    public partial class VisionMotionWindow
    {
        private DUTManager _dutManager;
        private string _currentDUTFilePath;

        private void InitializeDUTManager()
        {
            // Initialize the DUT manager with the RealTimeDataManager
            if (realTimeDataManager != null)
            {
                _dutManager = new DUTManager(realTimeDataManager, _logger);
                _logger.Information("DUT Manager initialized");
            }
            else
            {
                _logger.Warning("Cannot initialize DUT Manager: RealTimeDataManager is null");
            }
        }

        // Method to register a new DUT
        private async void RegisterNewDUT_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dutManager == null)
                {
                    InitializeDUTManager();
                    if (_dutManager == null)
                    {
                        MessageBox.Show("DUT Manager is not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // In a real application, you would get the serial number from a UI element
                // For this example, we'll use a simple text input dialog or generate one
                string serialNumber = GetSerialNumberFromUser();
                if (string.IsNullOrEmpty(serialNumber))
                {
                    return; // User canceled or didn't enter a serial number
                }

                // Create a command to register the birth of a DUT
                var command = new DUTBirthCommand(
                    realTimeDataManager,
                    serialNumber,
                    "StandardRecipe",  // Optional recipe ID
                    "Operator1",       // Optional operator ID
                    _logger
                );

                // Execute the command
                StatusBarTextBlock.Text = "Registering DUT...";
                var result = await command.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "DUT registered successfully";
                    _logger.Information("DUT registration result: {Result}", result.Message);

                    // Extract the file path from the success message
                    string message = result.Message;
                    int filePathIndex = message.LastIndexOf("Log file: ");
                    if (filePathIndex != -1)
                    {
                        _currentDUTFilePath = message.Substring(filePathIndex + 10); // "Log file: " is 10 characters

                        // Verify that the file exists before setting it as current DUT
                        if (File.Exists(_currentDUTFilePath))
                        {
                            _dutManager.SetCurrentDUT(_currentDUTFilePath);
                            _logger.Information("Set current DUT file to: {FilePath}", _currentDUTFilePath);
                        }
                        else
                        {
                            _logger.Warning("Created DUT file was not found at path: {FilePath}", _currentDUTFilePath);
                            MessageBox.Show($"DUT file was not found at expected location: {_currentDUTFilePath}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        _logger.Warning("Unable to extract file path from result message: {Message}", message);
                    }

                    MessageBox.Show($"DUT registered successfully: {serialNumber}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to register DUT: {result.Message}";
                    _logger.Warning("DUT registration failed: {Message}", result.Message);
                    MessageBox.Show($"Failed to register DUT: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error registering DUT";
                _logger.Error(ex, "Error in RegisterNewDUT_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Method to add a value to the current DUT record
        private async void AddDUTValue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dutManager == null || string.IsNullOrEmpty(_currentDUTFilePath))
                {
                    MessageBox.Show("No DUT is registered. Please register a DUT first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Verify file exists before trying to add value
                if (!File.Exists(_currentDUTFilePath))
                {
                    _logger.Warning("Current DUT file not found at path: {FilePath}", _currentDUTFilePath);
                    MessageBox.Show($"The DUT file was not found at: {_currentDUTFilePath}. Please register a new DUT.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _currentDUTFilePath = null; // Reset the path since it's invalid
                    return;
                }

                // In a real application, you would get these values from UI elements
                string state = "AlignmentTest"; // Example: State of the DUT
                string channelName = "Keithley Current"; // Example: Channel to read value from

                // Verify channel exists before trying to add value
                if (!realTimeDataManager.ChannelExists(channelName))
                {
                    _logger.Warning("Channel not found: {ChannelName}", channelName);
                    MessageBox.Show($"The specified channel '{channelName}' was not found. Please select a valid channel.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a command to add a value to the DUT record
                var command = new DUTAddValueCommand(
                    realTimeDataManager,
                    _currentDUTFilePath,
                    state,
                    channelName,
                    _logger
                );

                // Execute the command
                StatusBarTextBlock.Text = $"Adding {channelName} value to DUT record...";
                var result = await command.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "Value added to DUT record";
                    _logger.Information("DUT value addition result: {Result}", result.Message);
                    MessageBox.Show($"Value added to DUT record: {result.Message}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to add value to DUT record: {result.Message}";
                    _logger.Warning("DUT value addition failed: {Message}", result.Message);
                    MessageBox.Show($"Failed to add value to DUT record: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error adding value to DUT record";
                _logger.Error(ex, "Error in AddDUTValue_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Method to add a manual value to the current DUT record
        private async void AddManualDUTValue_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dutManager == null || string.IsNullOrEmpty(_currentDUTFilePath))
                {
                    MessageBox.Show("No DUT is registered. Please register a DUT first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Verify file exists before trying to add value
                if (!File.Exists(_currentDUTFilePath))
                {
                    _logger.Warning("Current DUT file not found at path: {FilePath}", _currentDUTFilePath);
                    MessageBox.Show($"The DUT file was not found at: {_currentDUTFilePath}. Please register a new DUT.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _currentDUTFilePath = null; // Reset the path since it's invalid
                    return;
                }

                // In a real application, you would get these values from UI elements
                string state = "ManualInspection"; // Example: State of the DUT
                double value = 0.95; // Example: Value
                string unit = "Quality"; // Example: Unit

                // Create a command to add a manual value to the DUT record
                var command = new DUTAddValueCommand(
                    realTimeDataManager,
                    _currentDUTFilePath,
                    state,
                    value,
                    unit,
                    _logger
                );

                // Execute the command
                StatusBarTextBlock.Text = "Adding manual value to DUT record...";
                var result = await command.ExecuteAsync(CancellationToken.None);

                // Update status based on result
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "Manual value added to DUT record";
                    _logger.Information("DUT manual value addition result: {Result}", result.Message);
                    MessageBox.Show($"Manual value added to DUT record: {result.Message}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"Failed to add manual value to DUT record: {result.Message}";
                    _logger.Warning("DUT manual value addition failed: {Message}", result.Message);
                    MessageBox.Show($"Failed to add manual value to DUT record: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error adding manual value to DUT record";
                _logger.Error(ex, "Error in AddManualDUTValue_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // Method to test a full DUT workflow
        private async void TestDUtWorkFlow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dutManager == null)
                {
                    InitializeDUTManager();
                    if (_dutManager == null)
                    {
                        MessageBox.Show("DUT Manager is not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Generate a serial number
                string serialNumber = $"SLED{DateTime.Now:yyMMddHHmmss}";

                // Step 1: Register the DUT first (separate from the sequence)
                StatusBarTextBlock.Text = "Registering DUT...";
                string dutFilePath = await _dutManager.CreateDUTAsync(serialNumber, "TestRecipe", "AutomatedTest");

                if (string.IsNullOrEmpty(dutFilePath))
                {
                    MessageBox.Show("Failed to create DUT record", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _logger.Information("Created DUT with file path: {FilePath}", dutFilePath);

                // Now create the sequence for testing steps
                var sequence = new CommandSequence(
                    "DUT Test Workflow",
                    "Complete DUT testing sequence",
                    _logger
                );

                // Step 2: Move to the first test position
                sequence.AddCommand(new MoveToNamedPositionCommand(
                    _motionKernel,
                    "3",
                    "SeeSLED",
                    _logger
                ));

                // Step 3: Delay to let things settle
                sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(500), _logger));

                // Step 4: Add a channel measurement in the first position
                sequence.AddCommand(new DUTAddValueCommand(
                    realTimeDataManager,
                    dutFilePath, // Use the already captured file path directly
                    "InitialAlignment",
                    "Keithley Current",
                    _logger
                ));

                // Step 5: Capture an image for reference
                sequence.AddCommand(new CameraImageCaptureCommand(
                    _cameraManager,
                    $"DUT_{serialNumber}",
                    true
                ));

                // Step 6: Move to the next test position
                sequence.AddCommand(new MoveToNamedPositionCommand(
                    _motionKernel,
                    "3",
                    "SeePIC",
                    _logger
                ));

                // Step 7: Delay to let things settle
                sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(500), _logger));

                // Step 8: Add another channel measurement
                sequence.AddCommand(new DUTAddValueCommand(
                    realTimeDataManager,
                    dutFilePath, // Use the same file path
                    "FinalAlignment",
                    "Keithley Current",
                    _logger
                ));

                // Step 9: Add a manual value for quality inspection
                sequence.AddCommand(new DUTAddValueCommand(
                    realTimeDataManager,
                    dutFilePath, // Use the same file path
                    "QualityCheck",
                    0.98,
                    "Score",
                    _logger
                ));

                // Execute the sequence
                StatusBarTextBlock.Text = "Running DUT test workflow...";
                var result = await sequence.ExecuteAsync(CancellationToken.None);

                // Handle result...
                if (result.Success)
                {
                    StatusBarTextBlock.Text = "DUT test workflow completed successfully";
                    _logger.Information("DUT workflow result: {Result}", result.Message);
                    MessageBox.Show($"DUT test workflow completed successfully for {serialNumber}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusBarTextBlock.Text = $"DUT test workflow failed: {result.Message}";
                    _logger.Warning("DUT workflow failed: {Message}", result.Message);
                    MessageBox.Show($"DUT test workflow failed: {result.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusBarTextBlock.Text = "Error in DUT test workflow";
                _logger.Error(ex, "Error in TestDUTWorkflow_Click");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // Helper method to get serial number from user
        private string GetSerialNumberFromUser()
        {
            // In a production application, this would be a proper dialog
            // For this example, we'll generate a serial number or you could add a dialog here
            string generatedSN = $"SLED{DateTime.Now:yyMMddHHmmss}";

            // Simple message box to confirm
            var result = MessageBox.Show($"Use generated serial number: {generatedSN}?",
                "Serial Number", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.OK)
            {
                return generatedSN;
            }
            else
            {
                return null; // User canceled
            }
        }

        // Define a simple class to hold command variables
        private class CommandVariableString
        {
            public string Value { get; set; }
        }
    }
}