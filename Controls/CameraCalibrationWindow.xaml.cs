using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Interaction logic for CameraCalibrationWindow.xaml
    /// </summary>
    public partial class CameraCalibrationWindow : Window
    {
        private readonly CameraCalibrationManager _calibrationManager;
        private readonly ILogger _logger;
        private readonly CameraGantryController _gantryController;
        private CalibrationSettings _currentSettings;
        private bool _isCalibrating = false;

        /// <summary>
        /// Gets the resulting calibration settings after the dialog is closed
        /// </summary>
        public CalibrationSettings Result { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="gantryController">The gantry controller for testing movements</param>
        /// <param name="calibrationManager">The settings manager</param>
        /// <param name="logger">Logger instance</param>
        public CameraCalibrationWindow(
            CameraGantryController gantryController,
            CameraCalibrationManager calibrationManager = null,
            ILogger logger = null)
        {
            InitializeComponent();

            _gantryController = gantryController ?? throw new ArgumentNullException(nameof(gantryController));
            _calibrationManager = calibrationManager ?? new CameraCalibrationManager();
            _logger = logger?.ForContext<CameraCalibrationWindow>() ?? Log.ForContext<CameraCalibrationWindow>();

            // Load current settings
            LoadSettings();

            // Subscribe to gantry controller events
            _gantryController.MovementStarted += GantryController_MovementStarted;
            _gantryController.MovementCompleted += GantryController_MovementCompleted;

            // Set up validation rules for text input
            FactorXTextBox.PreviewTextInput += NumberValidationTextBox;
            FactorYTextBox.PreviewTextInput += NumberValidationTextBox;
            TestDistanceTextBox.PreviewTextInput += NumberValidationTextBox;
        }

        /// <summary>
        /// Loads the current calibration settings and updates the UI
        /// </summary>
        private async void LoadSettings()
        {
            try
            {
                _currentSettings = await _calibrationManager.LoadSettingsAsync();

                // Update the UI with current settings
                FactorXTextBox.Text = _currentSettings.PixelToMmFactorX.ToString("F6");
                FactorYTextBox.Text = _currentSettings.PixelToMmFactorY.ToString("F6");
                LastUpdatedTextBlock.Text = $"Last updated: {_currentSettings.LastUpdated}";

                // Default test distance to 1 mm
                TestDistanceTextBox.Text = "1";

                _logger.Debug("Loaded calibration settings into UI: X={XFactor}, Y={YFactor}",
                    _currentSettings.PixelToMmFactorX, _currentSettings.PixelToMmFactorY);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading calibration settings");
                MessageBox.Show($"Error loading calibration settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Validates that the text input is a valid decimal number
        /// </summary>
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Allow only digits, decimal point, and minus sign
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != '-')
                {
                    e.Handled = true;
                    return;
                }
            }

            // If it's a decimal point, make sure there's not already one
            if (e.Text == ".")
            {
                if (((TextBox)sender).Text.Contains("."))
                {
                    e.Handled = true;
                    return;
                }
            }

            // If it's a minus sign, make sure it's at the beginning and there's not already one
            if (e.Text == "-")
            {
                if (((TextBox)sender).Text.Contains("-") || ((TextBox)sender).SelectionStart != 0)
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Apply button click handler
        /// </summary>
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Parse input values
                if (!TryParseCalibrationFactors(out double factorX, out double factorY))
                {
                    return;
                }

                // Update the settings object
                _currentSettings.PixelToMmFactorX = factorX;
                _currentSettings.PixelToMmFactorY = factorY;
                _currentSettings.LastUpdated = DateTime.Now;

                // Save to file
                await _calibrationManager.SaveSettingsAsync(_currentSettings);

                // Update the gantry controller
                _gantryController.SetCalibrationFactors(factorX, factorY);

                // Update UI
                LastUpdatedTextBlock.Text = $"Last updated: {_currentSettings.LastUpdated}";

                StatusTextBlock.Text = "Calibration settings applied successfully";
                _logger.Information("Calibration settings applied: X={XFactor}, Y={YFactor}", factorX, factorY);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying calibration settings");
                MessageBox.Show($"Error applying calibration settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error applying settings";
            }
        }

        /// <summary>
        /// Tests the calibration by moving the gantry a small amount in X direction
        /// </summary>
        private async void TestXButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCalibrating)
            {
                StatusTextBlock.Text = "Calibration test already in progress";
                return;
            }

            try
            {
                // Parse the test distance
                if (!double.TryParse(TestDistanceTextBox.Text, out double testDistance))
                {
                    MessageBox.Show("Please enter a valid test distance", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _isCalibrating = true;
                StatusTextBlock.Text = "Testing X-axis movement...";
                TestXButton.IsEnabled = false;
                TestYButton.IsEnabled = false;

                // Apply current UI values first
                if (TryParseCalibrationFactors(out double factorX, out double factorY))
                {
                    _gantryController.SetCalibrationFactors(factorX, factorY);
                }

                // Create a test movement (X only)
                await MoveGantryForTest(testDistance, 0);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during X-axis calibration test");
                MessageBox.Show($"Error during calibration test: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error during X-axis test";
                ResetTestButtons();
            }
        }

        /// <summary>
        /// Tests the calibration by moving the gantry a small amount in Y direction
        /// </summary>
        private async void TestYButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCalibrating)
            {
                StatusTextBlock.Text = "Calibration test already in progress";
                return;
            }

            try
            {
                // Parse the test distance
                if (!double.TryParse(TestDistanceTextBox.Text, out double testDistance))
                {
                    MessageBox.Show("Please enter a valid test distance", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _isCalibrating = true;
                StatusTextBlock.Text = "Testing Y-axis movement...";
                TestXButton.IsEnabled = false;
                TestYButton.IsEnabled = false;

                // Apply current UI values first
                if (TryParseCalibrationFactors(out double factorX, out double factorY))
                {
                    _gantryController.SetCalibrationFactors(factorX, factorY);
                }

                // Create a test movement (Y only)
                await MoveGantryForTest(0, testDistance);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Y-axis calibration test");
                MessageBox.Show($"Error during calibration test: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Error during Y-axis test";
                ResetTestButtons();
            }
        }

        /// <summary>
        /// Helper method to move the gantry for testing
        /// </summary>
        private async Task MoveGantryForTest(double deltaXmm, double deltaYmm)
        {
            try
            {
                // Move the gantry by the specified amount
                double[] relativeMove = new double[6]; // X, Y, Z, U, V, W
                relativeMove[0] = deltaXmm;
                relativeMove[1] = deltaYmm;

                // Execute gantry movement
                bool result = await Task.Run(() => _gantryController.ExecuteTestMovementAsync(relativeMove));

                if (!result)
                {
                    StatusTextBlock.Text = "Test movement failed";
                    _logger.Warning("Test movement failed");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing test movement");
                throw;
            }
        }

        /// <summary>
        /// Event handler for gantry movement started
        /// </summary>
        private void GantryController_MovementStarted(object sender, MovementStartedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = $"Moving gantry: X={e.DeltaXmm:F3}mm, Y={e.DeltaYmm:F3}mm";
            });
        }

        /// <summary>
        /// Event handler for gantry movement completed
        /// </summary>
        private void GantryController_MovementCompleted(object sender, MovementCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Success)
                {
                    StatusTextBlock.Text = "Test movement completed successfully";
                }
                else
                {
                    StatusTextBlock.Text = $"Test movement failed: {e.ErrorMessage}";
                }

                ResetTestButtons();
            });
        }

        /// <summary>
        /// Resets the test button state
        /// </summary>
        private void ResetTestButtons()
        {
            TestXButton.IsEnabled = true;
            TestYButton.IsEnabled = true;
            _isCalibrating = false;
        }

        /// <summary>
        /// Tries to parse the calibration factors from the text boxes
        /// </summary>
        private bool TryParseCalibrationFactors(out double factorX, out double factorY)
        {
            factorX = 0;
            factorY = 0;

            if (!double.TryParse(FactorXTextBox.Text, out factorX) || factorX <= 0)
            {
                MessageBox.Show("Please enter a valid positive value for X factor", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!double.TryParse(FactorYTextBox.Text, out factorY) || factorY <= 0)
            {
                MessageBox.Show("Please enter a valid positive value for Y factor", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// OK button click handler
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Save the current settings
            if (TryParseCalibrationFactors(out double factorX, out double factorY))
            {
                _currentSettings.PixelToMmFactorX = factorX;
                _currentSettings.PixelToMmFactorY = factorY;
                _currentSettings.LastUpdated = DateTime.Now;

                // Save to file
                try
                {
                    _calibrationManager.SaveSettings(_currentSettings);

                    // Update the gantry controller
                    _gantryController.SetCalibrationFactors(factorX, factorY);

                    Result = _currentSettings;
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error saving settings on OK");
                    MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Cancel button click handler
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Window closing event handler
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            // Unsubscribe from events
            _gantryController.MovementStarted -= GantryController_MovementStarted;
            _gantryController.MovementCompleted -= GantryController_MovementCompleted;
        }
    }
}