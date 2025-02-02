using Newtonsoft.Json;
using Serilog;
using System.Net;
using UaaSolutionWpf.IO;
using System.IO;

namespace UaaSolutionWpf.Services
{
    public class IOConfig
    {
        public class IOPin
        {
            public int Pin { get; set; }
            public string Name { get; set; }
        }

        public class IODeviceConfig
        {
            public List<IOPin> Outputs { get; set; } = new List<IOPin>();
            public List<IOPin> Inputs { get; set; } = new List<IOPin>();
        }

        public class EziioDevice
        {
            public int DeviceId { get; set; }
            public string Name { get; set; }
            public string IP { get; set; }
            public IODeviceConfig IOConfig { get; set; }
        }

        public class IOConfigRoot
        {
            public Dictionary<string, string> Metadata { get; set; }
            public List<EziioDevice> Eziio { get; set; }
        }
    }

    public class IOService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, EziioClass> _devices = new Dictionary<string, EziioClass>();
        private readonly string _configPath;
        private IOConfig.IOConfigRoot _config;

        public IOService(ILogger logger, string configPath)
        {
            _logger = logger.ForContext<IOService>();
            _configPath = configPath;
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Load configuration
                string jsonContent = await File.ReadAllTextAsync(_configPath);
                _config = JsonConvert.DeserializeObject<IOConfig.IOConfigRoot>(jsonContent);

                if (_config?.Eziio == null)
                {
                    throw new Exception("Invalid IO configuration");
                }

                // Initialize each device
                foreach (var deviceConfig in _config.Eziio)
                {
                    var ipParts = deviceConfig.IP.Split('.');
                    if (ipParts.Length != 4)
                    {
                        _logger.Error("Invalid IP address format for device {DeviceName}: {IP}",
                            deviceConfig.Name, deviceConfig.IP);
                        continue;
                    }

                    var eziioConfig = new EziioConfiguration
                    {
                        IpA = int.Parse(ipParts[0]),
                        IpB = int.Parse(ipParts[1]),
                        IpC = int.Parse(ipParts[2]),
                        IpD = int.Parse(ipParts[3])
                    };

                    var device = new EziioClass(_logger, eziioConfig);

                    // Create pin mapping
                    var pinMapping = new Dictionary<string, int>();
                    foreach (var output in deviceConfig.IOConfig.Outputs)
                    {
                        pinMapping[output.Name] = output.Pin;
                    }

                    // Connect to device
                    bool connected = await device.ConnectAsync(
                        ConnectionType.TCP,
                        deviceConfig.DeviceId,
                        eziioConfig.IpA,
                        eziioConfig.IpB,
                        eziioConfig.IpC,
                        eziioConfig.IpD
                    );

                    if (connected)
                    {
                        _devices[deviceConfig.Name] = device;
                        _logger.Information("Successfully initialized IO device: {DeviceName}", deviceConfig.Name);
                    }
                    else
                    {
                        _logger.Error("Failed to connect to IO device: {DeviceName}", deviceConfig.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing IO service");
                throw;
            }
        }

        public bool SetOutput(string deviceName, string pinName, bool state)
        {
            try
            {
                if (!_devices.TryGetValue(deviceName, out var device))
                {
                    _logger.Error("Device not found: {DeviceName}", deviceName);
                    return false;
                }

                var deviceConfig = _config.Eziio.First(d => d.Name == deviceName);
                var pin = deviceConfig.IOConfig.Outputs.FirstOrDefault(p => p.Name == pinName);

                if (pin == null)
                {
                    _logger.Error("Pin not found: {PinName} on device {DeviceName}", pinName, deviceName);
                    return false;
                }

                return state ?
                    device.SetOutput(deviceConfig.DeviceId, pin.Pin) :
                    device.ClearOutput(deviceConfig.DeviceId, pin.Pin);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting output {PinName} on device {DeviceName}", pinName, deviceName);
                return false;
            }
        }

        public bool GetInput(string deviceName, string pinName)
        {
            // TODO: Implement input reading
            throw new NotImplementedException("Input reading not yet implemented");
        }

        public void Dispose()
        {
            foreach (var device in _devices.Values)
            {
                try
                {
                    device.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error disposing IO device");
                }
            }
            _devices.Clear();
        }
    }
}