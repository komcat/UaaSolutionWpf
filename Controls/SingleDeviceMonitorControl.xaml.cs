using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using Serilog;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Controls
{
    public partial class SingleDeviceMonitorControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private  ILogger _logger;
        private  DevicePositionMonitor _positionMonitor;
        private readonly DispatcherTimer _updateTimer;
        private  string _deviceId;
        private bool _disposed;

        private string _deviceName;
        public string DeviceName
        {
            get => _deviceName;
            private set
            {
                if (_deviceName != value)
                {
                    _deviceName = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _currentPosition = "Unknown";
        public string CurrentPosition
        {
            get => _currentPosition;
            private set
            {
                if (_currentPosition != value)
                {
                    _currentPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SingleDeviceMonitorControl()
        {
            InitializeComponent();
            DataContext = this;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += UpdateDevicePosition;
        }

        public void Initialize(string deviceId, string deviceName, DevicePositionMonitor positionMonitor, ILogger logger)
        {
            _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            DeviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
            _positionMonitor = positionMonitor ?? throw new ArgumentNullException(nameof(positionMonitor));
            _logger = logger?.ForContext<SingleDeviceMonitorControl>() ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                _logger.Information("Initializing position monitor for device {DeviceId}", deviceId);
                _updateTimer.Start();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize position monitor for device {DeviceId}", deviceId);
                throw;
            }
        }

        private async void UpdateDevicePosition(object sender, EventArgs e)
        {
            if (_disposed) return;

            try
            {
                var position = await _positionMonitor.GetCurrentPosition(_deviceId);
                CurrentPosition = position.Name ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating position for device {DeviceId}", _deviceId);
                CurrentPosition = "Error";
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateTimer?.Stop();
                _disposed = true;
            }
        }
    }
}