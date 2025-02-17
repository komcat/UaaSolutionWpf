using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.Services;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    public partial class TriggerControl : UserControl, IDisposable
    {
        private ILogger _logger;
        private PreciseTimer _timer;
        private IOManager _ioManager;
        private string _deviceName;
        private string _pinName;
        private bool _isTriggering;
        private bool _disposed;

        public TriggerControl()
        {
            InitializeComponent();
            
            
        }

        public void Configure(IOManager ioManager, ILogger logger,string deviceName, string pinName, string triggerName = "Trigger" )
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _deviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
            _pinName = pinName ?? throw new ArgumentNullException(nameof(pinName));
            TriggerName.Text = triggerName;
            _logger = logger.ForContext<TriggerControl>();
            _logger.Debug("Configured trigger control for {Device} pin {Pin}", deviceName, pinName);
            _timer = new PreciseTimer(_logger);
        }

        private async void TriggerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ioManager == null || _isTriggering) return;

            try
            {
                _isTriggering = true;
                TriggerButton.IsEnabled = false;
                TriggerButton.Background = Brushes.Gray;

                // Clear output first
                await Task.Run(() => _ioManager.ClearOutput(_deviceName, _pinName));
                _logger.Debug("Initial clear for {Device} pin {Pin}", _deviceName, _pinName);

                // Wait 100ms precisely
                await _timer.StartAsync(TimeSpan.FromMilliseconds(100));

                // Set output on
                await Task.Run(() => _ioManager.SetOutput(_deviceName, _pinName));
                _logger.Debug("Set output for {Device} pin {Pin}", _deviceName, _pinName);

                // Hold for 100ms precisely
                _logger.Debug("Timer starts 100ms..");
                await _timer.StartAsync(TimeSpan.FromMilliseconds(100));
                _logger.Debug("Timer stops 100ms..");
                // Clear output
                await Task.Run(() => _ioManager.ClearOutput(_deviceName, _pinName));
                _logger.Debug("Final clear for {Device} pin {Pin}", _deviceName, _pinName);

                // Final hold for 100ms precisely
                await _timer.StartAsync(TimeSpan.FromMilliseconds(100));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during trigger sequence for {Device} pin {Pin}", _deviceName, _pinName);
                MessageBox.Show($"Error during trigger sequence: {ex.Message}",
                    "Trigger Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isTriggering = false;
                TriggerButton.IsEnabled = true;
                TriggerButton.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // #007ACC
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _timer?.Dispose();
            }

            _disposed = true;
        }
    }
}