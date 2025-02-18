using System;
using System.Windows;
using System.Windows.Controls;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    public partial class BufferControl : UserControl, IDisposable
    {
        private ILogger? _logger;
        private Gantry.AcsGantryConnectionManager? _gantryManager;
        private const int BUFFER_NUMBER = 2;

        // Add parameterless constructor for XAML
        public BufferControl()
        {
            InitializeComponent();
        }

        // Public method to initialize the control after construction
        public void Initialize(Gantry.AcsGantryConnectionManager gantryManager, ILogger logger)
        {
            _gantryManager = gantryManager ?? throw new ArgumentNullException(nameof(gantryManager));
            _logger = logger?.ForContext<BufferControl>() ?? throw new ArgumentNullException(nameof(logger));

            _gantryManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            UpdateButtonStates(_gantryManager.IsConnected);
        }

        private void UpdateButtonStates(bool isConnected)
        {
            if (btnRunBuffer != null && btnStopBuffer != null)
            {
                btnRunBuffer.IsEnabled = isConnected;
                btnStopBuffer.IsEnabled = isConnected;
            }
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            Dispatcher.Invoke(() => UpdateButtonStates(isConnected));
        }

        private async void BtnRunBuffer_Click(object sender, RoutedEventArgs e)
        {
            if (_gantryManager == null || _logger == null) return;

            try
            {
                btnRunBuffer.IsEnabled = false;
                await _gantryManager.RunBufferAsync(BUFFER_NUMBER);
                _logger.Information("Successfully started Buffer {BufferNumber}", BUFFER_NUMBER);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error running Buffer {BufferNumber}", BUFFER_NUMBER);
                MessageBox.Show($"Error running buffer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRunBuffer.IsEnabled = true;
            }
        }

        private async void BtnStopBuffer_Click(object sender, RoutedEventArgs e)
        {
            if (_gantryManager == null || _logger == null) return;

            try
            {
                btnStopBuffer.IsEnabled = false;
                await _gantryManager.StopBufferAsync(BUFFER_NUMBER);
                _logger.Information("Successfully stopped Buffer {BufferNumber}", BUFFER_NUMBER);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping Buffer {BufferNumber}", BUFFER_NUMBER);
                MessageBox.Show($"Error stopping buffer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnStopBuffer.IsEnabled = true;
            }
        }

        public void Dispose()
        {
            if (_gantryManager != null)
            {
                _gantryManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }
            GC.SuppressFinalize(this);
        }
    }
}