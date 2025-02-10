using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using System.Windows.Threading;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Controls
{
    public partial class TECControllerV2 : UserControl, INotifyPropertyChanged, IDisposable
    {
        private ILogger _logger;
        private CLD101xGpibService _gpibService;
        private DispatcherTimer _readTimer;
        private bool _isConnected;
        private double _currentSetpoint = 0.150; // Default to 150mA
        private double _temperatureSetpoint = 25.0; // Default to 25°C
        private double _currentReading;
        private double _temperatureReading;
        private bool _disposed;
        private bool _isReading; // Flag to prevent overlapping reads

        public event PropertyChangedEventHandler PropertyChanged;

        public double CurrentSetpoint
        {
            get => _currentSetpoint;
            set
            {
                if (_currentSetpoint != value)
                {
                    _currentSetpoint = value;
                    OnPropertyChanged();
                }
            }
        }

        public double TemperatureSetpoint
        {
            get => _temperatureSetpoint;
            set
            {
                if (_temperatureSetpoint != value)
                {
                    _temperatureSetpoint = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CurrentReading
        {
            get => _currentReading;
            private set
            {
                if (_currentReading != value)
                {
                    _currentReading = value;
                    OnPropertyChanged();
                }
            }
        }

        public double TemperatureReading
        {
            get => _temperatureReading;
            private set
            {
                if (_temperatureReading != value)
                {
                    _temperatureReading = value;
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
                    Dispatcher.InvokeAsync(() =>
                    {
                        ConnectButton.Content = value ? "Disconnect" : "Connect";
                        ConnectButton.IsEnabled = true;
                    });
                }
            }
        }

        // Other properties remain the same...
        // Add Dispatcher.InvokeAsync for UI updates in setters if needed

        public TECControllerV2()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger.ForContext<TECControllerV2>();
        }

        public void InitializeServices()
        {
            _gpibService = new CLD101xGpibService(_logger);

            // Use Dispatcher for measurement updates
            _gpibService.CurrentMeasurementReceived += (s, value) =>
                Dispatcher.InvokeAsync(() => CurrentReading = value);
            _gpibService.TemperatureMeasurementReceived += (s, value) =>
                Dispatcher.InvokeAsync(() => TemperatureReading = value);
            _gpibService.ErrorOccurred += OnGpibError;

            _readTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _readTimer.Tick += ReadTimer_Tick;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsConnected)
            {
                ConnectButton.IsEnabled = false;
                try
                {
                    await Task.Run(async () => await _gpibService.ConnectAsync());
                    IsConnected = true;
                    _readTimer.Start();
                    _logger.Information("Connected to CLD101x");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to connect to CLD101x");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Connection failed: {ex.Message}", "Connection Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        ConnectButton.IsEnabled = true;
                    });
                }
            }
            else
            {
                await DisconnectAsync();
            }
        }

        private async Task DisconnectAsync()
        {
            ConnectButton.IsEnabled = false;
            _readTimer.Stop();

            try
            {
                await Task.Run(() => _gpibService.Disconnect());
                IsConnected = false;
                _logger.Information("Disconnected from CLD101x");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during disconnect");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error during disconnect: {ex.Message}", "Disconnect Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private async void ReadTimer_Tick(object sender, EventArgs e)
        {
            if (_isReading || !IsConnected) return;
            _isReading = true;

            try
            {
                int retryCount = 0;
                const int maxRetries = 3;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        await Task.Run(async () =>
                        {
                            await _gpibService.ReadLaserCurrentAsync();
                            await Task.Delay(100); // Small delay between reads
                            await _gpibService.ReadTecTemperatureAsync();
                        });
                        break; // Success, exit retry loop
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.Warning(ex, "Read attempt {RetryCount} failed", retryCount);

                        if (retryCount >= maxRetries)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                HandleError("Error reading measurements after multiple attempts", ex);
                                _readTimer.Stop();
                            });
                            await DisconnectAsync();
                            break;
                        }

                        await Task.Delay(100 * retryCount); // Exponential backoff
                    }
                }
            }
            finally
            {
                _isReading = false;
            }
        }
        private async Task SafeExecuteAsync(Func<Task> action, string errorContext)
        {
            if (_gpibService == null)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Service not initialized.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error));
                return;
            }

            if (!IsConnected)
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("Please connect to the device first.", "Not Connected",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            try
            {
                await Task.Run(action);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{ErrorContext} failed", errorContext);
                await Dispatcher.InvokeAsync(() => HandleError(errorContext, ex));
            }
        }

        private bool ValidateCurrentSetpoint()
        {
            const double minCurrent = 0.0;
            const double maxCurrent = 0.5; // 500mA max

            if (CurrentSetpoint < minCurrent || CurrentSetpoint > maxCurrent)
            {
                MessageBox.Show($"Current must be between {minCurrent}A and {maxCurrent}A",
                    "Invalid Current", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private bool ValidateTemperatureSetpoint()
        {
            const double minTemp = 10.0;
            const double maxTemp = 40.0;

            if (TemperatureSetpoint < minTemp || TemperatureSetpoint > maxTemp)
            {
                MessageBox.Show($"Temperature must be between {minTemp}°C and {maxTemp}°C",
                    "Invalid Temperature", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }
        private async void LowCurrent_Click(object sender, RoutedEventArgs e)
        {
            await SafeExecuteAsync(async () =>
            {
                await _gpibService.SetLaserCurrent(0.150);
                await Dispatcher.InvokeAsync(() => CurrentSetpoint = 0.150);
                await _gpibService.LaserOn();
            }, "Error setting low current");
        }

        private async void HighCurrent_Click(object sender, RoutedEventArgs e)
        {
            await SafeExecuteAsync(async () =>
            {
                await _gpibService.SetLaserCurrent(0.250);
                await Dispatcher.InvokeAsync(() => CurrentSetpoint = 0.250);
                await _gpibService.LaserOn();
            }, "Error setting high current");
        }

        private async void SetCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateCurrentSetpoint()) return;

            await SafeExecuteAsync(async () =>
            {
                await _gpibService.SetLaserCurrent(CurrentSetpoint);
                _logger.Information("Set laser current to {Current}A", CurrentSetpoint);
            }, "Error setting current");
        }

        private async void SetTemperature_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateTemperatureSetpoint()) return;

            await SafeExecuteAsync(async () =>
            {
                await _gpibService.SetTecTemperature(TemperatureSetpoint);
                await _gpibService.TecOn();
                _logger.Information("Set TEC temperature to {Temperature}°C", TemperatureSetpoint);
            }, "Error setting temperature");
        }
        private async void LaserOff_Click(object sender, RoutedEventArgs e)
        {
            await SafeExecuteAsync(async () =>
            {
                await _gpibService.LaserOff();
            }, "Error turning laser off");
        }

        private async void TecOff_Click(object sender, RoutedEventArgs e)
        {
            await SafeExecuteAsync(async () =>
            {
                await _gpibService.TecOff();
            }, "Error turning TEC off");
        }

        private void OnGpibError(object sender, Exception e)
        {
            Dispatcher.InvokeAsync(() => HandleError("GPIB Communication Error", e));
        }

        private void HandleError(string context, Exception ex)
        {
            _logger.Error(ex, context);
            MessageBox.Show($"{context}: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
                    _readTimer?.Stop();
                    _gpibService?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}