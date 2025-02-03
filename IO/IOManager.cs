using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FASTECH;
using Newtonsoft.Json;
using Serilog;
using UaaSolutionWpf.IO;

namespace IOControl
{
    public class IOConfig
    {
        public class Metadata
        {
            public string Version { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        public class IOPin
        {
            public int Pin { get; set; }
            public string Name { get; set; }
        }

        public class IOBlockConfig
        {
            public List<IOPin> Inputs { get; set; }
            public List<IOPin> Outputs { get; set; }
        }

        public class IOBlock
        {
            public int DeviceId { get; set; }
            public string Name { get; set; }
            public string IP { get; set; }
            public IOBlockConfig IOConfig { get; set; }
        }

        public Metadata Metadata { get; set; }
        public List<IOBlock> Eziio { get; set; }
    }

    public class IOManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, EziioClass> _ioBlocks;
        private readonly IOConfig _config;
        private bool _disposed;

        public IOManager(ILogger logger, string configPath)
        {
            _logger = logger.ForContext<IOManager>();
            _ioBlocks = new Dictionary<string, EziioClass>();

            try
            {
                string jsonContent = System.IO.File.ReadAllText(configPath);
                _config = JsonConvert.DeserializeObject<IOConfig>(jsonContent);
                _logger.Information("Successfully loaded IO configuration from {ConfigPath}", configPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load IO configuration from {ConfigPath}", configPath);
                throw;
            }
        }

        public async Task InitializeAllBlocksAsync()
        {
            foreach (var block in _config.Eziio)
            {
                await InitializeBlockAsync(block);
            }
        }

        private async Task InitializeBlockAsync(IOConfig.IOBlock block)
        {
            try
            {
                var eziio = new EziioClass(_logger);
                IPAddress ip = IPAddress.Parse(block.IP);
                byte[] ipBytes = ip.GetAddressBytes();

                bool connected = await eziio.ConnectAsync(
                    ConnectionType.TCP,
                    block.DeviceId,
                    ipBytes[0],
                    ipBytes[1],
                    ipBytes[2],
                    ipBytes[3]
                );

                if (connected)
                {
                    _ioBlocks[block.Name] = eziio;
                    _logger.Information("Successfully connected to IO block {BlockName} at {IP}", block.Name, block.IP);
                }
                else
                {
                    _logger.Error("Failed to connect to IO block {BlockName} at {IP}", block.Name, block.IP);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing IO block {BlockName} at {IP}", block.Name, block.IP);
                throw;
            }
        }

        public bool SetOutput(string blockName, string pinName, bool state)
        {
            try
            {
                if (!_ioBlocks.TryGetValue(blockName, out var eziio))
                {
                    throw new KeyNotFoundException($"IO block '{blockName}' not found");
                }

                var block = _config.Eziio.Find(b => b.Name == blockName);
                var pin = block.IOConfig.Outputs.Find(p => p.Name == pinName);

                if (pin == null)
                {
                    throw new KeyNotFoundException($"Output pin '{pinName}' not found in block '{blockName}'");
                }

                if (state)
                {
                    return eziio.SetOutput(block.DeviceId, pin.Pin);
                }
                else
                {
                    return eziio.ClearOutput(block.DeviceId, pin.Pin);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting output {PinName} on block {BlockName}", pinName, blockName);
                throw;
            }
        }

        public bool GetOutput(string blockName, string pinName)
        {
            try
            {
                if (!_ioBlocks.TryGetValue(blockName, out var eziio))
                {
                    throw new KeyNotFoundException($"IO block '{blockName}' not found");
                }

                var block = _config.Eziio.Find(b => b.Name == blockName);
                var pin = block.IOConfig.Outputs.Find(p => p.Name == pinName);

                if (pin == null)
                {
                    throw new KeyNotFoundException($"Output pin '{pinName}' not found in block '{blockName}'");
                }

                return eziio.GetPinStatus(block.DeviceId, pin.Pin);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting output status for {PinName} on block {BlockName}", pinName, blockName);
                throw;
            }
        }

        public bool GetInput(string blockName, string pinName)
        {
            try
            {
                if (!_ioBlocks.TryGetValue(blockName, out var eziio))
                {
                    throw new KeyNotFoundException($"IO block '{blockName}' not found");
                }

                var block = _config.Eziio.Find(b => b.Name == blockName);
                var pin = block.IOConfig.Inputs.Find(p => p.Name == pinName);

                if (pin == null)
                {
                    throw new KeyNotFoundException($"Input pin '{pinName}' not found in block '{blockName}'");
                }

                uint uInput = 0;
                uint uLatch = 0;

                if (EziMOTIONPlusELib.FAS_GetInput(block.DeviceId, ref uInput, ref uLatch) != EziMOTIONPlusELib.FMM_OK)
                {
                    throw new Exception($"Failed to get input status for block '{blockName}'");
                }

                return (uInput & (1u << pin.Pin)) != 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting input status for {PinName} on block {BlockName}", pinName, blockName);
                throw;
            }
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
                    foreach (var block in _config.Eziio)
                    {
                        if (_ioBlocks.TryGetValue(block.Name, out var eziio))
                        {
                            try
                            {
                                eziio.CloseConnection(block.DeviceId);
                                eziio.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Error disposing IO block {BlockName}", block.Name);
                            }
                        }
                    }
                }

                _disposed = true;
            }
        }

        ~IOManager()
        {
            Dispose(false);
        }
    }
}