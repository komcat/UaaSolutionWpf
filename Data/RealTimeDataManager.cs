using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.IO;
using System.Threading;
using Serilog;

namespace UaaSolutionWpf.Data
{
    public class MeasurementValue
    {
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
        public string Unit { get; set; }
        public bool IsValid { get; set; }
    }

    public class RealTimeData : INotifyPropertyChanged
    {
        // Use ConcurrentDictionary for thread-safe dictionary operations
        private ConcurrentDictionary<string, MeasurementValue> _measurements = new();
        public IReadOnlyDictionary<string, MeasurementValue> Measurements => _measurements;

        // Use a synchronization context to make property changed events thread-safe
        private SynchronizationContext _synchronizationContext;

        public RealTimeData()
        {
            // Capture the synchronization context of the thread creating this instance
            _synchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            // Ensure property changed event is raised on the original synchronization context
            _synchronizationContext.Post(_ =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }, null);
        }

        public void UpdateMeasurement(string channel, double value, string unit = null)
        {
            // Use GetOrAdd to ensure thread-safe insertion
            var measurement = _measurements.AddOrUpdate(channel,
                // Add new entry if not exists
                _ => new MeasurementValue
                {
                    Value = value,
                    Timestamp = DateTime.Now,
                    Unit = unit,
                    IsValid = true
                },
                // Update existing entry
                (_, existing) =>
                {
                    existing.Value = value;
                    existing.Timestamp = DateTime.Now;
                    existing.Unit = unit;
                    existing.IsValid = true;
                    return existing;
                });

            OnPropertyChanged($"Measurement_{channel}");
        }

        public bool TryGetMeasurement(string channel, out MeasurementValue measurement)
        {
            return _measurements.TryGetValue(channel, out measurement);
        }

        public void InvalidateChannel(string channel)
        {
            if (_measurements.TryGetValue(channel, out var measurement))
            {
                var updatedMeasurement = new MeasurementValue
                {
                    Value = measurement.Value,
                    Timestamp = measurement.Timestamp,
                    Unit = measurement.Unit,
                    IsValid = false
                };
                _measurements.TryUpdate(channel, updatedMeasurement, measurement);
                OnPropertyChanged($"Measurement_{channel}");
            }
        }
    }

    public class RealTimeDataManager
    {
        // Use ReaderWriterLockSlim for more efficient synchronization of configuration
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;
        private readonly RealTimeData _data;
        private readonly ConcurrentDictionary<string, ChannelConfig> _channelConfigs;

        public RealTimeData Data => _data;

        public class ChannelConfig
        {
            public string ChannelName { get; set; }
            public int Id { get; set; }
            public double Value { get; set; }
            public string Unit { get; set; }
            public double Target { get; set; }
        }

        public class RealTimeDataConfig
        {
            public List<ChannelConfig> Channels { get; set; }
            public Dictionary<string, object> GlobalSettings { get; set; }
        }

        public RealTimeDataManager(string configPath, ILogger logger)
        {
            _logger = logger.ForContext<RealTimeDataManager>();
            _data = new RealTimeData();
            _channelConfigs = new ConcurrentDictionary<string, ChannelConfig>();

            LoadConfiguration(configPath);
        }
        /// <summary>
        /// Checks if a channel exists in the configuration
        /// </summary>
        /// <param name="channelName">The name of the channel to check</param>
        /// <returns>True if the channel exists, false otherwise</returns>
        public bool ChannelExists(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
            {
                return false;
            }

            _configLock.EnterReadLock();
            try
            {
                return _channelConfigs.ContainsKey(channelName);
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }
        private void LoadConfiguration(string configPath)
        {
            try
            {
                string jsonContent = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<RealTimeDataConfig>(jsonContent);

                // Use write lock to safely populate the configuration
                _configLock.EnterWriteLock();
                try
                {
                    foreach (var channel in config.Channels)
                    {
                        _channelConfigs[channel.ChannelName] = channel;
                        _logger.Information("Loaded configuration for channel {ChannelName}", channel.ChannelName);
                    }
                }
                finally
                {
                    _configLock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load real-time data configuration from {ConfigPath}", configPath);
                throw;
            }
        }

        public void UpdateChannelValue(string channelName, double value)
        {
            try
            {
                // Use a read lock to safely access configuration
                _configLock.EnterReadLock();
                ChannelConfig config;
                try
                {
                    if (!_channelConfigs.TryGetValue(channelName, out config))
                    {
                        _logger.Warning("Attempted to update unconfigured channel {ChannelName}", channelName);
                        return;
                    }
                }
                finally
                {
                    _configLock.ExitReadLock();
                }

                // Validate the value
                if (IsValueValid(config, value))
                {
                    _data.UpdateMeasurement(channelName, value, config.Unit);
                    //_logger.Debug("Updated {ChannelName} with value {Value} {Unit}",
                    //    channelName, value, config.Unit);
                }
                else
                {
                    _logger.Warning("Value {Value} is out of valid range for channel {ChannelName}",
                        value, channelName);
                    // Optionally: Invalidate the channel or handle out-of-range values
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating channel {ChannelName}", channelName);
            }
        }

        private bool IsValueValid(ChannelConfig config, double value)
        {
            // Example validation logic
            // You might want to add more sophisticated validation based on your requirements
            return value >= -config.Target * 100 && value <= config.Target * 100; // Simple example
        }

        public bool TryGetChannelValue(string channelName, out MeasurementValue value)
        {
            return _data.TryGetMeasurement(channelName, out value);
        }

        public ChannelConfig GetChannelConfig(string channelName)
        {
            _configLock.EnterReadLock();
            try
            {
                return _channelConfigs.TryGetValue(channelName, out var config) ? config : null;
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }

        public IEnumerable<string> GetConfiguredChannels()
        {
            _configLock.EnterReadLock();
            try
            {
                return _channelConfigs.Keys.ToList();
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }
    }
}