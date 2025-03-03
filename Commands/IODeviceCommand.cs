using System;
using System.Threading.Tasks;
using Serilog;
using EzIIOLib;
using UaaSolutionWpf.Commands;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Command to set the state of an output pin
    /// </summary>
    public class SetOutputPinCommand : Command<MultiDeviceManager>
    {
        private readonly string _deviceName;
        private readonly string _pinName;
        private readonly bool _state;

        public SetOutputPinCommand(
            MultiDeviceManager deviceManager,
            string deviceName,
            string pinName,
            bool state,
            ILogger logger = null)
            : base(
                deviceManager,
                $"SetOutput-{deviceName}-{pinName}-{(state ? "On" : "Off")}",
                $"Set output pin {pinName} on device {deviceName} to {(state ? "On" : "Off")}",
                logger)
        {
            _deviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
            _pinName = pinName ?? throw new ArgumentNullException(nameof(pinName));
            _state = state;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                _logger.Information("Setting output pin {PinName} on device {DeviceName} to {State}",
                    _pinName, _deviceName, _state ? "On" : "Off");

                bool success;
                if (_state)
                {
                    // Set the output pin on
                    success = _context.SetOutput(_deviceName, _pinName);
                }
                else
                {
                    // Clear the output pin (off)
                    success = _context.ClearOutput(_deviceName, _pinName);
                }

                if (!success)
                {
                    _logger.Warning("Failed to set output pin {PinName} on device {DeviceName} to {State}",
                        _pinName, _deviceName, _state ? "On" : "Off");
                    return CommandResult.Failed(
                        $"Failed to set output pin {_pinName} on device {_deviceName} to {(_state ? "On" : "Off")}");
                }

                _logger.Information("Successfully set output pin {PinName} on device {DeviceName} to {State}",
                    _pinName, _deviceName, _state ? "On" : "Off");
                return CommandResult.Successful(
                    $"Output pin {_pinName} on device {_deviceName} set to {(_state ? "On" : "Off")}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting output pin {PinName} on device {DeviceName} to {State}",
                    _pinName, _deviceName, _state ? "On" : "Off");
                return CommandResult.Failed(
                    $"Error setting output pin {_pinName} on device {_deviceName} to {(_state ? "On" : "Off")}: {ex.Message}",
                    ex);
            }
        }
    }

    /// <summary>
    /// Command to toggle the state of an output pin
    /// </summary>
    public class ToggleOutputPinCommand : Command<MultiDeviceManager>
    {
        private readonly string _deviceName;
        private readonly string _pinName;

        public ToggleOutputPinCommand(
            MultiDeviceManager deviceManager,
            string deviceName,
            string pinName,
            ILogger logger = null)
            : base(
                deviceManager,
                $"ToggleOutput-{deviceName}-{pinName}",
                $"Toggle output pin {pinName} on device {deviceName}",
                logger)
        {
            _deviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
            _pinName = pinName ?? throw new ArgumentNullException(nameof(pinName));
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                _logger.Information("Toggling output pin {PinName} on device {DeviceName}",
                    _pinName, _deviceName);

                bool success = _context.ToggleOutput(_deviceName, _pinName);

                if (!success)
                {
                    _logger.Warning("Failed to toggle output pin {PinName} on device {DeviceName}",
                        _pinName, _deviceName);
                    return CommandResult.Failed(
                        $"Failed to toggle output pin {_pinName} on device {_deviceName}");
                }

                // Get the current state after toggling to report in the result
                bool? currentState = _context.GetOutputState(_deviceName, _pinName);
                string stateText = currentState.HasValue ? (currentState.Value ? "On" : "Off") : "Unknown";

                _logger.Information("Successfully toggled output pin {PinName} on device {DeviceName} to {State}",
                    _pinName, _deviceName, stateText);
                return CommandResult.Successful(
                    $"Output pin {_pinName} on device {_deviceName} toggled to {stateText}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error toggling output pin {PinName} on device {DeviceName}",
                    _pinName, _deviceName);
                return CommandResult.Failed(
                    $"Error toggling output pin {_pinName} on device {_deviceName}: {ex.Message}",
                    ex);
            }
        }
    }
}