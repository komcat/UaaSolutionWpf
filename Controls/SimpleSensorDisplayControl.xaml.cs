using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using Serilog;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf.Controls
{
    public partial class SimpleSensorDisplayControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private readonly DispatcherTimer _updateTimer;
        private ILogger _logger;
        private RealTimeDataManager _realTimeDataManager;
        private bool _disposed;
        private string _selectedChannel;
        private double _currentValue;
        private string _unit;
        private DateTime _lastUpdateTime;
        private bool _isConnected;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<string> Channels { get; } = new ObservableCollection<string>();

        public string SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (_selectedChannel != value)
                {
                    _selectedChannel = value;
                    OnPropertyChanged();
                    UpdateDisplayedValue();
                }
            }
        }

        public string DisplayValue
        {
            get => FormatValueWithUnit(_currentValue, _unit);
        }

        public string LastUpdateDisplay
        {
            get
            {
                var timeDiff = DateTime.Now - _lastUpdateTime;
                if (timeDiff.TotalMinutes < 1) return "Just now";
                if (timeDiff.TotalHours < 1) return $"{(int)timeDiff.TotalMinutes}m ago";
                return $"{(int)timeDiff.TotalHours}h ago";
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

        public SimpleSensorDisplayControl()
        {
            InitializeComponent();
            DataContext = this;
            _lastUpdateTime = DateTime.Now;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += (s, e) => OnPropertyChanged(nameof(LastUpdateDisplay));
            _updateTimer.Start();
        }

        public void Initialize(RealTimeDataManager realTimeDataManager, ILogger logger)
        {
            _realTimeDataManager = realTimeDataManager;
            _logger = logger;
            LoadChannels();
            _realTimeDataManager.Data.PropertyChanged += OnDataUpdated;
        }

        private void LoadChannels()
        {
            Channels.Clear();
            foreach (var channel in _realTimeDataManager.GetConfiguredChannels())
            {
                if (!string.IsNullOrEmpty(channel) && channel.ToLower() != "reserved")
                {
                    Channels.Add(channel);
                }
            }

            if (Channels.Count > 0)
            {
                SelectedChannel = Channels[0];
            }
        }

        private void OnDataUpdated(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName.StartsWith("Measurement_"))
            {
                string channelName = e.PropertyName.Substring("Measurement_".Length);
                if (channelName == SelectedChannel)
                {
                    UpdateDisplayedValue();
                }
            }
        }

        private void UpdateDisplayedValue()
        {
            if (string.IsNullOrEmpty(SelectedChannel)) return;

            if (_realTimeDataManager.TryGetChannelValue(SelectedChannel, out var measurement))
            {
                _currentValue = measurement.Value;
                _unit = measurement.Unit;
                _lastUpdateTime = DateTime.Now;
                IsConnected = measurement.IsValid;
                OnPropertyChanged(nameof(DisplayValue));
            }
        }

        private string FormatValueWithUnit(double value, string unit)
        {
            if (string.IsNullOrEmpty(unit)) return $"{value:F2}";

            return unit.ToUpper() switch
            {
                "A" => FormatWithPrefix(value, "A"),
                "V" => FormatWithPrefix(value, "V"),
                "W" => FormatWithPrefix(value, "W"),
                _ => $"{value:F2} {unit}"
            };
        }

        private string FormatWithPrefix(double value, string unit)
        {
            var absValue = Math.Abs(value);
            if (absValue >= 1) return $"{value:F2} {unit}";
            if (absValue >= 0.001) return $"{value * 1000:F2} m{unit}";
            if (absValue >= 0.000001) return $"{value * 1000000:F2} µ{unit}";
            return $"{value * 1000000000:F2} n{unit}";
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
                if (_realTimeDataManager?.Data != null)
                {
                    _realTimeDataManager.Data.PropertyChanged -= OnDataUpdated;
                }
                _disposed = true;
            }
        }
    }
}