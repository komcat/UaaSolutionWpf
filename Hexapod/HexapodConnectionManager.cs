using Serilog;
using System.Windows;
using System.IO;
using UaaSolutionWpf.Controls;
using UaaSolutionWpf.Hexapod;
using UaaSolutionWpf.Config;
public class HexapodConnectionManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly Dictionary<HexapodType, PIConnection> _piConnections;
    private readonly Dictionary<HexapodType, HexapodGCS> _hexapodControllers;
    private readonly Dictionary<HexapodType, HexapodControl> _hexapodControls;
    private readonly ConfigurationManager _configManager;
    private bool _disposed;

    public enum HexapodType
    {
        Left,       
        Bottom,
        Right
    }

    public HexapodConnectionManager(Dictionary<HexapodType, HexapodControl> controls)
    {
        _logger = Log.ForContext<HexapodConnectionManager>();
        _piConnections = new Dictionary<HexapodType, PIConnection>();
        _hexapodControllers = new Dictionary<HexapodType, HexapodGCS>();
        _hexapodControls = controls;

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

            // Initialize settings only for provided controls
            if (_hexapodControls.ContainsKey(HexapodType.Left))
            {
                _piConnections[HexapodType.Left] = new PIConnection
                {
                    IPAddress = hexapodSettings.Left.IpAddress ?? "192.168.0.10",
                    Port = hexapodSettings.Left.Port != 0 ? hexapodSettings.Left.Port : 50000
                };
            }

            if (_hexapodControls.ContainsKey(HexapodType.Bottom))
            {
                _piConnections[HexapodType.Bottom] = new PIConnection
                {
                    IPAddress = hexapodSettings.Bottom.IpAddress ?? "192.168.0.20",
                    Port = hexapodSettings.Bottom.Port != 0 ? hexapodSettings.Bottom.Port : 50000
                };
            }

            if (_hexapodControls.ContainsKey(HexapodType.Right))
            {
                _piConnections[HexapodType.Right] = new PIConnection
                {
                    IPAddress = hexapodSettings.Right.IpAddress ?? "192.168.0.30",
                    Port = hexapodSettings.Right.Port != 0 ? hexapodSettings.Right.Port : 50000
                };
            }

            // Update UI controls with configuration values
            foreach (var kvp in _hexapodControls)
            {
                if (_piConnections.TryGetValue(kvp.Key, out var connection))
                {
                    kvp.Value.IpAddress = connection.IPAddress;
                    kvp.Value.PortNumber = connection.Port;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize connection settings from configuration");
            throw new InvalidOperationException("Failed to load hexapod connection settings from configuration", ex);
        }
    }

    public void InitializeConnections()
    {
        try
        {
            foreach (var kvp in _hexapodControls)
            {
                var type = kvp.Key;
                var control = kvp.Value;
                var connection = _piConnections[type];

                _logger.Information("Attempting to connect to {Type} Hexapod at {IP}:{Port}",
                    type, connection.IPAddress, connection.Port);

                _hexapodControllers[type] = new HexapodGCS($"{type} Hexapod", _logger);
                int controllerId = _hexapodControllers[type].Connect(connection.IPAddress, connection.Port);

                control.IsConnected = (controllerId >= 0);

                if (controllerId >= 0)
                {
                    _logger.Information("{Type} Hexapod connected successfully", type);
                    ConfigureConnectedHexapod(type);
                    LogInitialPositions(type);  // Add this line
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