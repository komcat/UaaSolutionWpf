using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UaaSolutionWpf.Workflow
{
    public class WorkflowController
    {
        private Dictionary<string, WorkflowStep> _workflowSteps;
        private string _currentStepId;

        // UI elements
        private Panel _workflowButtonPanel;
        private TextBlock _statusTextBlock;
        private ProgressBar _statusProgressBar;
        private TextBlock _timeTextBlock;

        // Result tracking
        private Dictionary<string, TextBlock> _resultBlocks;
        private Dictionary<string, TextBlock> _timestampBlocks;

        private bool _isRunning = false;
        private bool _isPaused = false;

        public WorkflowController(Panel buttonPanel, TextBlock statusBlock, ProgressBar progressBar = null, TextBlock timeBlock = null)
        {
            _workflowButtonPanel = buttonPanel;
            _statusTextBlock = statusBlock;
            _statusProgressBar = progressBar;
            _timeTextBlock = timeBlock;

            _resultBlocks = new Dictionary<string, TextBlock>();
            _timestampBlocks = new Dictionary<string, TextBlock>();

            InitializeWorkflow();
        }

        public void InitializeWorkflow()
        {
            _workflowSteps = new Dictionary<string, WorkflowStep>
            {
                ["init"] = new WorkflowStep
                {
                    Name = "Initialize",
                    ButtonText = "Initialize System",
                    NextStepId = "readData",
                    Operation = InitSystemAsync,
                    RequiresConfirmation = false,
                    MaxRetries = 2
                },
                ["readData"] = new WorkflowStep
                {
                    Name = "Read Data",
                    ButtonText = "Read Sensor Data",
                    NextStepId = "userConfirm",
                    Operation = ReadSensorDataAsync,
                    RequiresConfirmation = false,
                    MaxRetries = 3
                },
                ["userConfirm"] = new WorkflowStep
                {
                    Name = "User Confirmation",
                    ButtonText = "Confirm Completion",
                    NextStepId = "complete",
                    Operation = UserConfirmationAsync,
                    RequiresConfirmation = true,
                    MaxRetries = 1
                },
                ["complete"] = new WorkflowStep
                {
                    Name = "Complete",
                    ButtonText = "Process Complete",
                    NextStepId = "init", // Loop back to beginning
                    Operation = CompleteProcessAsync,
                    RequiresConfirmation = false,
                    MaxRetries = 0
                }
            };

            _currentStepId = "init"; // Start at the beginning

            // Clear result trackers
            _resultBlocks.Clear();
            _timestampBlocks.Clear();

            UpdateWorkflowUI();
        }

        private void UpdateWorkflowUI()
        {
            // Clear existing content
            _workflowButtonPanel.Children.Clear();

            // Create a grid to hold buttons, results, and timestamps
            var workflowGrid = new Grid();

            // Define columns: Button, Result, Timestamp
            workflowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            workflowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            workflowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

            // Add header row
            workflowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Create headers
            var actionHeader = new TextBlock
            {
                Text = "Action",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(actionHeader, 0);
            Grid.SetColumn(actionHeader, 0);

            var resultHeader = new TextBlock
            {
                Text = "Result",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(resultHeader, 0);
            Grid.SetColumn(resultHeader, 1);

            var timestampHeader = new TextBlock
            {
                Text = "Timestamp",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(timestampHeader, 0);
            Grid.SetColumn(timestampHeader, 2);

            workflowGrid.Children.Add(actionHeader);
            workflowGrid.Children.Add(resultHeader);
            workflowGrid.Children.Add(timestampHeader);

            // Add a separator under headers
            var separator = new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(5, 20, 5, 5)
            };
            Grid.SetRow(separator, 0);
            Grid.SetColumn(separator, 0);
            Grid.SetColumnSpan(separator, 3);
            workflowGrid.Children.Add(separator);

            // Add row for each workflow step
            int rowIndex = 1;
            foreach (var entry in _workflowSteps)
            {
                var step = entry.Value;
                workflowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Create button
                var button = new Button
                {
                    Content = step.ButtonText,
                    Tag = entry.Key,
                    Margin = new Thickness(5),
                    Padding = new Thickness(5),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = (entry.Key == _currentStepId)
                };
                button.Click += WorkflowButton_Click;
                Grid.SetRow(button, rowIndex);
                Grid.SetColumn(button, 0);
                workflowGrid.Children.Add(button);

                // Create result text block
                var resultBlock = new TextBlock
                {
                    Text = GetStatusText(step),
                    Margin = new Thickness(5),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(resultBlock, rowIndex);
                Grid.SetColumn(resultBlock, 1);
                workflowGrid.Children.Add(resultBlock);

                // Store reference to result block
                _resultBlocks[entry.Key] = resultBlock;

                // Create timestamp text block
                var timestampBlock = new TextBlock
                {
                    Text = GetTimestampText(step),
                    Margin = new Thickness(5)
                };
                Grid.SetRow(timestampBlock, rowIndex);
                Grid.SetColumn(timestampBlock, 2);
                workflowGrid.Children.Add(timestampBlock);

                // Store reference to timestamp block
                _timestampBlocks[entry.Key] = timestampBlock;

                rowIndex++;
            }

            _workflowButtonPanel.Children.Add(workflowGrid);

            // Update status display
            if (_statusTextBlock != null)
            {
                var currentStep = _workflowSteps[_currentStepId];
                _statusTextBlock.Text = $"Current step: {currentStep.Name} - {currentStep.Status}";

                if (!string.IsNullOrEmpty(currentStep.StatusMessage))
                {
                    _statusTextBlock.Text += $" ({currentStep.StatusMessage})";
                }
            }

            // Update progress bar if available
            if (_statusProgressBar != null)
            {
                var currentStep = _workflowSteps[_currentStepId];
                _statusProgressBar.IsIndeterminate = currentStep.Status == OperationStatus.InProgress;
                _statusProgressBar.Value = currentStep.Status == OperationStatus.Completed ? 100 : 0;
            }

            // Update time display if available
            if (_timeTextBlock != null && _workflowSteps[_currentStepId].StartTime.HasValue)
            {
                var currentStep = _workflowSteps[_currentStepId];
                _timeTextBlock.Text = $"Duration: {currentStep.Duration.TotalSeconds:F1}s";
            }
        }

        private string GetStatusText(WorkflowStep step)
        {
            switch (step.Status)
            {
                case OperationStatus.Completed:
                    return "✓ " + (string.IsNullOrEmpty(step.StatusMessage) ? "Completed successfully" : step.StatusMessage);
                case OperationStatus.Failed:
                    return "❌ " + (string.IsNullOrEmpty(step.StatusMessage) ? "Failed" : step.StatusMessage);
                case OperationStatus.InProgress:
                    return "⏳ In progress...";
                case OperationStatus.Waiting:
                    return "⏸ Waiting for confirmation";
                case OperationStatus.Skipped:
                    return "⏭ Skipped";
                case OperationStatus.Paused:
                    return "⏸ Paused";
                default:
                    return string.Empty;
            }
        }

        private string GetTimestampText(WorkflowStep step)
        {
            if (step.EndTime.HasValue)
            {
                return step.EndTime.Value.ToString("g"); // Short date and time pattern
            }
            else if (step.StartTime.HasValue)
            {
                return $"Started: {step.StartTime.Value.ToString("g")}";
            }
            return string.Empty;
        }

        private async void WorkflowButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var stepId = button.Tag as string;

            if (!_workflowSteps.ContainsKey(stepId)) return;

            var currentStep = _workflowSteps[stepId];

            // Disable all buttons during operation
            SetAllButtonsEnabled(false);
            UpdateStepStatusDisplay(currentStep, "Starting operation...");

            // Update result block
            if (_resultBlocks.TryGetValue(stepId, out var resultBlock))
            {
                resultBlock.Text = "⏳ Operation in progress...";
            }

            // Update timestamp block (start time)
            if (_timestampBlocks.TryGetValue(stepId, out var timestampBlock))
            {
                timestampBlock.Text = $"Started: {DateTime.Now.ToString("g")}";
            }

            // Execute with status tracking
            bool success = await currentStep.ExecuteWithStatus();

            // Update result block with completion status
            if (_resultBlocks.TryGetValue(stepId, out resultBlock))
            {
                resultBlock.Text = GetStatusText(currentStep);

                // Apply color based on status
                if (success)
                {
                    resultBlock.Foreground = Brushes.Green;
                }
                else
                {
                    resultBlock.Foreground = Brushes.Red;
                }
            }

            // Update timestamp block (end time)
            if (_timestampBlocks.TryGetValue(stepId, out timestampBlock))
            {
                timestampBlock.Text = GetTimestampText(currentStep);
            }

            if (success)
            {
                if (currentStep.RequiresConfirmation)
                {
                    UpdateStepStatusDisplay(currentStep, "Waiting for confirmation...");
                    currentStep.Status = OperationStatus.Waiting;

                    if (await ShowConfirmationDialog(currentStep.Name))
                    {
                        // Mark current step as completed after confirmation
                        currentStep.MarkCompleted("Confirmed by user");

                        // Update the result text block to show confirmation
                        if (_resultBlocks.TryGetValue(stepId, out var resultTextBlock))
                        {
                            resultTextBlock.Text = "✓ Confirmed and completed successfully";
                            resultTextBlock.Foreground = Brushes.Green;
                        }

                        // Update timestamp if needed
                        if (_timestampBlocks.TryGetValue(stepId, out var timeTextBlock))
                        {
                            timeTextBlock.Text = DateTime.Now.ToString("g");
                        }

                        _currentStepId = currentStep.NextStepId;
                    }
                }
                else
                {
                    _currentStepId = currentStep.NextStepId;
                }
            }
            else if (currentStep.CanRetry && await ShowRetryDialog(currentStep))
            {
                // Stay on current step for retry
                UpdateStepStatusDisplay(currentStep, $"Retry {currentStep.RetryCount}/{currentStep.MaxRetries}");

                if (_resultBlocks.TryGetValue(stepId, out resultBlock))
                {
                    resultBlock.Text = $"⚠ Failed - Retry #{currentStep.RetryCount}";
                    resultBlock.Foreground = Brushes.Orange;
                }
            }
            else
            {
                // Handle failure case - maybe reset or go to error recovery step
                UpdateStepStatusDisplay(currentStep, $"Failed: {currentStep.StatusMessage}");
            }

            UpdateWorkflowUI();
        }

        private void SetAllButtonsEnabled(bool enabled)
        {
            // This now needs to find buttons within the grid
            foreach (var child in _workflowButtonPanel.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var gridChild in grid.Children)
                    {
                        if (gridChild is Button button)
                        {
                            button.IsEnabled = enabled;
                        }
                    }
                }
            }
        }

        private void UpdateStepStatusDisplay(WorkflowStep step, string message = null)
        {
            // Update UI to show status information
            if (_statusTextBlock != null)
            {
                string statusText = message ?? step.StatusMessage;
                _statusTextBlock.Text = $"{step.Name}: {statusText}";
            }

            // Update progress bar if available
            if (_statusProgressBar != null)
            {
                if (step.Status == OperationStatus.InProgress)
                {
                    _statusProgressBar.IsIndeterminate = true;
                }
                else
                {
                    _statusProgressBar.IsIndeterminate = false;
                    _statusProgressBar.Value = step.Status == OperationStatus.Completed ? 100 : 0;
                }
            }

            // Update time display if available
            if (_timeTextBlock != null && step.StartTime.HasValue)
            {
                _timeTextBlock.Text = $"Duration: {step.Duration.TotalSeconds:F1}s";
            }

            // Update result block
            if (_resultBlocks.TryGetValue(step.Name.Replace(" ", "").ToLower(), out var resultBlock))
            {
                resultBlock.Text = GetStatusText(step);
            }
        }

        private Task<bool> ShowConfirmationDialog(string stepName)
        {
            var result = MessageBox.Show(
                $"Step '{stepName}' requires confirmation. Do you want to continue?",
                "Confirmation Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            bool confirmed = result == MessageBoxResult.Yes;

            // Update status text immediately based on confirmation result
            if (_statusTextBlock != null)
            {
                _statusTextBlock.Text = confirmed
                    ? $"{stepName}: Confirmed by user"
                    : $"{stepName}: Waiting for confirmation...";
            }

            return Task.FromResult(confirmed);
        }

        private Task<bool> ShowRetryDialog(WorkflowStep step)
        {
            var result = MessageBox.Show(
                $"Step '{step.Name}' failed: {step.StatusMessage}\n\nRetry? ({step.RetryCount}/{step.MaxRetries})",
                "Operation Failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return Task.FromResult(result == MessageBoxResult.Yes);
        }

        /// <summary>
        /// Adds play/pause buttons to the workflow UI
        /// </summary>
        public void AddPlayPauseControls()
        {
            var playPauseButtons = new UaaSolutionWpf.Controls.PlayPauseButtons();

            // Set initial button states
            playPauseButtons.SetPlayEnabled(true);
            playPauseButtons.SetPauseEnabled(false);

            // Set up action handlers
            playPauseButtons.SetActions(
                // Play action
                async () => {
                    _isRunning = true;
                    _isPaused = false;

                    // Update button states
                    playPauseButtons.SetPlayEnabled(false);
                    playPauseButtons.SetPauseEnabled(true);

                    try
                    {
                        // Auto-execute steps until completion or pause
                        while (_isRunning && !_isPaused && _currentStepId != null)
                        {
                            var step = _workflowSteps[_currentStepId];
                            UpdateStepStatusDisplay(step, "Running automatically...");

                            // Execute current step
                            bool success = await step.ExecuteWithStatus();

                            // Update result block
                            if (_resultBlocks.TryGetValue(_currentStepId, out var resultBlock))
                            {
                                resultBlock.Text = GetStatusText(step);

                                // Apply color based on status
                                if (success)
                                {
                                    resultBlock.Foreground = Brushes.Green;
                                }
                                else
                                {
                                    resultBlock.Foreground = Brushes.Red;
                                }
                            }

                            // Update timestamp block
                            if (_timestampBlocks.TryGetValue(_currentStepId, out var timestampBlock))
                            {
                                timestampBlock.Text = GetTimestampText(step);
                            }

                            if (success)
                            {
                                // If user confirmation is required, break automatic execution
                                if (step.RequiresConfirmation)
                                {
                                    UpdateStepStatusDisplay(step, "Waiting for confirmation...");

                                    // Update the result text block for waiting status
                                    if (_resultBlocks.TryGetValue(_currentStepId, out var resultBlockOut))
                                    {
                                        resultBlockOut.Text = "⏸ Waiting for confirmation";
                                        resultBlockOut.Foreground = Brushes.DarkOrange;
                                    }

                                    break;
                                }

                                // Move to next step
                                _currentStepId = step.NextStepId;
                                UpdateWorkflowUI();

                                // Small delay between steps for UI update
                                await Task.Delay(500);
                            }
                            else
                            {
                                // Handle failure
                                _isPaused = true;
                                UpdateStepStatusDisplay(step, $"Failed: {step.StatusMessage}");
                                break;
                            }
                        }
                    }
                    finally
                    {
                        // Reset button states when done
                        _isRunning = false;
                        playPauseButtons.SetPlayEnabled(true);
                        playPauseButtons.SetPauseEnabled(false);
                    }
                },

                // Pause action
                () => {
                    _isPaused = true;
                    UpdateStepStatusDisplay(_workflowSteps[_currentStepId], "Execution paused");
                    playPauseButtons.SetPlayEnabled(true);
                    playPauseButtons.SetPauseEnabled(false);
                }
            );

            // Create a border to contain the play/pause buttons
            var container = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10)
            };
            container.Child = playPauseButtons;

            // Add to the top of the workflow panel
            _workflowButtonPanel.Children.Insert(0, container);
        }

        // Example operation implementations
        private async Task InitSystemAsync()
        {
            // Simulate initialization
            await Task.Delay(1500);

            // You could add some actual initialization logic here
            // For example:
            // _controller.Initialize();
            // _sensors.Calibrate();
        }

        private async Task ReadSensorDataAsync()
        {
            // Simulate reading data
            await Task.Delay(2000);

            // Example of actual implementation:
            // var sensorData = await _dataReader.ReadAllSensorsAsync();
            // _dataProcessor.ProcessSensorData(sensorData);
        }

        private async Task UserConfirmationAsync()
        {
            // This step doesn't actually need to do anything
            // The confirmation will be handled by the RequiresConfirmation flag
            await Task.CompletedTask;
        }

        private async Task CompleteProcessAsync()
        {
            // Finalize the process
            await Task.Delay(1000);

            // Example:
            // await _dataLogger.SaveSessionData();
            // _controller.ResetToIdle();
        }
    }
}