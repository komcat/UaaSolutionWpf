using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using Serilog;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.Motion;

namespace UaaSolutionWpf.Controls
{
    public partial class DevicePositionMonitorControl : UserControl, INotifyPropertyChanged
    {
        private  ILogger _logger;
        private  DevicePositionMonitor _positionMonitor;
        private readonly DispatcherTimer _updateTimer;

        private string _leftHexapodPosition = "Unknown";
        private string _rightHexapodPosition = "Unknown";
        private string _bottomHexapodPosition = "Unknown";
        private string _gantryPosition = "Unknown";

        public string LeftHexapodPosition
        {
            get => _leftHexapodPosition;
            private set
            {
                if (_leftHexapodPosition != value)
                {
                    _leftHexapodPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RightHexapodPosition
        {
            get => _rightHexapodPosition;
            private set
            {
                if (_rightHexapodPosition != value)
                {
                    _rightHexapodPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BottomHexapodPosition
        {
            get => _bottomHexapodPosition;
            private set
            {
                if (_bottomHexapodPosition != value)
                {
                    _bottomHexapodPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GantryPosition
        {
            get => _gantryPosition;
            private set
            {
                if (_gantryPosition != value)
                {
                    _gantryPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public DevicePositionMonitorControl()
        {
            InitializeComponent();
            DataContext = this;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Update every 500ms
            };
            _updateTimer.Tick += UpdateDevicePositions;
        }

        public void Initialize(DevicePositionMonitor positionMonitor, ILogger logger)
        {
            _positionMonitor = positionMonitor ?? throw new ArgumentNullException(nameof(positionMonitor));
            _logger = logger?.ForContext<DevicePositionMonitorControl>() ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                _logger.Information("Initializing DevicePositionMonitorControl");
                _updateTimer.Start();
                _logger.Information("DevicePositionMonitorControl initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize DevicePositionMonitorControl");
                throw;
            }
        }

        private async void UpdateDevicePositions(object sender, EventArgs e)
        {
            try
            {
                // Get positions for each device
                var leftPosition = await _positionMonitor.GetCurrentPosition("hex-left");
                var rightPosition = await _positionMonitor.GetCurrentPosition("hex-right");
                var bottomPosition = await _positionMonitor.GetCurrentPosition("hex-bottom");
                var gantryPosition = await _positionMonitor.GetCurrentPosition("gantry-main");

                // Update properties
                LeftHexapodPosition = leftPosition.Name ?? "Unknown";
                RightHexapodPosition = rightPosition.Name ?? "Unknown";
                BottomHexapodPosition = bottomPosition.Name ?? "Unknown";
                GantryPosition = gantryPosition.Name ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating device positions");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _updateTimer?.Stop();
        }
    }
}