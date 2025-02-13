using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using Serilog;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf.Controls
{
    public partial class SingleSensorDisplayControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        public enum TrendDirection
        {
            Increasing,
            Decreasing,
            Stable
        }
        private readonly DispatcherTimer _updateTimer;
        private ILogger _logger;
        private RealTimeDataManager _realTimeDataManager;
        private bool _disposed;
        private string _selectedChannel;
        private double _currentValue;
        private string _unit;
        private double _targetValue;
        private bool _hasTarget;
        private DateTime _lastUpdateTime;
        private double _previousValue;
        private bool _isConnected;
        private double _percentageComplete;
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

        public double CurrentValue
        {
            get => _currentValue;
            private set
            {
                if (_currentValue != value)
                {
                    _previousValue = _currentValue;
                    _currentValue = value;

                    // Calculate trend
                    if (Math.Abs(_currentValue - _previousValue) < 0.000001)
                        CurrentTrend = TrendDirection.Stable;
                    else
                        CurrentTrend = _currentValue > _previousValue ? TrendDirection.Increasing : TrendDirection.Decreasing;

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayValue));
                    UpdateProgressPercentage();
                }
            }
        }
        public double PercentageComplete
        {
            get => _percentageComplete;
            private set
            {
                if (_percentageComplete != value)
                {
                    _percentageComplete = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayValue
        {
            get
            {
                return FormatValueWithUnit(CurrentValue, Unit);
            }
        }

        private string FormatValueWithUnit(double value, string unit)
        {
            var (scaledValue, prefix) = GetScaledValueAndPrefix(value);
            return unit?.ToUpper() switch
            {
                "A" or "V" or "W" => $"{scaledValue:F2} {prefix}{unit}",
                _ => $"{value:F2} {unit}"
            };
        }

        private (double value, string prefix) GetScaledValueAndPrefix(double value)
        {
            var absValue = Math.Abs(value);
            if (absValue >= 1000) return (value / 1000, "k");
            if (absValue >= 1) return (value, "");
            if (absValue >= 0.001) return (value * 1000, "m");
            if (absValue >= 0.000001) return (value * 1000000, "µ");
            if (absValue >= 0.000000001) return (value * 1000000000, "n");
            return (value * 1000000000000, "p");
        }

        public string Unit
        {
            get => _unit;
            private set
            {
                if (_unit != value)
                {
                    _unit = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayValue));
                }
            }
        }

        public double TargetValue
        {
            get => _targetValue;
            private set
            {
                if (_targetValue != value)
                {
                    _targetValue = value;
                    OnPropertyChanged();
                    UpdateProgressPercentage();
                }
            }
        }

        public bool HasTarget
        {
            get => _hasTarget;
            private set
            {
                if (_hasTarget != value)
                {
                    _hasTarget = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime LastUpdateTime
        {
            get => _lastUpdateTime;
            private set
            {
                if (_lastUpdateTime != value)
                {
                    _lastUpdateTime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LastUpdateDisplay));
                }
            }
        }

        public string LastUpdateDisplay
        {
            get
            {
                var timeDiff = DateTime.Now - LastUpdateTime;
                if (timeDiff.TotalMinutes < 1) return "Just now";
                if (timeDiff.TotalMinutes < 60) return $"{(int)timeDiff.TotalMinutes}m ago";
                if (timeDiff.TotalHours < 24) return $"{(int)timeDiff.TotalHours}h ago";
                return $"{(int)timeDiff.TotalDays}d ago";
            }
        }
        private TrendDirection _currentTrend = TrendDirection.Stable;

        public TrendDirection CurrentTrend
        {
            get => _currentTrend;
            private set
            {
                if (_currentTrend != value)
                {
                    _currentTrend = value;
                    OnPropertyChanged();
                }
            }
        }

        public SingleSensorDisplayControl()
        {
            InitializeComponent();
            DataContext = this;
            LastUpdateTime = DateTime.Now;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _updateTimer.Tick += (s, e) => OnPropertyChanged(nameof(LastUpdateDisplay));
            _updateTimer.Start();
        }

        public void Initialize(RealTimeDataManager realTimeDataManager, ILogger logger)
        {
            try
            {
                _realTimeDataManager = realTimeDataManager ?? throw new ArgumentNullException(nameof(realTimeDataManager));
                _logger = logger?.ForContext<SingleSensorDisplayControl>() ?? throw new ArgumentNullException(nameof(logger));

                _logger.Information("Initializing SingleSensorDisplayControl...");
                LoadChannels();
                _realTimeDataManager.Data.PropertyChanged += OnDataUpdated;

                _logger.Information("SingleSensorDisplayControl initialized with {Count} channels", Channels.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize SingleSensorDisplayControl");
                throw;
            }
        }

        private void LoadChannels()
        {
            try
            {
                Channels.Clear();
                var configuredChannels = _realTimeDataManager.GetConfiguredChannels()
                    .Where(ch => !string.IsNullOrEmpty(ch) && ch.ToLower() != "reserved");

                foreach (var channel in configuredChannels)
                {
                    Channels.Add(channel);
                }

                if (Channels.Any())
                {
                    SelectedChannel = Channels[0];
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading channels");
                throw;
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
            if (string.IsNullOrEmpty(SelectedChannel) || _realTimeDataManager == null)
                return;

            try
            {
                if (_realTimeDataManager.TryGetChannelValue(SelectedChannel, out var measurement))
                {
                    CurrentValue = measurement.Value;
                    Unit = measurement.Unit;
                    LastUpdateTime = DateTime.Now;

                    var config = _realTimeDataManager.GetChannelConfig(SelectedChannel);
                    if (config != null)
                    {
                        TargetValue = config.Target;
                        HasTarget = config.Target > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating displayed value for channel {Channel}", SelectedChannel);
            }
        }

        private void UpdateProgressPercentage()
        {
            if (HasTarget && TargetValue > 0)
            {
                PercentageComplete = Math.Min((CurrentValue / TargetValue) * 100, 100);
            }
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
                    _updateTimer.Stop();
                    if (_realTimeDataManager?.Data != null)
                    {
                        _realTimeDataManager.Data.PropertyChanged -= OnDataUpdated;
                    }
                }
                _disposed = true;
            }
        }
    }
}