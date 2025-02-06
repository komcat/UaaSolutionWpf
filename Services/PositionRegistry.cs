using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;
using UaaSolutionWpf.Motion;

namespace UaaSolutionWpf.Services
{
    public class PositionRegistry
    {
        private readonly ILogger _logger;
        private readonly WorkingPositions _positions;
        private readonly string _configPath;

        public PositionRegistry(string configPath, ILogger logger)
        {
            _logger = logger.ForContext<PositionRegistry>();
            _configPath = configPath;
            _positions = LoadPositions();
        }

        private WorkingPositions LoadPositions()
        {
            try
            {
                string jsonContent = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<WorkingPositions>(jsonContent)
                    ?? throw new InvalidOperationException("Failed to deserialize positions");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load working positions from {ConfigPath}", _configPath);
                return new WorkingPositions
                {
                    Hexapods = new List<HexapodData>(),
                    Gantries = new List<GantryData>()
                };
            }
        }

        public Position GetHexapodPosition(int hexapodId, string positionName)
        {
            var hexapod = _positions.Hexapods?.FirstOrDefault(h => h.HexapodId == hexapodId);
            if (hexapod?.Positions.TryGetValue(positionName, out var position) == true)
            {
                return position;
            }

            _logger.Warning("Position {PositionName} not found for hexapod {HexapodId}", positionName, hexapodId);
            return new Position();
        }

        public Position GetGantryPosition(int gantryId, string positionName)
        {
            var gantry = _positions.Gantries?.FirstOrDefault(g => g.GantryId == gantryId);
            if (gantry?.Positions.TryGetValue(positionName, out var position) == true)
            {
                return position;
            }

            _logger.Warning("Position {PositionName} not found for gantry {GantryId}", positionName, gantryId);
            return new Position();
        }

        public int GetHexapodIdFromLocation(string location)
        {
            return location.ToLower() switch
            {
                "left" => 0,
                "bottom" => 1,
                "right" => 2,
                _ => throw new ArgumentException($"Unknown hexapod location: {location}")
            };
        }

        public Dictionary<string, Position> GetAllHexapodPositions(int hexapodId)
        {
            return _positions.Hexapods?
                .FirstOrDefault(h => h.HexapodId == hexapodId)?
                .Positions ?? new Dictionary<string, Position>();
        }

        public Dictionary<string, Position> GetAllGantryPositions(int gantryId)
        {
            return _positions.Gantries?
                .FirstOrDefault(g => g.GantryId == gantryId)?
                .Positions ?? new Dictionary<string, Position>();
        }

        public bool TryGetHexapodPosition(int hexapodId, string positionName, out Position position)
        {
            position = new Position(); // Initialize with default value
            var hexapod = _positions.Hexapods?.FirstOrDefault(h => h.HexapodId == hexapodId);
            if (hexapod == null || !hexapod.Positions.TryGetValue(positionName, out position))
            {
                _logger.Warning("Position {PositionName} not found for hexapod {HexapodId}", positionName, hexapodId);
                return false;
            }
            return true;
        }

        public bool TryGetGantryPosition(int gantryId, string positionName, out Position position)
        {
            position = new Position(); // Initialize with default value
            var gantry = _positions.Gantries?.FirstOrDefault(g => g.GantryId == gantryId);
            if (gantry == null || !gantry.Positions.TryGetValue(positionName, out position))
            {
                _logger.Warning("Position {PositionName} not found for gantry {GantryId}", positionName, gantryId);
                return false;
            }
            return true;
        }
    }
}