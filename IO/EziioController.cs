
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace UaaSolutionWpf.IO
{
    public class EziioController
    {
        public const int TCP = 0;
        public const int UDP = 1;
        public const int OUTPUTPIN = 16;
        public static int nTimerCnt = 0;

        private readonly Dictionary<string, int> pinMapping;
        private bool connected;
        private int boardId;
        private readonly EziioClass eziio;
        private readonly ILogger _logger;

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<SetOutputEventArgs> SetOutputSuccessful;
        public event EventHandler<SetOutputEventArgs> SetOutputFailed;
        public event EventHandler LoadOutputPinMappingSuccessful;
        public event EventHandler<LoadOutputPinMappingFailedEventArgs> LoadOutputPinMappingFailed;

        public EziioController(string jsonFilePath, ILogger logger)
        {
            _logger = logger.ForContext<EziioController>();
            eziio = new EziioClass(_logger);
            pinMapping = new Dictionary<string, int>();
            LoadPinMapping(jsonFilePath);
            connected = false;
            boardId = 0; // Set default board ID
        }

        private void LoadPinMapping(string jsonFilePath)
        {
            try
            {
                if (!File.Exists(jsonFilePath))
                {
                    var errorMessage = $"The JSON file with pin mappings was not found at path: {jsonFilePath}";
                    _logger.Error(errorMessage);
                    throw new FileNotFoundException(errorMessage);
                }

                var jsonContent = File.ReadAllText(jsonFilePath);
                var loadedMapping = JsonConvert.DeserializeObject<Dictionary<string, int>>(jsonContent);

                // Clear and update the pin mapping
                pinMapping.Clear();
                foreach (var kvp in loadedMapping)
                {
                    pinMapping.Add(kvp.Key, kvp.Value);
                }

                _logger.Information("Successfully loaded pin mapping from {FilePath}", jsonFilePath);
                OnLoadOutputPinMappingSuccessful(EventArgs.Empty);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to load pin mapping: {ex.Message}";
                _logger.Error(ex, "Failed to load pin mapping from {FilePath}", jsonFilePath);
                OnLoadOutputPinMappingFailed(new LoadOutputPinMappingFailedEventArgs(errorMessage));
            }
        }

        public void Connect(int commType, int boardId)
        {
            try
            {
                this.boardId = boardId;
                if (eziio.Connect(commType, boardId))
                {
                    connected = true;
                    _logger.Information("Successfully connected to board {BoardId} using {CommType}",
                        boardId, commType == TCP ? "TCP" : "UDP");
                    OnConnected(EventArgs.Empty);
                }
                else
                {
                    _logger.Error("Failed to connect to board {BoardId} using {CommType}",
                        boardId, commType == TCP ? "TCP" : "UDP");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while connecting to board {BoardId}", boardId);
                throw;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (connected)
                {
                    eziio.CloseConnection(boardId);
                    connected = false;
                    _logger.Information("Disconnected from board {BoardId}", boardId);
                    OnDisconnected(EventArgs.Empty);
                }
                else
                {
                    _logger.Warning("Disconnect attempted when not connected");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while disconnecting from board {BoardId}", boardId);
                throw;
            }
        }

        /// <summary>
        /// Eziio set output
        /// </summary>
        /// <param name="pinName">output string name of the pin</param>
        /// <param name="state">true=ON, false=OFF</param>
        /// <exception cref="InvalidOperationException">Thrown when not connected to the device</exception>
        /// <exception cref="ArgumentException">Thrown when pin name is not found in mapping</exception>
        public void SetOutput(string pinName, bool state)
        {
            if (!connected)
            {
                var error = "Not connected to the device";
                _logger.Error(error);
                throw new InvalidOperationException(error);
            }

            if (!pinMapping.ContainsKey(pinName))
            {
                var error = $"Pin name '{pinName}' not found in the mapping";
                _logger.Error(error);
                throw new ArgumentException(error);
            }

            try
            {
                int pinNumber = pinMapping[pinName];
                bool success;

                if (state)
                {
                    success = eziio.SetOutput(boardId, pinNumber);
                }
                else
                {
                    success = eziio.ClearOutput(boardId, pinNumber);
                }

                if (success)
                {
                    _logger.Information("Successfully set pin {PinName} (number {PinNumber}) to {State}",
                        pinName, pinNumber, state ? "On" : "Off");
                    OnSetOutputSuccessful(new SetOutputEventArgs(pinName, state));
                }
                else
                {
                    _logger.Error("Failed to set pin {PinName} (number {PinNumber}) to {State}",
                        pinName, pinNumber, state ? "On" : "Off");
                    OnSetOutputFailed(new SetOutputEventArgs(pinName, state));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while setting output for pin {PinName}", pinName);
                OnSetOutputFailed(new SetOutputEventArgs(pinName, state));
                throw;
            }
        }

        public bool GetOutputStatus(string pinName)
        {
            if (!connected)
            {
                _logger.Error("Cannot get output status: Not connected to the device");
                throw new InvalidOperationException("Not connected to the device");
            }

            if (!pinMapping.ContainsKey(pinName))
            {
                _logger.Error("Cannot get output status: Pin name '{PinName}' not found in mapping", pinName);
                throw new ArgumentException($"Pin name '{pinName}' not found in the mapping");
            }

            try
            {
                bool status = eziio.GetOutput(boardId);
                _logger.Information("Successfully retrieved output status for pin {PinName}", pinName);
                return status;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get output status for pin {PinName}", pinName);
                throw;
            }
        }

        protected virtual void OnConnected(EventArgs e)
        {
            Connected?.Invoke(this, e);
        }

        protected virtual void OnDisconnected(EventArgs e)
        {
            Disconnected?.Invoke(this, e);
        }

        protected virtual void OnSetOutputSuccessful(SetOutputEventArgs e)
        {
            SetOutputSuccessful?.Invoke(this, e);
        }

        protected virtual void OnSetOutputFailed(SetOutputEventArgs e)
        {
            SetOutputFailed?.Invoke(this, e);
        }

        protected virtual void OnLoadOutputPinMappingSuccessful(EventArgs e)
        {
            LoadOutputPinMappingSuccessful?.Invoke(this, e);
        }

        protected virtual void OnLoadOutputPinMappingFailed(LoadOutputPinMappingFailedEventArgs e)
        {
            LoadOutputPinMappingFailed?.Invoke(this, e);
        }
    }

    public class SetOutputEventArgs : EventArgs
    {
        public string PinName { get; }
        public bool State { get; }

        public SetOutputEventArgs(string pinName, bool state)
        {
            PinName = pinName;
            State = state;
        }
    }

    public class LoadOutputPinMappingFailedEventArgs : EventArgs
    {
        public string ErrorMessage { get; }

        public LoadOutputPinMappingFailedEventArgs(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
    }
}