﻿using Serilog;
using System.Windows;
using System.IO;
using UaaSolutionWpf.Controls;
using UaaSolutionWpf.Hexapod;
using UaaSolutionWpf.Config;
public class HexapodConnectionManager : IDisposable
{
    
    public enum HexapodType
    {
        Left,       
        Bottom,
        Right
    }
    private readonly ILogger _logger;
    private readonly Dictionary<HexapodType, PIConnection> _piConnections;
    private readonly Dictionary<HexapodType, HexapodGCS> _hexapodControllers;
    private readonly Dictionary<HexapodType, HexapodControl> _hexapodControls;
    private readonly ConfigurationManager _configManager;
    private readonly HexapodConfigManager _hexapodConfigManager;
    private bool _disposed;

    public HexapodConnectionManager(
        Dictionary<HexapodType, HexapodControl> controls,
        HexapodConfigManager hexapodConfigManager)
    {
        _logger = Log.ForContext<HexapodConnectionManager>();
        _piConnections = new Dictionary<HexapodType, PIConnection>();
        _hexapodControllers = new Dictionary<HexapodType, HexapodGCS>();
        _hexapodControls = controls;
        _hexapodConfigManager = hexapodConfigManager ?? throw new ArgumentNullException(nameof(hexapodConfigManager));

        try
        {
            _configManager = new ConfigurationManager(Path.Combine("Config", "appsettings.json"));
            InitializeConnectionSettings();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load configuration");
            throw;
        }
    }

