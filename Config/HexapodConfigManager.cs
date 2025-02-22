using Serilog;
using System.Text.Json;
using UaaSolutionWpf.Services;
using System.IO;

namespace UaaSolutionWpf.Config
{

    public class HexapodConfig
    {
        public Dictionary<string, HexapodDeviceConfig> Devices { get; set; } = new();
    }

    public class HexapodDeviceConfig
    {
        public bool IsEnabled { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Name { get; set; }
    }

    public class HexapodConfigManager
    {
        private readonly ILogger _logger;
        private HexapodConfig _config;
        private const string DEFAULT_CONFIG_PATH = "Config/hexapodConfig.json";

        public HexapodConfigManager(ILogger logger)
        {
            _logger = logger.ForContext<HexapodConfigManager>();
        }

        public void LoadConfiguration(string configPath = DEFAULT_CONFIG_PATH)
        {
            try
            {
                string jsonContent = File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<HexapodConfig>(jsonContent)
                    ?? throw new InvalidOperationException("Failed to deserialize config");

                _logger.Information("Loaded hexapod configuration from {Path}", configPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load hexapod configuration");
                throw;
            }
        }

        public bool IsHexapodEnabled(string position)
        {
            return _config.Devices.TryGetValue(position, out var device) && device.IsEnabled;
        }

        public HexapodDeviceConfig GetDeviceConfig(string position)
        {
            if (_config.Devices.TryGetValue(position, out var device))
            {
                return device;
            }
            throw new KeyNotFoundException($"No configuration found for hexapod position: {position}");
        }

        public IEnumerable<string> GetEnabledHexapods()
        {
            return _config.Devices
                .Where(kvp => kvp.Value.IsEnabled)
                .Select(kvp => kvp.Key);
        }
    }

    public class EnhancedHexapodDeviceFactory
    {
        private readonly Dictionary<int, HexapodMovementService> _services;
        private readonly ILogger _logger;
        private readonly PositionRegistry _positionRegistry;
        private readonly HexapodConfigManager _configManager;
        private HexapodConnectionManager _connectionManager;

        public EnhancedHexapodDeviceFactory(
            PositionRegistry positionRegistry,
            HexapodConfigManager configManager,
            ILogger logger)
        {
            _services = new Dictionary<int, HexapodMovementService>();
            _positionRegistry = positionRegistry;
            _configManager = configManager;
            _logger = logger.ForContext<EnhancedHexapodDeviceFactory>();
        }

        public void Initialize(HexapodConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;

            // Create services only for enabled hexapods
            InitializeEnabledHexapods();
        }

        private void InitializeEnabledHexapods()
        {
            var positionToIdMap = new Dictionary<string, int>
        {
            { "Left", 0 },
            { "Bottom", 1 },
            { "Right", 2 }
        };

            var typeMap = new Dictionary<string, HexapodConnectionManager.HexapodType>
        {
            { "Left", HexapodConnectionManager.HexapodType.Left },
            { "Bottom", HexapodConnectionManager.HexapodType.Bottom },
            { "Right", HexapodConnectionManager.HexapodType.Right }
        };

            foreach (var position in _configManager.GetEnabledHexapods())
            {
                if (positionToIdMap.TryGetValue(position, out int id) &&
                    typeMap.TryGetValue(position, out var type))
                {
                    _services[id] = new HexapodMovementService(
                        _connectionManager,
                        type,
                        _positionRegistry,
                        _logger
                    );

                    _logger.Information("Initialized movement service for {Position} hexapod", position);
                }
            }
        }

        public bool IsHexapodEnabled(int hexapodId)
        {
            return _services.ContainsKey(hexapodId);
        }

        public HexapodMovementService GetService(int hexapodId)
        {
            if (_services.TryGetValue(hexapodId, out var service))
            {
                return service;
            }

            throw new ArgumentException($"No service found for hexapod ID: {hexapodId} - hexapod may be disabled");
        }

        public IReadOnlyDictionary<int, HexapodMovementService> GetAllServices()
        {
            return _services;
        }
    }
}
