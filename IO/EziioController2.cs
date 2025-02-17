using FASTECH;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.IO
{
    public class EziioController2 : IDisposable
    {
        private const int TCP = 0;
        private const int UDP = 1;
        private const int OUTPUT_PIN_COUNT = 16;
        private const int INPUT_PIN_COUNT = 16;

        private static readonly uint[] PIN_MASKS = new uint[16]
        {
            0x00010000, 0x00020000, 0x00040000, 0x00080000,
            0x00100000, 0x00200000, 0x00400000, 0x00800000,
            0x01000000, 0x02000000, 0x04000000, 0x08000000,
            0x10000000, 0x20000000, 0x40000000, 0x80000000
        };

        private readonly ILogger _logger;
        private readonly EziioDevice _deviceConfig;
        private readonly IPAddress _ipAddress;
        private readonly int _deviceId;
        private readonly string _deviceName;

        // State tracking
        private bool _isConnected;
        private bool[] _inputStates = new bool[INPUT_PIN_COUNT];
        private bool[] _outputStates = new bool[OUTPUT_PIN_COUNT];
        private uint _lastLoggedInputStatus = uint.MaxValue;
        private uint _lastLoggedOutputStatus = uint.MaxValue;

        // Threading
        private readonly object _stateLock = new object();
        private Thread _monitorThread;
        private CancellationTokenSource _cancellationTokenSource;

        // Events
        public event EventHandler<bool> ConnectionStateChanged;
        public event EventHandler<(string Name, bool State)> InputStateChanged;
        public event EventHandler<(string Name, bool State)> OutputStateChanged;
        public event EventHandler<string> StatusMessageUpdated;

        public bool IsConnected => _isConnected;
        public string DeviceName => _deviceName;
        public string IpAddress => _ipAddress.ToString();

        public EziioController2(EziioDevice config, ILogger logger)
        {
            _deviceConfig = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger?.ForContext<EziioController2>() ?? throw new ArgumentNullException(nameof(logger));

            _deviceId = config.DeviceId;
            _deviceName = config.Name;

            if (!IPAddress.TryParse(config.IP, out _ipAddress))
            {
                throw new ArgumentException($"Invalid IP address format: {config.IP}");
            }

            _cancellationTokenSource = new CancellationTokenSource();
        }

        public bool Connect()
        {
            try
            {
                bool success = EziMOTIONPlusELib.FAS_ConnectTCP(_ipAddress, _deviceId);

                if (success)
                {
                    _isConnected = true;
                    _logger.Information("[{Device}] Successfully connected to {IP}", _deviceName, _ipAddress);
                    StartMonitoring();

                    ConnectionStateChanged?.Invoke(this, true);
                    StatusMessageUpdated?.Invoke(this, "Connected successfully");
                }
                else
                {
                    _logger.Error("[{Device}] Failed to connect to {IP}", _deviceName, _ipAddress);
                    StatusMessageUpdated?.Invoke(this, "Connection failed");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{Device}] Error connecting to device", _deviceName);
                StatusMessageUpdated?.Invoke(this, $"Connection error: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                StopMonitoring();

                if (_isConnected)
                {
                    EziMOTIONPlusELib.FAS_Close(_deviceId);
                    _isConnected = false;

                    _logger.Information("[{Device}] Disconnected from device", _deviceName);
                    ConnectionStateChanged?.Invoke(this, false);
                    StatusMessageUpdated?.Invoke(this, "Disconnected");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{Device}] Error disconnecting from device", _deviceName);
                StatusMessageUpdated?.Invoke(this, $"Disconnect error: {ex.Message}");
            }
        }

        public bool SetOutput(string pinName, bool state)
        {
            // Find the pin configuration from device config
            var pin = _deviceConfig.IOConfig.Outputs.Find(p => p.Name == pinName);
            if (pin == null)
            {
                _logger.Error("[{DeviceName}] Pin name {PinName} not found in config.", _deviceName, pinName);
                return false;
            }
            Task.Delay(35);
            MonitorOutputs();

            // Use the appropriate method based on desired state
            if (state)
            {
                return SetOutput(pin.Pin);
            }
            else
            {
                return ClearOutput(pin.Pin);
            }
        }

        public bool SetOutput(int pinNum)
        {
            if (pinNum < 0 || pinNum >= OUTPUT_PIN_COUNT)
            {
                _logger.Error("[{DeviceName}] Invalid pin number: {PinNumber}", _deviceName, pinNum);
                return false;
            }

            uint uSetMask = PIN_MASKS[pinNum];
            uint uClrMask = 0x00000000;

            // Set the output
            if (EziMOTIONPlusELib.FAS_SetOutput(_deviceId, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("[{DeviceName}] Failed to set output for pin {PinNumber}", _deviceName, pinNum);
                return false;
            }

            // Force an immediate output status check
            uint currentOutput = 0;
            uint status = 0;

            if (EziMOTIONPlusELib.FAS_GetOutput(_deviceId, ref currentOutput, ref status) == EziMOTIONPlusELib.FMM_OK)
            {
                // Consider the operation successful if we can set the output
                _logger.Debug("[{DeviceName}] Output pin {PinNumber} set successfully", _deviceName, pinNum);
                var pin = _deviceConfig.IOConfig.Outputs.Find(p => p.Pin == pinNum);
                if (pin != null)
                {
                    OutputStateChanged?.Invoke(this, (pin.Name, true));
                }

                // Update the state in our tracking array
                lock (_stateLock)
                {
                    _outputStates[pinNum] = true;
                }

                return true;
            }

            return false;
        }
        public bool ClearOutput(int pinNum)
        {
            if (pinNum < 0 || pinNum >= OUTPUT_PIN_COUNT)
            {
                _logger.Error("[{DeviceName}] Invalid pin number: {PinNumber}", _deviceName, pinNum);
                return false;
            }

            uint uSetMask = 0x00000000;
            uint uClrMask = PIN_MASKS[pinNum];

            // Clear the output
            if (EziMOTIONPlusELib.FAS_SetOutput(_deviceId, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("[{DeviceName}] Failed to clear output for pin {PinNumber}", _deviceName, pinNum);
                return false;
            }

            // Force an immediate output status check
            uint currentOutput = 0;
            uint status = 0;

            if (EziMOTIONPlusELib.FAS_GetOutput(_deviceId, ref currentOutput, ref status) == EziMOTIONPlusELib.FMM_OK)
            {
                // Consider the operation successful if we can clear the output
                _logger.Debug("[{DeviceName}] Output pin {PinNumber} cleared successfully", _deviceName, pinNum);
                var pin = _deviceConfig.IOConfig.Outputs.Find(p => p.Pin == pinNum);
                if (pin != null)
                {
                    OutputStateChanged?.Invoke(this, (pin.Name, false));
                }

                // Update the state in our tracking array
                lock (_stateLock)
                {
                    _outputStates[pinNum] = false;
                }

                return true;
            }

            return false;
        }

        public bool ClearOutputByName(string pinName)
        {
            // Find the pin configuration from device config
            var pin = _deviceConfig.IOConfig.Outputs.Find(p => p.Name == pinName);
            if (pin == null)
            {
                _logger.Error("[{DeviceName}] Pin name not found: {PinName}", _deviceName, pinName);
                return false;
            }

            return ClearOutput(pin.Pin);
        }
        private bool VerifyOutputSet(int pinNum)
        {
            lock (_stateLock)
            {
                return _outputStates[pinNum];
            }
        }

        private bool VerifyOutputCleared(int pinNum)
        {
            lock (_stateLock)
            {
                return !_outputStates[pinNum];
            }
        }
        public bool GetInputState(string pinName)
        {
            var pin = _deviceConfig.IOConfig.Inputs.Find(p => p.Name == pinName);
            if (pin == null)
            {
                _logger.Error("[{Device}] Input pin not found: {PinName}", _deviceName, pinName);
                return false;
            }

            lock (_stateLock)
            {
                return _inputStates[pin.Pin];
            }
        }

        public bool GetOutputState(string pinName)
        {
            var pin = _deviceConfig.IOConfig.Outputs.Find(p => p.Name == pinName);
            if (pin == null)
            {
                _logger.Error("[{Device}] Output pin not found: {PinName}", _deviceName, pinName);
                return false;
            }

            lock (_stateLock)
            {
                return _outputStates[pin.Pin];
            }
        }

        private void StartMonitoring()
        {
            _monitorThread = new Thread(MonitorStates)
            {
                IsBackground = true,
                Name = $"EziioMonitor_{_deviceName}"
            };

            _monitorThread.Start();
        }

        private void StopMonitoring()
        {
            _cancellationTokenSource.Cancel();
            _monitorThread?.Join(TimeSpan.FromSeconds(1));
        }

        private void MonitorStates()
        {
            _logger.Information("[{Device}] Starting state monitoring thread", _deviceName);

            while (!_cancellationTokenSource.Token.IsCancellationRequested && _isConnected)
            {
                try
                {
                    MonitorInputs();
                    MonitorOutputs();
                    Thread.Sleep(50); // Adjust polling rate as needed
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[{Device}] Error in monitoring thread", _deviceName);
                    Thread.Sleep(1000); // Back off on error
                }
            }

            _logger.Information("[{Device}] State monitoring thread stopped", _deviceName);
        }

        private void MonitorInputs()
        {
            uint currentInput = 0;
            uint latch = 0;

            if (EziMOTIONPlusELib.FAS_GetInput(_deviceId, ref currentInput, ref latch) == EziMOTIONPlusELib.FMM_OK)
            {
                if (currentInput != _lastLoggedInputStatus)
                {
                    _lastLoggedInputStatus = currentInput;
                    UpdateInputStates(currentInput);
                }
            }
        }

        private void MonitorOutputs()
        {
            uint currentOutput = 0;
            uint status = 0;

            if (EziMOTIONPlusELib.FAS_GetOutput(_deviceId, ref currentOutput, ref status) == EziMOTIONPlusELib.FMM_OK)
            {
                if (currentOutput != _lastLoggedOutputStatus)
                {
                    _lastLoggedOutputStatus = currentOutput;
                    UpdateOutputStates(currentOutput);
                }
            }
        }

        private void UpdateInputStates(uint inputStatus)
        {
            lock (_stateLock)
            {
                for (int i = 0; i < INPUT_PIN_COUNT; i++)
                {
                    bool newState = (inputStatus & (1u << i)) != 0;
                    if (_inputStates[i] != newState)
                    {
                        _inputStates[i] = newState;
                        var pin = _deviceConfig.IOConfig.Inputs.Find(p => p.Pin == i);
                        if (pin != null)
                        {
                            _logger.Information("[{Device}] Input {PinName} (Pin {PinNumber}) changed to {State}",
                                _deviceName, pin.Name, i, newState ? "ON" : "OFF");
                            InputStateChanged?.Invoke(this, (pin.Name, newState));
                        }
                        else
                        {
                            _logger.Debug("[{Device}] Unmapped input pin {PinNumber} changed to {State}",
                                _deviceName, i, newState ? "ON" : "OFF");
                        }
                    }
                }
            }
        }

        private void UpdateOutputStates(uint outputStatus)
        {
            lock (_stateLock)
            {
                for (int i = 0; i < OUTPUT_PIN_COUNT; i++)
                {
                    bool newState = (outputStatus & PIN_MASKS[i]) != 0;
                    if (_outputStates[i] != newState)
                    {
                        _outputStates[i] = newState;
                        var pin = _deviceConfig.IOConfig.Outputs.Find(p => p.Pin == i);
                        if (pin != null)
                        {
                            _logger.Information("[{Device}] Output {PinName} (Pin {PinNumber}) changed to {State}",
                                _deviceName, pin.Name, i, newState ? "ON" : "OFF");
                            OutputStateChanged?.Invoke(this, (pin.Name, newState));
                        }
                        else
                        {
                            _logger.Debug("[{Device}] Unmapped output pin {PinNumber} changed to {State}",
                                _deviceName, i, newState ? "ON" : "OFF");
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
            _cancellationTokenSource.Dispose();
        }
    }
}