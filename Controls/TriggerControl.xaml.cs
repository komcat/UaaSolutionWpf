using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.Controls
{
    public partial class TriggerControl : UserControl
    {
        private IOManager _ioManager;
        private string _deviceName;
        private string _pinName;
        private bool _isTriggering;

        public TriggerControl()
        {
            InitializeComponent();
        }

        public void Configure(IOManager ioManager, string deviceName, string pinName, string triggerName = "Trigger")
        {
            _ioManager = ioManager;
            _deviceName = deviceName;
            _pinName = pinName;
            TriggerName.Text = triggerName;
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

                // Wait 100ms
                await Task.Delay(100);

                // Set output on
                await Task.Run(() => _ioManager.SetOutput(_deviceName, _pinName));

                // Hold for 100ms
                await Task.Delay(100);

                // Clear output
                await Task.Run(() => _ioManager.ClearOutput(_deviceName, _pinName));

                // Final hold for 100ms
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
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
    }
}