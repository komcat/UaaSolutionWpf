using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GPIBKeithleyCurrentMeasurement;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    public enum DisplayChannel
    {
        Channel1,
        Channel2
    }

    public partial class KeithleyCurrentControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private ILogger _logger;
        private GpibService _gpibService;
        private bool _isConnected;
        private double _currentValue;
        private bool _isMeasuring;
        private bool _disposed;
        private int count = 0;

        public event PropertyChangedEventHandler PropertyChanged;

        // Channel selection properties
        public DisplayChannel[] AvailableChannels { get; } =
            (DisplayChannel[])Enum.GetValues(typeof(DisplayChannel));

        private DisplayChannel _selectedChannel = DisplayChannel.Channel1;
        public DisplayChannel SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (_selectedChannel != value)
                {
                    _selectedChannel = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CurrentValue
        {
            get => _currentValue;
            private set
            {
                if (_currentValue != value)
                {
                    _currentValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public KeithleyCurrentControl()
        {
            InitializeComponent();
            Init();
        }
        public void SetLogger(ILogger logger)
        {
            _logger =logger.ForContext<KeithleyCurrentControl>();

        }

        public void Init()
        {
            DataContext = this;


            _logger = Log.ForContext<KeithleyCurrentControl>();
            _gpibService = new GpibService("GPIB0::1::INSTR");

            // Wire up GPIB service events
            _gpibService.MeasurementReceived += OnMeasurementReceived;
            _gpibService.ErrorOccurred += OnErrorOccurred;
            _logger.Information("KeithleyCurrentControl constructor executed");
        }
        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                try
                {
                    await _gpibService.ConnectAsync();
                    IsConnected = true;
                    ConnectButton.Content = "Disconnect";
                    _logger.Information("Successfully connected to Keithley");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to connect to Keithley");
                    MessageBox.Show($"Connection failed: {ex.Message}", "Connection Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                try
                {
                    _gpibService.Disconnect();
                    IsConnected = false;
                    ConnectButton.Content = "Connect";
                    StopButton.IsEnabled = false;
                    StartButton.IsEnabled = false;
                    _logger.Information("Disconnected from Keithley");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during disconnect");
                }
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMeasuring)
            {
                try
                {
                    StartButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    _isMeasuring = true;

                    // Start continuous measurement
                    _gpibService.StartContinuousReadAsync();
                    _logger.Information("Started continuous measurement");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error starting measurement");
                    MessageBox.Show($"Error starting measurement: {ex.Message}",
                        "Measurement Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    // Reset buttons
                    StartButton.IsEnabled = true;
                    StopButton.IsEnabled = false;
                    _isMeasuring = false;
                }
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _gpibService.StopMeasurement();
                _isMeasuring = false;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                _logger.Information("Stopped continuous measurement");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping measurement");
            }
        }

        private (double? channel1, double? channel2) ParseMeasurement(string measurement)
        {
            try
            {
                // Remove any newline characters and split by comma
                var values = measurement.Trim('\n', '\r').Split(',');
                if (values.Length != 2)
                {
                    _logger.Warning("Unexpected measurement format: {Measurement}", measurement);
                    return (null, null);
                }

                bool ch1Success = double.TryParse(values[0], out double ch1Value);
                bool ch2Success = double.TryParse(values[1], out double ch2Value);

                return (
                    ch1Success ? ch1Value : null,
                    ch2Success ? ch2Value : null
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error parsing measurement: {Measurement}", measurement);
                return (null, null);
            }
        }

        private void OnMeasurementReceived(object sender, string measurement)
        {
            try
            {
                count++;
                var (channel1, channel2) = ParseMeasurement(measurement);

                Dispatcher.Invoke(() =>
                {
                    //_logger.Debug($"Count data received : {count}, value: {channel1} / {channel2}");

                    switch (SelectedChannel)
                    {
                        case DisplayChannel.Channel1:
                            if (channel1.HasValue)
                                CurrentValue = channel1.Value;
                            else
                                _logger.Warning("Failed to parse Channel 1 value from: {Measurement}", measurement);
                            break;

                        case DisplayChannel.Channel2:
                            if (channel2.HasValue)
                                CurrentValue = channel2.Value;
                            else
                                _logger.Warning("Failed to parse Channel 2 value from: {Measurement}", measurement);
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing measurement");
            }
        }

        private void OnErrorOccurred(object sender, Exception e)
        {
            _logger.Error(e, "GPIB error occurred");

            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Measurement error: {e.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Reset state
                _isMeasuring = false;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            });
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop measurement if running
                    if (_isMeasuring)
                    {
                        _gpibService.StopMeasurement();
                    }

                    // Disconnect if connected
                    if (IsConnected)
                    {
                        _gpibService.Disconnect();
                    }

                    // Dispose GPIB service
                    _gpibService?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}