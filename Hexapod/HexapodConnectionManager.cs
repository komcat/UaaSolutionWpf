using Serilog;
using System.Windows;
using System.IO;
using UaaSolutionWpf.Controls;
using UaaSolutionWpf.Hexapod;
using UaaSolutionWpf.Config;
public class HexapodConnectionManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly PIConnection[] _piConnections;
    private readonly HexapodGCS[] _hexapodControllers;
    private readonly HexapodControl[] _hexapodControls;
    private readonly ConfigurationManager _configManager;
    private bool _disposed;

    public HexapodConnectionManager(HexapodControl leftControl, HexapodControl bottomControl, HexapodControl rightControl)
    {
        _logger = Log.ForContext<HexapodConnectionManager>();
        _piConnections = new PIConnection[3];
        _hexapodControllers = new HexapodGCS[3];
        _hexapodControls = new[] { leftControl, bottomControl, rightControl };

        try
        {
            // Use the existing ConfigurationManager with the path in the Config folder
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

            // Load Left Hexapod settings
            _piConnections[0] = new PIConnection
            {
                IPAddress = hexapodSettings.Left.IpAddress,
                Port = hexapodSettings.Left.Port
            };

            // Load Bottom Hexapod settings
            _piConnections[1] = new PIConnection
            {
                IPAddress = hexapodSettings.Bottom.IpAddress,
                Port = hexapodSettings.Bottom.Port
            };

            // Load Right Hexapod settings
            _piConnections[2] = new PIConnection
            {
                IPAddress = hexapodSettings.Right.IpAddress,
                Port = hexapodSettings.Right.Port
            };

            // Update UI controls with the configuration values
            for (int i = 0; i < _hexapodControls.Length; i++)
            {
                if (_hexapodControls[i] != null)
                {
                    _hexapodControls[i].IpAddress = _piConnections[i].IPAddress;
                    _hexapodControls[i].PortNumber = _piConnections[i].Port;
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
            for (int i = 0; i < _hexapodControls.Length; i++)
            {
                if (_hexapodControls[i] == null)
                {
                    _logger.Warning("Hexapod control {Index} is null", i);
                    continue;
                }

                string name = GetHexapodName(i);
                _logger.Information("Attempting to connect to {Name} at {IP}:{Port}",
                    name, _piConnections[i].IPAddress, _piConnections[i].Port);

                _hexapodControllers[i] = new HexapodGCS(name, _logger);

                // Use the settings from the PIConnection array
                string ipAddress = _piConnections[i].IPAddress;
                int port = _piConnections[i].Port;

                // Update the UI control with the connection settings
                _hexapodControls[i].IpAddress = ipAddress;
                _hexapodControls[i].PortNumber = port;

                int controllerId = _hexapodControllers[i].Connect(ipAddress, port);
                bool isConnected = controllerId >= 0;

                // Update the IsConnected property which will automatically update the UI through databinding
                _hexapodControls[i].IsConnected = isConnected;

                if (isConnected)
                {
                    _logger.Information("{Name} connected successfully", name);
                    ConfigureConnectedHexapod(i);
                }
                else
                {
                    _logger.Warning("{Name} connection failed", name);
                    ShowConnectionError(name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error initializing hexapod connections");
            ShowInitializationError(ex.Message);
        }
    }

    private void ConfigureConnectedHexapod(int index)
    {
        // Start position updates
        _hexapodControllers[index].StartRealTimePositionUpdates(100); // Update every 100ms

        // Subscribe to position updates
        _hexapodControllers[index].PositionUpdated += (positions) =>
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateHexapodPosition(_hexapodControls[index], positions);
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

    public HexapodGCS GetHexapodController(int index)
    {
        if (index < 0 || index >= _hexapodControllers.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _hexapodControllers[index];
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (var controller in _hexapodControllers)
                {
                    controller?.Dispose();
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
}