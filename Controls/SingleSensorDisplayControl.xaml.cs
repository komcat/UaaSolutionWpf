using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using Serilog;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf.Controls
{
    public partial class SingleSensorDisplayControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private ILogger _logger;
        private RealTimeDataManager _realTimeDataManager;
        private bool _disposed;
        private string _selectedChannel;
        private double _currentValue;
        private string _unit;
        private double _targetValue;
        private bool _hasTarget;

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
                    _currentValue = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayValue));
                }
            }
        }

        public string DisplayValue
        {
            get
            {
                // Helper function to format with SI prefix
                string FormatWithPrefix(double value)
                {
                    if (Math.Abs(value) >= 1e-3)
                        return $"{value * 1e3:F1} m{Unit}"; // milli
                    else if (Math.Abs(value) >= 1e-6)
                        return $"{value * 1e6:F1} µ{Unit}"; // micro
                    else if (Math.Abs(value) >= 1e-9)
                        return $"{value * 1e9:F1} n{Unit}"; // nano
                    else
                        return $"{value * 1e12:F1} p{Unit}"; // pico
                }

                // Format based on unit type
                switch (Unit?.ToUpper())
                {
                    case "A": // For current
                        return FormatWithPrefix(CurrentValue);
                    case "V": // For voltage
                        return FormatWithPrefix(CurrentValue);
                    case "W": // For power
                        return $"{CurrentValue * 1000:F1} mW"; // Always show in milliwatts
                    default:
                        return $"{CurrentValue:F1} {Unit}";
                }
            }
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

        public SingleSensorDisplayControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void Initialize(RealTimeDataManager realTimeDataManager, ILogger logger)
        {
            try
            {
                _realTimeDataManager = realTimeDataManager ?? throw new ArgumentNullException(nameof(realTimeDataManager));
                _logger = logger?.ForContext<SingleSensorDisplayControl>() ?? throw new ArgumentNullException(nameof(logger));

                _logger.Information("Initializing SingleSensorDisplayControl...");

                // Log the configuration we're working with
                var channelConfigs = _realTimeDataManager.GetConfiguredChannels();
                _logger.Information("Found {Count} configured channels", channelConfigs?.Count() ?? 0);

                foreach (var channel in channelConfigs ?? Enumerable.Empty<string>())
                {
                    var config = _realTimeDataManager.GetChannelConfig(channel);
                    _logger.Debug("Channel: {Name}, Unit: {Unit}, Target: {Target}",
                        channel,
                        config?.Unit ?? "none",
                        config?.Target ?? 0);
                }

                // Load channel names from RealTimeDataManager
                LoadChannels();

                // Subscribe to real-time data updates
                _realTimeDataManager.Data.PropertyChanged += OnDataUpdated;

                _logger.Information("SingleSensorDisplayControl initialized successfully with {Count} channels", Channels.Count);
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

                // Get all configured channels that are valid (not reserved)
                var configuredChannels = _realTimeDataManager.GetConfiguredChannels()
                    .Where(ch => !string.IsNullOrEmpty(ch) && ch.ToLower() != "reserved");

                foreach (var channel in configuredChannels)
                {
                    _logger.Debug("Adding channel to ComboBox: {Channel}", channel);
                    Channels.Add(channel);
                }

                // Select first channel if available
                if (Channels.Any())
                {
                    SelectedChannel = Channels[0];
                    _logger.Information("Selected initial channel: {Channel}", SelectedChannel);
                }
                else
                {
                    _logger.Warning("No channels available to display");
                }

                _logger.Information("Loaded {Count} channels into ComboBox", Channels.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading channels into ComboBox");
                throw;
            }
        }
        private void OnDataUpdated(object sender, PropertyChangedEventArgs e)
        {
            try
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
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling data update");
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