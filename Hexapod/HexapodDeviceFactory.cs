using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Hexapod
{
    public class HexapodDeviceFactory
    {
        private readonly Dictionary<int, HexapodMovementService> _services;
        private readonly ILogger _logger;
        private readonly PositionRegistry _positionRegistry;
        private HexapodConnectionManager _connectionManager;

        public HexapodDeviceFactory(PositionRegistry positionRegistry, ILogger logger)
        {
            _services = new Dictionary<int, HexapodMovementService>();
            _positionRegistry = positionRegistry;
            _logger = logger.ForContext<HexapodDeviceFactory>();
        }

        public void Initialize(HexapodConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;

            // Create services for all hexapod types
            _services[0] = new HexapodMovementService(
                connectionManager,
                HexapodConnectionManager.HexapodType.Left,
                _positionRegistry,
                _logger
            );

            _services[1] = new HexapodMovementService(
                connectionManager,
                HexapodConnectionManager.HexapodType.Bottom,
                _positionRegistry,
                _logger
            );

            _services[2] = new HexapodMovementService(
                connectionManager,
                HexapodConnectionManager.HexapodType.Right,
                _positionRegistry,
                _logger
            );

            _logger.Information("Initialized movement services for all hexapods");
        }

        public HexapodMovementService GetService(int hexapodId)
        {
            if (_services.TryGetValue(hexapodId, out var service))
            {
                return service;
            }

            throw new ArgumentException($"No service found for hexapod ID: {hexapodId}");
        }

        public IReadOnlyDictionary<int, HexapodMovementService> GetAllServices()
        {
            return _services;
        }
    }
}