    private void InitializeConnectionSettings()
    {
        try
        {
            var hexapodSettings = _configManager.Settings.ConnectionSettings.Hexapods;

            // Only initialize settings for enabled hexapods
            foreach (var kvp in _hexapodControls)
            {
                var type = kvp.Key;
                var location = type.ToString(); // Converts to "Left", "Right", etc.

                // Skip if hexapod is disabled in config
                if (!_hexapodConfigManager.IsHexapodEnabled(location))
                {
                    _logger.Information("Skipping {Type} Hexapod - disabled in configuration", type);
                    kvp.Value.IsConnected = false;
                    continue;
                }

                // Get connection settings based on type
                var settings = type switch
                {
                    HexapodType.Left => hexapodSettings.Left,
                    HexapodType.Bottom => hexapodSettings.Bottom,
                    HexapodType.Right => hexapodSettings.Right,
                    _ => throw new ArgumentException($"Invalid hexapod type: {type}")
                };

                _piConnections[type] = new PIConnection
                {
                    IPAddress = settings.IpAddress ?? $"192.168.0.{GetDefaultPort(type)}",
                    Port = settings.Port != 0 ? settings.Port : 50000
                };

                // Update UI control with connection settings
                kvp.Value.IpAddress = _piConnections[type].IPAddress;
                kvp.Value.PortNumber = _piConnections[type].Port;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize connection settings");
            throw new InvalidOperationException("Failed to load hexapod connection settings", ex);
        }
    }
    private int GetDefaultPort(HexapodType type) => type switch
    {
        HexapodType.Left => 10,
        HexapodType.Bottom => 20,
        HexapodType.Right => 30,
        _ => throw new ArgumentException($"Invalid hexapod type: {type}")
    };

    public void InitializeConnections()
    {
        try
        {
            foreach (var kvp in _hexapodControls)
            {
                var type = kvp.Key;
                var control = kvp.Value;
                var location = type.ToString();

                // Skip if hexapod is disabled
                if (!_hexapodConfigManager.IsHexapodEnabled(location))
                {
                    _logger.Information("{Type} Hexapod is disabled, skipping connection", type);
                    control.IsConnected = false;
                    continue;
                }

                // Only attempt connection if we have connection settings
                if (!_piConnections.TryGetValue(type, out var connection))
                {
                    _logger.Warning("{Type} Hexapod has no connection settings", type);
                    control.IsConnected = false;
                    continue;
                }

                _logger.Information("Attempting to connect to {Type} Hexapod at {IP}:{Port}",
                    type, connection.IPAddress, connection.Port);

                _hexapodControllers[type] = new HexapodGCS($"{type} Hexapod", _logger);
                int controllerId = _hexapodControllers[type].Connect(connection.IPAddress, connection.Port);

                control.IsConnected = (controllerId >= 0);

                if (controllerId >= 0)
                {
                    _logger.Information("{Type} Hexapod connected successfully", type);
                    ConfigureConnectedHexapod(type);
                    LogInitialPositions(type);
                }
                else
                {
                    _logger.Warning("{Type} Hexapod connection failed", type);
                    ShowConnectionError($"{type} Hexapod");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error initializing hexapod connections");
            ShowInitializationError(ex.Message);
        }
    }
    private void ConfigureConnectedHexapod(HexapodType type)
    {
        _hexapodControllers[type].StartRealTimePositionUpdates(100);
        _hexapodControllers[type].PositionUpdated += (positions) =>
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateHexapodPosition(_hexapodControls[type], positions);
            }));
        };
    }

    private void ConfigureConnectedHexapod(int index)
    {
        // Start position updates
        _hexapodControllers[(HexapodType)index].StartRealTimePositionUpdates(100); // Update every 100ms

        // Subscribe to position updates
        _hexapodControllers[(HexapodType)index].PositionUpdated += (positions) =>
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateHexapodPosition(_hexapodControls[(HexapodType)index], positions);
            }));
        };
    }

    private void UpdateHexapodPosition(HexapodControl control, double[] positions)
    {
        if (positions.Length >= 6)
        {
            control.XPosition = positions[0];
            control.YPosition = positions[1];
            control.ZPosition = positions[2];
            control.UPosition = positions[3];
            control.VPosition = positions[4];
            control.WPosition = positions[5];
        }
    }

    private void ShowConnectionError(string name)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            MessageBox.Show(
                $"Failed to connect to {name}. Please check the connection settings.",
                "Connection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }));
    }

    private void ShowInitializationError(string message)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            MessageBox.Show(
                $"Error initializing hexapod connections: {message}",
                "Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }));
    }

    private string GetHexapodName(int index) => index switch
    {
        0 => "Left Hexapod",
        1 => "Bottom Hexapod",
        2 => "Right Hexapod",
        _ => $"Hexapod {index}"
    };

    public HexapodGCS GetHexapodController(HexapodType type)
    {
        return _hexapodControllers.TryGetValue(type, out var controller) ? controller : null;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (var kvp in _hexapodControllers)
                {
                    kvp.Value?.Dispose();
                }
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    private void LogInitialPositions(HexapodType type)
    {
        try
        {
            if (_hexapodControllers.TryGetValue(type, out var controller))
            {
                LogCurrentPosition(type, controller);
                LogPivotPoint(type, controller);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get initial status for {Type} Hexapod", type);
        }
    }

    private void LogCurrentPosition(HexapodType type, HexapodGCS controller)
    {
        try
        {
            double[] positions = controller.GetPosition();
            _logger.Information("{Type} Hexapod Initial Position - X:{X:F4}, Y:{Y:F4}, Z:{Z:F4}, U:{U:F4}, V:{V:F4}, W:{W:F4}",
                type,
                positions[0],
                positions[1],
                positions[2],
                positions[3],
                positions[4],
                positions[5]);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get current position for {Type} Hexapod", type);
        }
    }

    private void LogPivotPoint(HexapodType type, HexapodGCS controller)
    {
        try
        {
            if (controller.GetPivotCoordinates(out double pivotX, out double pivotY, out double pivotZ))
            {
                _logger.Information("{Type} Hexapod Pivot Point - X:{X:F4}, Y:{Y:F4}, Z:{Z:F4}",
                    type,
                    pivotX,
                    pivotY,
                    pivotZ);
            }
            else
            {
                _logger.Warning("{Type} Hexapod: Failed to get pivot point coordinates", type);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get pivot point for {Type} Hexapod", type);
        }
    }
}