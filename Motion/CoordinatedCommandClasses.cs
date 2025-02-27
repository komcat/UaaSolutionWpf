﻿using EzIIOLib;
using System;
using System.Collections.Generic;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Motion
{
    public enum CommandType
    {
        Motion,
        ImageCapture,
        Output,          // Direct IO control without validation
        Timer,
        WaitForInput,
        SlideMove        // Full slide control with validation
    }

    public class CoordinatedCommand
    {
        // Common properties
        public CommandType Type { get; set; }
        public int ExecutionOrder { get; set; }
        public bool WaitForCompletion { get; set; }
        public string Description { get; set; }  // For logging and debugging

        // Motion specific properties
        public string DeviceId { get; set; }     // e.g., "gantry-main", "hex-left"
        public string TargetPosition { get; set; }

        // IO specific properties
        public string DeviceName { get; set; }   // e.g., "IOBottom", "IOTop"
        public string PinName { get; set; }      // e.g., "UV_Head", "Dispenser_Head"
        public bool State { get; set; }          // true=set, false=clear

        // Timer specific properties
        public TimeSpan Duration { get; set; }

        // Input waiting properties
        public string InputDeviceName { get; set; }
        public string InputPinName { get; set; }
        public bool ExpectedState { get; set; }
        public TimeSpan? Timeout { get; set; }

        // Slide specific properties
        public string SlideId { get; set; }
        public SlidePosition TargetSlidePosition { get; set; }  // Changed from SlideState to SlidePosition
        public TimeSpan? SlideTimeout { get; set; }
        // Add property for image capture
        public string ImageCapturePrefix { get; set; }
        public static CoordinatedCommand CreateImageCaptureCommand(string prefix, int order, bool waitForComplete = true)
        {
            return new CoordinatedCommand
            {
                Type = CommandType.ImageCapture,
                ExecutionOrder = order,
                WaitForCompletion = waitForComplete,
                Description = $"Capture camera image with prefix {prefix}",
                ImageCapturePrefix = prefix
            };
        }
        // Factory methods for cleaner creation
        public static CoordinatedCommand CreateMotionCommand(string deviceId, string targetPosition, int order, bool waitForComplete = true)
        {
            return new CoordinatedCommand
            {
                Type = CommandType.Motion,
                DeviceId = deviceId,
                TargetPosition = targetPosition,
                ExecutionOrder = order,
                WaitForCompletion = waitForComplete,
                Description = $"Move {deviceId} to {targetPosition}"
            };
        }

        public static CoordinatedCommand CreateOutputCommand(string deviceName, string pinName, bool state, int order, bool waitForComplete = true)
        {
            return new CoordinatedCommand
            {
                Type = CommandType.Output,
                DeviceName = deviceName,
                PinName = pinName,
                State = state,
                ExecutionOrder = order,
                WaitForCompletion = waitForComplete,
                Description = $"Set {deviceName}.{pinName} to {(state ? "ON" : "OFF")}"
            };
        }

        public static CoordinatedCommand CreateSlideCommand(
               string slideId,
               SlidePosition targetSlidePosition,  // Changed parameter type
               int order,
               bool waitForComplete = true,
               TimeSpan? timeout = null)
        {
            return new CoordinatedCommand
            {
                Type = CommandType.SlideMove,
                SlideId = slideId,
                TargetSlidePosition = targetSlidePosition,
                ExecutionOrder = order,
                WaitForCompletion = waitForComplete,
                SlideTimeout = timeout,
                Description = $"Move slide {slideId} to {targetSlidePosition}"
            };
        }

        public static CoordinatedCommand CreateTimerCommand(TimeSpan duration, int order)
        {
            return new CoordinatedCommand
            {
                Type = CommandType.Timer,
                Duration = duration,
                ExecutionOrder = order,
                WaitForCompletion = true,  // Timers always wait for completion
                Description = $"Wait for {duration.TotalSeconds} seconds"
            };
        }

        public static CoordinatedCommand CreateWaitForInputCommand(
            string deviceName,
            string pinName,
            bool expectedState,
            int order,
            TimeSpan? timeout = null)
        {
            return new CoordinatedCommand
            {
                Type = CommandType.WaitForInput,
                InputDeviceName = deviceName,
                InputPinName = pinName,
                ExpectedState = expectedState,
                ExecutionOrder = order,
                WaitForCompletion = true,  // Input waiting always waits for completion
                Timeout = timeout,
                Description = $"Wait for {deviceName}.{pinName} to be {(expectedState ? "ON" : "OFF")}"
            };
        }
    }
}