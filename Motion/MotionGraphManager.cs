using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using System.IO;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Config;
using UaaSolutionWpf.Hexapod;

namespace UaaSolutionWpf.Motion
{
    public class DeviceConfig
    {
        public string Id { get; set; }
        public string Type { get; set; }  // "Hexapod" or "Gantry"
        public string Name { get; set; }
        public string GraphId { get; set; }  // Reference to the graph in WorkingGraphs.json
    }

    public class MotionSystemConfig
    {
        public List<DeviceConfig> Devices { get; set; }
    }

    public class MotionGraphManager
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, Graph> _graphs;
        private readonly Dictionary<string, object> _deviceControllers;
        private readonly Dictionary<string, DeviceConfig> _deviceConfigs;

        public MotionGraphManager(
            HexapodConnectionManager hexapodManager,
            AcsGantryConnectionManager gantryConnectionManager,
            string configPath,
            ILogger logger)
        {
            _logger = logger.ForContext<MotionGraphManager>();
            _graphs = new Dictionary<string, Graph>();
            _deviceControllers = new Dictionary<string, object>();
            _deviceConfigs = new Dictionary<string, DeviceConfig>();

            LoadGraphs();
            InitializeDevices(hexapodManager, gantryConnectionManager, configPath);
        }

        private void LoadGraphs()
        {
            try
            {
                string graphPath = Path.Combine("Config", "WorkingGraphs.json");
                _logger.Information("Loading motion graphs from {Path}", graphPath);

                string jsonContent = File.ReadAllText(graphPath);
                var graphSet = JsonConvert.DeserializeObject<GraphSet>(jsonContent);

                foreach (var graphKvp in graphSet.graphs)
                {
                    var graph = new Graph();
                    foreach (var edge in graphKvp.Value.edges)
                    {
                        graph.AddEdge(edge.from, edge.to, edge.weight);
                    }
                    _graphs[graphKvp.Key] = graph;
                }

                _logger.Information("Successfully loaded {Count} graphs", _graphs.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load motion graphs");
                throw;
            }
        }

        private void InitializeDevices(
            HexapodConnectionManager hexapodManager,
            AcsGantryConnectionManager gantryConnectionManager,
            string configPath)
        {
            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<MotionSystemConfig>(json);

                foreach (var device in config.Devices)
                {
                    _deviceConfigs[device.Id] = device;

                    switch (device.Type.ToLower())
                    {
                        case "hexapod":
                            if (Enum.TryParse<HexapodConnectionManager.HexapodType>(device.Name, true, out var hexapodType))
                            {
                                var controller = hexapodManager.GetHexapodController(hexapodType);
                                if (controller != null)
                                {
                                    _deviceControllers[device.Id] = controller;
                                    _logger.Information("Successfully initialized Hexapod device {Id}", device.Id);
                                }
                            }
                            break;

                        case "gantry":
                            _deviceControllers[device.Id] = gantryConnectionManager;
                            _logger.Information("Successfully initialized Gantry device {Id}", device.Id);
                            break;

                        default:
                            _logger.Warning("Unknown device type {Type} for device {Id}", device.Type, device.Id);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize devices");
                throw;
            }
        }

        public async Task<bool> MoveDeviceToPosition(string deviceId, string targetPosition, bool showConfirmation = true)
        {
            try
            {
                if (!_deviceConfigs.TryGetValue(deviceId, out var deviceConfig))
                {
                    _logger.Error("Device configuration not found for ID {DeviceId}", deviceId);
                    return false;
                }

                if (!_deviceControllers.TryGetValue(deviceId, out var controller))
                {
                    _logger.Error("Device controller not found for ID {DeviceId}", deviceId);
                    return false;
                }

                if (!_graphs.TryGetValue(deviceConfig.GraphId, out var graph))
                {
                    _logger.Error("Graph not found for device {DeviceId} with GraphId {GraphId}",
                        deviceId, deviceConfig.GraphId);
                    return false;
                }

                _logger.Information("Attempting to move {DeviceType} {DeviceName} to position {Position}",
                    deviceConfig.Type, deviceConfig.Name, targetPosition);

                // Get current position
                string currentPosition = await GetCurrentPosition(deviceConfig, controller);
                if (string.IsNullOrEmpty(currentPosition))
                {
                    MessageBox.Show($"Unable to determine current position for {deviceConfig.Name}",
                        "Position Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Find path
                var path = graph.ShortestPath(currentPosition, targetPosition);
                if (path == null || path.Count == 0)
                {
                    MessageBox.Show($"No valid path found from {currentPosition} to {targetPosition}",
                        "Path Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Show confirmation if requested
                if (showConfirmation)
                {
                    var result = MessageBox.Show(
                        $"Move {deviceConfig.Name} through path:\n{string.Join(" -> ", path)}",
                        "Confirm Movement",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        return false;
                    }
                }

                // Execute movement
                return await ExecuteMovement(deviceConfig, controller, path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during movement for device {DeviceId}", deviceId);
                MessageBox.Show($"Error during movement: {ex.Message}",
                    "Movement Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<string> GetCurrentPosition(DeviceConfig config, object controller)
        {
            switch (config.Type.ToLower())
            {
                case "hexapod":
                    if (controller is HexapodGCS hexapodController)
                    {
                        return hexapodController.WhereAmI();
                    }
                    break;

                case "gantry":
                    if (controller is AcsGantryConnectionManager gantryController)
                    {
                        // Implement gantry position detection
                        return "Current"; // Placeholder
                    }
                    break;
            }

            return string.Empty;
        }

        private async Task<bool> ExecuteMovement(DeviceConfig config, object controller, List<string> path)
        {
            foreach (var position in path)
            {
                _logger.Information("Moving {DeviceName} to position: {Position}",
                    config.Name, position);

                bool success = false;
                switch (config.Type.ToLower())
                {
                    case "hexapod":
                        if (controller is HexapodGCS hexapodController)
                        {
                            success = await MoveHexapod(hexapodController, position);
                        }
                        break;

                    case "gantry":
                        if (controller is AcsGantryConnectionManager gantryController)
                        {
                            success = await MoveGantry(gantryController, position);
                        }
                        break;
                }

                if (!success)
                {
                    _logger.Error("Failed to move {DeviceName} to position {Position}",
                        config.Name, position);
                    return false;
                }

                // Wait for movement to complete
                await Task.Delay(100); // Implement proper movement completion detection
            }

            return true;
        }

        private async Task<bool> MoveHexapod(HexapodGCS controller, string position)
        {
            try
            {
                // Implement the actual hexapod movement logic
                // This should use the appropriate method from your HexapodGCS class
                _logger.Information("Moving hexapod to position {Position}", position);

                // Example:
                // await controller.MoveToPositionName(position);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving hexapod to position {Position}", position);
                return false;
            }
        }

        private async Task<bool> MoveGantry(AcsGantryConnectionManager controller, string position)
        {
            try
            {
                // Implement the actual gantry movement logic
                _logger.Information("Moving gantry to position {Position}", position);

                // Example:
                // await controller.MoveToPosition(position);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving gantry to position {Position}", position);
                return false;
            }
        }

        public IReadOnlyDictionary<string, DeviceConfig> GetConfiguredDevices()
        {
            return _deviceConfigs;
        }
    }

    // Supporting classes for graph structure
    public class GraphSet
    {
        public Dictionary<string, GraphData> graphs { get; set; }
    }

    public class GraphData
    {
        public List<string> nodes { get; set; }
        public List<Edge> edges { get; set; }
    }

    public class Edge
    {
        public string from { get; set; }
        public string to { get; set; }
        public int weight { get; set; }
    }
}