using System;
using System.Threading.Tasks;
using Serilog;
using MotionServiceLib;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Base command for motion device operations
    /// </summary>
    public abstract class MotionDeviceCommand : Command<MotionKernel>
    {
        protected readonly string _deviceId;

        public MotionDeviceCommand(
            MotionKernel motionKernel,
            string deviceId,
            string name,
            string description,
            ILogger logger = null)
            : base(motionKernel, name, description, logger)
        {
            _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        }

        // Validate device is connected before executing
        protected void ValidateDeviceConnection()
        {
            if (!_context.IsDeviceConnected(_deviceId))
            {
                throw new InvalidOperationException($"Device {_deviceId} is not connected");
            }
        }
    }

    /// <summary>
    /// Command to move a device to a named position
    /// </summary>
    public class MoveToNamedPositionCommand : MotionDeviceCommand
    {
        private readonly string _positionName;

        public MoveToNamedPositionCommand(
            MotionKernel motionKernel,
            string deviceId,
            string positionName,
            ILogger logger = null)
            : base(
                motionKernel,
                deviceId,
                $"MoveToPosition-{deviceId}-{positionName}",
                $"Move device {deviceId} to position {positionName}",
                logger)
        {
            _positionName = positionName ?? throw new ArgumentNullException(nameof(positionName));
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            ValidateDeviceConnection();

            try
            {
                bool success = await _context.MoveToPositionAsync(_deviceId, _positionName);

                return success
                    ? CommandResult.Successful($"Successfully moved device {_deviceId} to position {_positionName}")
                    : CommandResult.Failed($"Failed to move device {_deviceId} to position {_positionName}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} to position {PositionName}", _deviceId, _positionName);
                return CommandResult.Failed(
                    $"Error moving device {_deviceId} to position {_positionName}: {ex.Message}",
                    ex
                );
            }
        }
    }

    /// <summary>
    /// Command to set device speed
    /// </summary>
    public class SetDeviceSpeedCommand : MotionDeviceCommand
    {
        private readonly double _speed;

        public SetDeviceSpeedCommand(
            MotionKernel motionKernel,
            string deviceId,
            double speed,
            ILogger logger = null)
            : base(
                motionKernel,
                deviceId,
                $"SetSpeed-{deviceId}-{speed}",
                $"Set speed for device {deviceId} to {speed}",
                logger)
        {
            _speed = speed;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            ValidateDeviceConnection();

            try
            {
                bool success = await _context.SetDeviceSpeedAsync(_deviceId, _speed);

                return success
                    ? CommandResult.Successful()
                    : CommandResult.Failed($"Failed to set speed to {_speed}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting device speed to {Speed}", _speed);
                return CommandResult.Failed(ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Command to stop a motion device
    /// </summary>
    public class StopDeviceCommand : MotionDeviceCommand
    {
        public StopDeviceCommand(
            MotionKernel motionKernel,
            string deviceId,
            ILogger logger = null)
            : base(
                motionKernel,
                deviceId,
                $"StopDevice-{deviceId}",
                $"Stop device {deviceId}",
                logger)
        {
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            ValidateDeviceConnection();

            try
            {
                bool success = await _context.StopDeviceAsync(_deviceId);

                return success
                    ? CommandResult.Successful()
                    : CommandResult.Failed($"Failed to stop device {_deviceId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping device {DeviceId}", _deviceId);
                return CommandResult.Failed(ex.Message, ex);
            }
        }
    }

    /// <summary>
    /// Command to home a motion device
    /// </summary>
    public class HomeDeviceCommand : MotionDeviceCommand
    {
        public HomeDeviceCommand(
            MotionKernel motionKernel,
            string deviceId,
            ILogger logger = null)
            : base(
                motionKernel,
                deviceId,
                $"HomeDevice-{deviceId}",
                $"Home device {deviceId}",
                logger)
        {
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            ValidateDeviceConnection();

            try
            {
                bool success = await _context.HomeDeviceAsync(_deviceId);

                return success
                    ? CommandResult.Successful()
                    : CommandResult.Failed($"Failed to home device {_deviceId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error homing device {DeviceId}", _deviceId);
                return CommandResult.Failed(ex.Message, ex);
            }
        }
    }
}