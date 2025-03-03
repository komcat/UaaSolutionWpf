using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using MotionServiceLib;
using EzIIOLib;
using UaaSolutionWpf.Commands;

namespace UaaSolutionWpf
{
    /// <summary>
    /// Utility class to build command sequences for common operations
    /// </summary>
    public class CommandBuilder
    {
        private readonly MotionKernel _motionKernel;
        private readonly MultiDeviceManager _deviceManager;
        private readonly PneumaticSlideManager _slideManager;
        private readonly ILogger _logger;

        public CommandBuilder(
            MotionKernel motionKernel,
            MultiDeviceManager deviceManager,
            PneumaticSlideManager slideManager = null,
            ILogger logger = null)
        {
            _motionKernel = motionKernel ?? throw new ArgumentNullException(nameof(motionKernel));
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _slideManager = slideManager; // Optional
            _logger = logger ?? Log.ForContext<CommandBuilder>();
        }

        /// <summary>
        /// Create a sequence to move a device to a position and set an output
        /// </summary>
        public CommandSequence CreateMoveAndSetOutputSequence(
            string deviceId, string positionName,
            string ioDeviceName, string pinName, bool outputState,
            TimeSpan? delayBetween = null)
        {
            var sequence = new CommandSequence(
                $"MoveAndSetOutput-{positionName}-{pinName}",
                $"Move to {positionName} and set {pinName} to {(outputState ? "on" : "off")}",
                _logger);

            sequence.AddCommand(new MoveToNamedPositionCommand(_motionKernel, deviceId, positionName, _logger));

            if (delayBetween.HasValue && delayBetween.Value > TimeSpan.Zero)
            {
                sequence.AddCommand(new DelayCommand(delayBetween.Value, _logger));
            }

            sequence.AddCommand(new SetOutputPinCommand(_deviceManager, ioDeviceName, pinName, outputState, _logger));

            return sequence;
        }

        /// <summary>
        /// Create a sequence to set multiple outputs in sequence
        /// </summary>
        public CommandSequence CreateMultiOutputSequence(
            string ioDeviceName,
            List<(string PinName, bool State)> outputSettings,
            TimeSpan? delayBetween = null)
        {
            var sequence = new CommandSequence(
                "MultiOutputSequence",
                $"Set multiple outputs on {ioDeviceName}",
                _logger);

            foreach (var (pinName, state) in outputSettings)
            {
                sequence.AddCommand(new SetOutputPinCommand(_deviceManager, ioDeviceName, pinName, state, _logger));

                if (delayBetween.HasValue && delayBetween.Value > TimeSpan.Zero)
                {
                    sequence.AddCommand(new DelayCommand(delayBetween.Value, _logger));
                }
            }

            return sequence;
        }

        /// <summary>
        /// Create a sequence for dispense operation with UV curing
        /// </summary>
        public CommandSequence CreateDispenseSequence(
            string gantryId, string dispensePositionName,
            string ioDeviceName, string dispensePinName, string uvPinName,
            TimeSpan dispenseTime, TimeSpan uvTime, TimeSpan cooldownTime)
        {
            var sequence = new CommandSequence(
                "DispenseWithUV",
                "Dispense material and cure with UV",
                _logger);

            // Step 1: Move to dispense position
            sequence.AddCommand(new MoveToNamedPositionCommand(_motionKernel, gantryId, dispensePositionName, _logger));

            // Step 2: Start dispensing
            sequence.AddCommand(new SetOutputPinCommand(_deviceManager, ioDeviceName, dispensePinName, true, _logger));

            // Step 3: Wait for dispense time
            sequence.AddCommand(new DelayCommand(dispenseTime, _logger));

            // Step 4: Stop dispensing
            sequence.AddCommand(new SetOutputPinCommand(_deviceManager, ioDeviceName, dispensePinName, false, _logger));

            // Step 5: Start UV curing
            sequence.AddCommand(new SetOutputPinCommand(_deviceManager, ioDeviceName, uvPinName, true, _logger));

            // Step 6: Wait for UV cure time
            sequence.AddCommand(new DelayCommand(uvTime, _logger));

            // Step 7: Stop UV curing
            sequence.AddCommand(new SetOutputPinCommand(_deviceManager, ioDeviceName, uvPinName, false, _logger));

            // Step 8: Wait for cooldown
            sequence.AddCommand(new DelayCommand(cooldownTime, _logger));

            return sequence;
        }

        /// <summary>
        /// Create a sequence for operating a pneumatic slide with position validation
        /// </summary>
        public CommandSequence CreatePneumaticSlideOperationSequence(
            string slideName, bool extend, TimeSpan timeout)
        {
            if (_slideManager == null)
            {
                throw new InvalidOperationException("PneumaticSlideManager is not available");
            }

            var sequence = new CommandSequence(
                $"Slide-{slideName}-{(extend ? "Extend" : "Retract")}",
                $"{(extend ? "Extend" : "Retract")} pneumatic slide {slideName}",
                _logger);

            // Step 1: Operate the slide
            sequence.AddCommand(new PneumaticSlideCommand(_slideManager, slideName, extend, 5000, _logger));

            // Step 2: Wait for a bit to let the slide move
            sequence.AddCommand(new DelayCommand(timeout, _logger));

            // Step 3: Log completion
            sequence.AddCommand(new LogMessageCommand(
                $"Pneumatic slide {slideName} {(extend ? "extension" : "retraction")} completed",
                false,
                _logger));

            return sequence;
        }

        /// <summary>
        /// Create a sequence for gripping operation
        /// </summary>
        public CommandSequence CreateGripperSequence(
            string gripperName, bool grip,
            string motionDeviceId = null, string positionName = null)
        {
            var sequence = new CommandSequence(
                $"{gripperName}-{(grip ? "Grip" : "Release")}",
                $"{(grip ? "Grip" : "Release")} with {gripperName}",
                _logger);

            // Step 1: Move to position if specified
            if (!string.IsNullOrEmpty(motionDeviceId) && !string.IsNullOrEmpty(positionName))
            {
                sequence.AddCommand(new MoveToNamedPositionCommand(_motionKernel, motionDeviceId, positionName, _logger));
                sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(500), _logger));
            }

            // Step 2: Set gripper state
            sequence.AddCommand(new SetOutputPinCommand(_deviceManager, "IOBottom", gripperName, grip, _logger));

            // Step 3: Wait for gripper to actuate
            sequence.AddCommand(new DelayCommand(TimeSpan.FromMilliseconds(500), _logger));

            return sequence;
        }
    }
}