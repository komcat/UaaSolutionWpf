using Serilog;
using System;
using System.Windows;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Windows
{
    /// <summary>
    /// Interaction logic for ConversionSettingsWindow.xaml
    /// </summary>
    public partial class ConversionSettingsWindow : Window
    {
        private readonly ILogger _logger;
        private readonly CameraConversionSettings _originalSettings;
        private readonly CameraSettingsManager _settingsManager;

        public CameraConversionSettings Result { get; private set; }

        public ConversionSettingsWindow(CameraConversionSettings settings, CameraSettingsManager settingsManager, ILogger logger)
        {
            InitializeComponent();

            _logger = logger?.ForContext<ConversionSettingsWindow>() ?? throw new ArgumentNullException(nameof(logger));
            _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));

            // Load current values
            txtXFactor.Text = _originalSettings.PixelToMillimeterFactorX.ToString("F5");
            txtYFactor.Text = _originalSettings.PixelToMillimeterFactorY.ToString("F5");

            // Set focus to first field
            Loaded += (s, e) => txtXFactor.Focus();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate input
                if (!TryParseDouble(txtXFactor.Text, out double xFactor) || xFactor <= 0)
                {
                    ShowError("Please enter a valid positive number for X Factor.");
                    txtXFactor.Focus();
                    return;
                }

                if (!TryParseDouble(txtYFactor.Text, out double yFactor) || yFactor <= 0)
                {
                    ShowError("Please enter a valid positive number for Y Factor.");
                    txtYFactor.Focus();
                    return;
                }

                // Create result
                Result = new CameraConversionSettings
                {
                    PixelToMillimeterFactorX = xFactor,
                    PixelToMillimeterFactorY = yFactor
                };

                // Save to settings file
                _settingsManager.SaveSettings(Result);
                _logger.Information("Camera conversion settings updated: X={XFactor}, Y={YFactor}",
                    xFactor, yFactor);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving camera conversion settings");
                ShowError($"Error saving settings: {ex.Message}");
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool TryParseDouble(string text, out double result)
        {
            // Try parse with different cultures (handles both dot and comma as decimal separator)
            return double.TryParse(text.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out result);
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}