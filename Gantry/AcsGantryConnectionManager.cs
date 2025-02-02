using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Serilog;
using System.Runtime.InteropServices;
using UaaSolutionWpf.Config;
using UaaSolutionWpf.Controls;
using System.Windows;

namespace UaaSolutionWpf.Gantry
{
    public class AcsGantryConnectionManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Dictionary<int, ACSController> _controllers;
        private readonly ConfigurationManager _configManager;
        private readonly GantryControl _gantryControl;
        private bool _disposed;
        private string _ipAddress;

        public event Action<bool> ConnectionStatusChanged;
        public event Action<string> ErrorOccurred;

        public bool IsConnected { get; private set; }

        public AcsGantryConnectionManager(GantryControl gantryControl, ILogger logger)
        {
            _logger = logger.ForContext<AcsGantryConnectionManager>();
            _controllers = new Dictionary<int, ACSController>();
            _gantryControl = gantryControl;
            IsConnected = false;

            try
            {
                _configManager = new ConfigurationManager(System.IO.Path.Combine("Config", "appsettings.json"));
                _ipAddress = _configManager.Settings.ConnectionSettings.ACS.IpAddress;

                // Initialize control with configuration
                _gantryControl.IpAddress = _ipAddress;

                _logger.Information("Initialized AcsGantryConnectionManager with IP: {IpAddress} from configuration", _ipAddress);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load configuration");
                throw;
            }

            // Subscribe to UI events from GantryControl
            SubscribeToControlEvents();
        }

        private bool _updatingUI = false;
        private bool _processingUIEvent = false;

        private void SubscribeToControlEvents()
        {
            // Handle X axis enable/disable
            _gantryControl.PropertyChanged += async (sender, e) =>
            {
                if (_updatingUI || _processingUIEvent) return;

                _processingUIEvent = true;
                try
                {
                    switch (e.PropertyName)
                    {
                        case "IsXEnabled":
                            await EnableMotorAsync(0, _gantryControl.IsXEnabled);
                            break;
                        case "IsYEnabled":
                            await EnableMotorAsync(1, _gantryControl.IsYEnabled);
                            break;
                        case "IsZEnabled":
                            await EnableMotorAsync(2, _gantryControl.IsZEnabled);
                            break;
                    }
                }
                finally
                {
                    _processingUIEvent = false;
                }
            };
        }

        public string IpAddress => _ipAddress;

        public async Task InitializeControllerAsync(string name)
        {
            try
            {
                _logger.Information("Initializing controller with name: {Name}", name);

                var controller = new ACSController(name, _logger);
                _controllers[0] = controller;

                // Subscribe to controller events
                controller.ConnectionStatusChanged += OnControllerConnectionStatusChanged;
                controller.ErrorOccurred += OnControllerErrorOccurred;
                controller.MotorStateChanged += OnMotorStateChanged;
                controller.MotorEnabled += OnMotorEnabled;
                controller.MotorDisabled += OnMotorDisabled;

                await ConnectAsync();

                // Start position monitoring
                StartPositionMonitoring();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize controller: {Name}", name);
                throw;
            }
        }

