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
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Motion
{


    public class MotionGraphManager
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, Graph> _graphs;
        private readonly Dictionary<string, DeviceConfig> _deviceConfigs;
        private readonly DevicePositionMonitor _positionMonitor;
        private readonly PositionRegistry _positionRegistry;
        public MotionGraphManager(
            DevicePositionMonitor positionMonitor,
            PositionRegistry positionRegistry,
            string configPath,
            ILogger logger)
        {
            _logger = logger.ForContext<MotionGraphManager>();
            _graphs = new Dictionary<string, Graph>();
            _deviceConfigs = new Dictionary<string, DeviceConfig>();
            _positionMonitor = positionMonitor ?? throw new ArgumentNullException(nameof(positionMonitor));
            _positionRegistry = positionRegistry;
            LoadGraphs();
            LoadDeviceConfigs(configPath);
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

        private void LoadDeviceConfigs(string configPath)
        {
            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<MotionSystemConfig>(json);

                foreach (var device in config.Devices)
                {
                    _deviceConfigs[device.Id] = device;
                    _logger.Information("Loaded configuration for device {Id} of type {Type}", device.Id, device.Type);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load device configurations");
                throw;
            }
        }

        private async Task<string> GetCurrentPosition(string deviceId)
        {
            try
            {
                var position = await _positionMonitor.GetCurrentPosition(deviceId);
                return DeterminePositionName(deviceId, position);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get current position for device {DeviceId}", deviceId);
                throw;
            }
        }

        private string DeterminePositionName(string deviceId, DevicePosition currentPosition)
        {
            try
            {
                // Parse device type and location from ID (e.g., "hex-left", "gantry-main")
                var parts = deviceId.Split('-');
                if (parts.Length != 2)
                {
                    throw new ArgumentException($"Invalid device ID format: {deviceId}");
                }

                // Tolerance for position matching (in mm)
                const double POSITION_TOLERANCE = 0.1;  // 0.1mm

                string closestPosition = null;
                double minDistance = double.MaxValue;

                switch (parts[0].ToLower())
                {
                    case "hex":
                        int hexapodId = _positionRegistry.GetHexapodIdFromLocation(parts[1]);
                        var hexPositions = _positionRegistry.GetAllHexapodPositions(hexapodId);

                        foreach (var position in hexPositions)
                        {
                            // Calculate only linear distance for hexapods
                            double distance = Math.Sqrt(
                                Math.Pow(currentPosition.X - position.Value.X, 2) +
                                Math.Pow(currentPosition.Y - position.Value.Y, 2) +
                                Math.Pow(currentPosition.Z - position.Value.Z, 2));

                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                closestPosition = position.Key;
                            }
                        }
                        break;

                    case "gantry":
                        var gantryPositions = _positionRegistry.GetAllGantryPositions(4); // Assuming gantry ID 4

                        foreach (var position in gantryPositions)
                        {
                            double distance = Math.Sqrt(
                                Math.Pow(currentPosition.X - position.Value.X, 2) +
                                Math.Pow(currentPosition.Y - position.Value.Y, 2) +
                                Math.Pow(currentPosition.Z - position.Value.Z, 2));

                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                closestPosition = position.Key;
                            }
                        }
                        break;

                    default:
                        throw new ArgumentException($"Unknown device type: {parts[0]}");
                }

                // Check if we're within tolerance
                if (minDistance <= POSITION_TOLERANCE)
                {
                    _logger.Debug("Device {DeviceId} identified at position {Position} (distance: {Distance:F3}mm)",
                        deviceId, closestPosition, minDistance);
                    return closestPosition;
                }

                _logger.Warning("Device {DeviceId} not near any known position (closest: {Position}, distance: {Distance:F3}mm)",
                    deviceId, closestPosition, minDistance);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error determining position name for device {DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<PathAnalysis> AnalyzeMovementPath(string deviceId, string targetPosition)
        {
            try
            {
                if (!_deviceConfigs.TryGetValue(deviceId, out var deviceConfig))
                {
                    _logger.Error("Device configuration not found for ID {DeviceId}", deviceId);
                    return new PathAnalysis { IsValid = false, Error = "Device configuration not found" };
                }

                if (!_graphs.TryGetValue(deviceConfig.GraphId, out var graph))
                {
                    _logger.Error("Graph not found for device {DeviceId} with GraphId {GraphId}",
                        deviceId, deviceConfig.GraphId);
                    return new PathAnalysis { IsValid = false, Error = "Motion graph not found" };
                }

                // Get current position name
                string currentPosition = await GetCurrentPosition(deviceId);
                if (string.IsNullOrEmpty(currentPosition))
                {
                    return new PathAnalysis
                    {
                        IsValid = false,
                        Error = $"Unable to determine current position for {deviceConfig.Name}"
                    };
                }

                // Find path
                var path = graph.ShortestPath(currentPosition, targetPosition);
                if (path == null || path.Count == 0)
                {
                    return new PathAnalysis
                    {
                        IsValid = false,
                        Error = $"No valid path found from {currentPosition} to {targetPosition}",
                        CurrentPosition = currentPosition,
                        TargetPosition = targetPosition
                    };
                }

                return new PathAnalysis
                {
                    IsValid = true,
                    DeviceType = deviceConfig.Type,
                    DeviceName = deviceConfig.Name,
                    CurrentPosition = currentPosition,
                    TargetPosition = targetPosition,
                    Path = path,
                    NumberOfSteps = path.Count - 1
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error analyzing movement path for device {DeviceId}", deviceId);
                return new PathAnalysis
                {
                    IsValid = false,
                    Error = $"Error analyzing path: {ex.Message}"
                };
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

    public class PathAnalysis
    {
        public bool IsValid { get; set; }
        public string Error { get; set; }
        public string DeviceType { get; set; }
        public string DeviceName { get; set; }
        public string CurrentPosition { get; set; }
        public string TargetPosition { get; set; }
        public List<string> Path { get; set; }
        public int NumberOfSteps { get; set; }
    }
}