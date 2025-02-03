using Serilog;
using System.Collections.Concurrent;
using System.Net;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.IO
{
    public enum IOPinType
    {
        Input,
        Output
    }

    public class PinStatusInfo
    {
        public string DeviceName { get; set; }
        public string PinName { get; set; }
        public IOPinType PinType { get; set; }
        public bool State { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public long UpdateCount { get; set; }
    }

    /// <summary>
    /// Provides real-time monitoring of IO devices
    /// </summary>
    public class IOMonitor : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IOService _ioService;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PinStatusInfo>> _devicePinStatus;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly int _monitoringIntervalMs;
        private bool _isDisposed;

        public event EventHandler<PinStatusInfo> PinStateChanged;

        public IOMonitor(ILogger logger, IOService ioService, int monitoringIntervalMs = 100)
        {
            _logger = logger.ForContext<IOMonitor>();
            _ioService = ioService;
            _monitoringIntervalMs = monitoringIntervalMs;
            _devicePinStatus = new ConcurrentDictionary<string, ConcurrentDictionary<string, PinStatusInfo>>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Starts monitoring the IO devices
        /// </summary>
        public async Task StartMonitoringAsync()
        {
            try
            {
                _logger.Information("Starting IO monitoring");
                await MonitorIODevicesAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting IO monitoring");
                throw;
            }
        }

        /// <summary>
        /// Stops monitoring the IO devices
        /// </summary>
        public void StopMonitoring()
        {
            try
            {
                _logger.Information("Stopping IO monitoring");
                _cancellationTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping IO monitoring");
            }
        }

        /// <summary>
        /// Gets the current status of a specific pin
        /// </summary>
        public PinStatusInfo GetPinStatus(string deviceName, string pinName)
        {
            if (_devicePinStatus.TryGetValue(deviceName, out var devicePins))
            {
                if (devicePins.TryGetValue(pinName, out var pinStatus))
                {
                    return pinStatus;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets all pin statuses for a specific device
        /// </summary>
        public Dictionary<string, PinStatusInfo> GetDevicePinStatuses(string deviceName)
        {
            if (_devicePinStatus.TryGetValue(deviceName, out var devicePins))
            {
                return new Dictionary<string, PinStatusInfo>(devicePins);
            }
            return new Dictionary<string, PinStatusInfo>();
        }

        /// <summary>
        /// Gets all pin statuses for all devices
        /// </summary>
        public Dictionary<string, Dictionary<string, PinStatusInfo>> GetAllPinStatuses()
        {
            var result = new Dictionary<string, Dictionary<string, PinStatusInfo>>();
            foreach (var device in _devicePinStatus)
            {
                result[device.Key] = new Dictionary<string, PinStatusInfo>(device.Value);
            }
            return result;
        }

        private async Task MonitorIODevicesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var deviceName in _devicePinStatus.Keys)
                    {
                        var devicePins = _devicePinStatus[deviceName];
                        foreach (var pinName in devicePins.Keys)
                        {
                            if (devicePins.TryGetValue(pinName, out var pinStatus))
                            {
                                bool currentStatus = pinStatus.PinType == IOPinType.Input
                                    ? await Task.Run(() => _ioService.GetInput(deviceName, pinName))
                                    : await Task.Run(() => _ioService.GetOutput(deviceName, pinName));

                                UpdatePinStatus(deviceName, pinName, currentStatus);
                            }
                        }
                    }

                    await Task.Delay(_monitoringIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.Information("IO monitoring cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during IO monitoring");
                    await Task.Delay(1000, cancellationToken); // Wait before retrying
                }
            }
        }
        private void UpdatePinStatus(string deviceName, string pinName, bool currentState)
        {
            if (!_devicePinStatus.TryGetValue(deviceName, out var devicePins))
            {
                devicePins = new ConcurrentDictionary<string, PinStatusInfo>();
                _devicePinStatus[deviceName] = devicePins;
            }

            if (!devicePins.TryGetValue(pinName, out var pinStatus))
            {
                pinStatus = new PinStatusInfo
                {
                    DeviceName = deviceName,
                    PinName = pinName,
                    State = currentState,
                    LastUpdateTime = DateTime.UtcNow,
                    UpdateCount = 1
                };
                devicePins[pinName] = pinStatus;
                PinStateChanged?.Invoke(this, pinStatus);
            }
            else if (pinStatus.State != currentState)
            {
                pinStatus.State = currentState;
                pinStatus.LastUpdateTime = DateTime.UtcNow;
                pinStatus.UpdateCount++;
                PinStateChanged?.Invoke(this, pinStatus);
            }
        }

        /// <summary>
        /// Adds a pin to be monitored
        /// </summary>
        public void AddPinToMonitor(string deviceName, string pinName, IOPinType pinType)
        {
            var devicePins = _devicePinStatus.GetOrAdd(deviceName, _ => new ConcurrentDictionary<string, PinStatusInfo>());
            devicePins.GetOrAdd(pinName, _ => new PinStatusInfo
            {
                DeviceName = deviceName,
                PinName = pinName,
                PinType = pinType,
                State = false,
                LastUpdateTime = DateTime.UtcNow,
                UpdateCount = 0
            });
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                StopMonitoring();
                _cancellationTokenSource.Dispose();
                _isDisposed = true;
            }
        }

        ~IOMonitor()
        {
            Dispose();
        }
    }
}