        private void StartPositionMonitoring()
        {
            Task.Run(async () =>
            {
                while (!_disposed && IsConnected)
                {
                    try
                    {
                        var controller = GetController();

                        // Get status for all axes
                        var (xPos, xEnabled, _) = controller.GetAxisStatus(0);
                        var (yPos, yEnabled, _) = controller.GetAxisStatus(1);
                        var (zPos, zEnabled, _) = controller.GetAxisStatus(2);

                        // Update UI on the UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _updatingUI = true;
                            try
                            {
                                _gantryControl.XPosition = xPos;
                                _gantryControl.YPosition = yPos;
                                _gantryControl.ZPosition = zPos;

                                // Only update enabled states if not processing a UI event
                                if (!_processingUIEvent)
                                {
                                    _gantryControl.IsXEnabled = xEnabled;
                                    _gantryControl.IsYEnabled = yEnabled;
                                    _gantryControl.IsZEnabled = zEnabled;
                                }
                            }
                            finally
                            {
                                _updatingUI = false;
                            }
                        });

                        await Task.Delay(200); // Reduced update frequency to 500ms
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error updating position status");
                        await Task.Delay(1000); // Wait longer on error
                    }
                }
            });
        }

        public async Task ConnectAsync()
        {
            try
            {
                _logger.Information("Attempting to connect to ACS controller at {IpAddress}", _ipAddress);

                foreach (var controller in _controllers.Values)
                {
                    await Task.Run(() => controller.Connect("Ethernet", _ipAddress));
                }

                IsConnected = true;
                _gantryControl.IsConnected = true;
                ConnectionStatusChanged?.Invoke(true);
                _logger.Information("Successfully connected to ACS controller");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to connect to ACS controller");
                IsConnected = false;
                _gantryControl.IsConnected = false;
                ConnectionStatusChanged?.Invoke(false);
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _logger.Information("Disconnecting from ACS controller");

                foreach (var controller in _controllers.Values)
                {
                    await Task.Run(() => controller.Disconnect());
                }

                IsConnected = false;
                _gantryControl.IsConnected = false;
                ConnectionStatusChanged?.Invoke(false);
                _logger.Information("Successfully disconnected from ACS controller");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during disconnection");
                throw;
            }
        }

        public async Task MoveToAbsolutePositionAsync(int axis, double position)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Moving axis {Axis} to absolute position {Position}", axis, position);
                var controller = GetController();
                controller.SetCurrentAxis(axis);
                await Task.Run(() => controller.MoveMotorToAbsolute(position));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move to absolute position");
                throw;
            }
        }

        public async Task MoveRelativeAsync(int axis, double increment)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Moving axis {Axis} by increment {Increment}", axis, increment);
                var controller = GetController();
                controller.SetCurrentAxis(axis);
                await Task.Run(() => controller.MoveMotor(increment));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move relative position");
                throw;
            }
        }

        private async Task EnableMotorAsync(int axis, bool enable)
        {
            ValidateConnection();

            try
            {
                var controller = GetController();
                controller.SetCurrentAxis(axis);

                if (enable)
                {
                    _logger.Information("Enabling motor for axis {Axis}", axis);
                    await Task.Run(() => controller.EnableMotor());
                }
                else
                {
                    _logger.Information("Disabling motor for axis {Axis}", axis);
                    await Task.Run(() => controller.DisableMotor());
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to set motor state for axis {Axis}", axis);
                throw;
            }
        }

        public async Task StopAllMotorsAsync()
        {
            ValidateConnection();

            try
            {
                _logger.Information("Stopping all motors");
                var controller = GetController();
                await Task.Run(() => controller.StopAllMotors());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to stop all motors");
                throw;
            }
        }

        public ACSController GetController()
        {
            if (!_controllers.ContainsKey(0))
            {
                throw new InvalidOperationException("No controller initialized");
            }
            return _controllers[0];
        }

        private void ValidateConnection()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to ACS controller");
            }
        }

        // Event handlers
        private void OnControllerConnectionStatusChanged(bool status)
        {
            IsConnected = status;
            _gantryControl.IsConnected = status;
            ConnectionStatusChanged?.Invoke(status);
        }

        private void OnControllerErrorOccurred(string error)
        {
            _logger.Error("Controller error: {Error}", error);
            ErrorOccurred?.Invoke(error);
        }

        private void OnMotorStateChanged(int axis, double position, bool isEnabled, bool isMoving)
        {
            _logger.Debug("Motor state changed - Axis: {Axis}, Position: {Position}, Enabled: {Enabled}, Moving: {Moving}",
                axis, position, isEnabled, isMoving);

            Application.Current.Dispatcher.Invoke(() =>
            {
                _updatingUI = true;
                try
                {
                    switch (axis)
                    {
                        case 0:
                            _gantryControl.XPosition = position;
                            _gantryControl.IsXEnabled = isEnabled;
                            break;
                        case 1:
                            _gantryControl.YPosition = position;
                            _gantryControl.IsYEnabled = isEnabled;
                            break;
                        case 2:
                            _gantryControl.ZPosition = position;
                            _gantryControl.IsZEnabled = isEnabled;
                            break;
                    }
                }
                finally
                {
                    _updatingUI = false;
                }
            });
        }

        private void OnMotorEnabled()
        {
            _logger.Information("Motor enabled");
        }

        private void OnMotorDisabled()
        {
            _logger.Information("Motor disabled");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                foreach (var controller in _controllers.Values)
                {
                    try
                    {
                        controller.StopAllMotors();
                        controller.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during controller cleanup");
                    }
                }
                _controllers.Clear();
            }

            _disposed = true;
        }
    }
}