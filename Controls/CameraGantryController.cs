using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using MotionServiceLib;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Manages the coordination between camera image clicks and gantry movement,
    /// handling the conversion from image pixels to real-world coordinates.
    /// </summary>
    public class CameraGantryController : IDisposable
    {
        private readonly MotionKernel _motionKernel;
        private readonly string _gantryDeviceId;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _movementLock = new SemaphoreSlim(1, 1);
        private readonly CameraSettings _settings;
        private bool _isMoving;
        private bool _disposed;

        // Movement limits and defaults
        private const double MaxRelativeMovement = 10.0; // Maximum movement in mm for safety
        private const double DefaultPixelToMmFactorX = 0.00427; // Default pixel to mm conversion factor X
        private const double DefaultPixelToMmFactorY = 0.00427; // Default pixel to mm conversion factor Y

        // Constants for different gantry movement modes
        public enum MoveMode
        {
            /// <summary>
            /// Move directly to the clicked point (no offsets)
            /// </summary>
            Direct,

            /// <summary>
            /// Move to keep the clicked point centered in the view
            /// </summary>
            Center,

            /// <summary>
            /// Move the gantry relative to the clicked position
            /// </summary>
            Relative
        }

        // Events
        public event EventHandler<MovementStartedEventArgs> MovementStarted;
        public event EventHandler<MovementCompletedEventArgs> MovementCompleted;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="motionKernel">The motion kernel for controlling devices</param>
        /// <param name="gantryDeviceId">The ID of the gantry device to control</param>
        /// <param name="logger">Logger for recording operations</param>
        public CameraGantryController(
            MotionKernel motionKernel,
            string gantryDeviceId,
            ILogger logger = null)
        {
            _motionKernel = motionKernel ?? throw new ArgumentNullException(nameof(motionKernel));
            _gantryDeviceId = string.IsNullOrEmpty(gantryDeviceId) ? throw new ArgumentNullException(nameof(gantryDeviceId)) : gantryDeviceId;
            _logger = logger?.ForContext<CameraGantryController>() ?? Log.ForContext<CameraGantryController>();

            // Load or create default settings
            _settings = LoadSettings();

            _logger.Information("CameraGantryController initialized for device {DeviceId} with conversion factors: X={XFactor}, Y={YFactor}",
                _gantryDeviceId, _settings.PixelToMmFactorX, _settings.PixelToMmFactorY);
        }

        /// <summary>
        /// Handles a camera image click and converts it to gantry movement
        /// </summary>
        /// <param name="clickPoint">The point clicked in the image (in pixels)</param>
        /// <param name="imageCenter">The center point of the image (in pixels)</param>
        /// <param name="imageSize">The total size of the image (in pixels)</param>
        /// <param name="scaleFactor">The scale factor of the displayed image</param>
        /// <param name="moveMode">The movement mode to use</param>
        /// <returns>A task representing the gantry movement operation</returns>
        public async Task HandleImageClickAsync(
            Point clickPoint,
            Point imageCenter,
            Size imageSize,
            double scaleFactor = 1.0,
            MoveMode moveMode = MoveMode.Center)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CameraGantryController));
            }

            if (_isMoving)
            {
                _logger.Warning("Movement already in progress - ignoring click");
                return;
            }

            try
            {
                await _movementLock.WaitAsync();
                _isMoving = true;

                // Calculate relative movement based on the selected mode
                (double deltaXmm, double deltaYmm) = CalculateMovementDistance(clickPoint, imageCenter, imageSize, scaleFactor, moveMode);

                // Validate movement limits
                if (Math.Abs(deltaXmm) > MaxRelativeMovement || Math.Abs(deltaYmm) > MaxRelativeMovement)
                {
                    _logger.Warning(
                        "Requested movement exceeds safety limits. X: {DeltaX:F3}mm, Y: {DeltaY:F3}mm",
                        deltaXmm, deltaYmm);

                    // Notify that movement was cancelled due to safety limits
                    MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(false, "Movement exceeds safety limits"));
                    return;
                }

                _logger.Information(
                    "Processing camera click at {ClickPoint} - Relative movement X: {DeltaX:F3}mm, Y: {DeltaY:F3}mm",
                    clickPoint, deltaXmm, deltaYmm);

                // Notify movement started
                MovementStarted?.Invoke(this, new MovementStartedEventArgs(deltaXmm, deltaYmm));

                // Execute movement
                await ExecuteGantryMovementAsync(deltaXmm, deltaYmm);

                // Notify movement completed
                MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(true));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing camera click");
                MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(false, ex.Message));
            }
            finally
            {
                _isMoving = false;
                _movementLock.Release();
            }
        }

        /// <summary>
        /// Calculates the movement distance in mm based on the click position and movement mode
        /// </summary>
        private (double deltaXmm, double deltaYmm) CalculateMovementDistance(
            Point clickPoint,
            Point imageCenter,
            Size imageSize,
            double scaleFactor,
            MoveMode moveMode)
        {
            // Calculate pixel distance from center or origin based on the mode
            double deltaXPixels, deltaYPixels;

            switch (moveMode)
            {
                case MoveMode.Center:
                    // In center mode, move so that the clicked point becomes the center
                    deltaXPixels = imageCenter.X - clickPoint.X;
                    deltaYPixels = imageCenter.Y - clickPoint.Y; // Invert Y for standard coordinate system
                    break;

                case MoveMode.Direct:
                    // In direct mode, just move to the clicked position
                    deltaXPixels = clickPoint.X - (imageSize.Width / 2);
                    deltaYPixels = clickPoint.Y - (imageSize.Height / 2);
                    break;

                case MoveMode.Relative:
                    // In relative mode, move relative to the clicked point
                    deltaXPixels = clickPoint.X - imageCenter.X;
                    deltaYPixels = clickPoint.Y - imageCenter.Y;
                    break;

                default:
                    throw new ArgumentException($"Unsupported move mode: {moveMode}");
            }

            // Apply scale factor and convert to millimeters
            double deltaXmm = (deltaXPixels / scaleFactor) * _settings.PixelToMmFactorX;
            double deltaYmm = (deltaYPixels / scaleFactor) * _settings.PixelToMmFactorY;

            return (deltaXmm, deltaYmm);
        }

        /// <summary>
        /// Executes the gantry movement using the MotionKernel
        /// </summary>
        private async Task ExecuteGantryMovementAsync(double deltaXmm, double deltaYmm)
        {
            try
            {
                // Get current position
                var currentPosition = await _motionKernel.GetCurrentPositionAsync(_gantryDeviceId);
                if (currentPosition == null)
                {
                    throw new InvalidOperationException($"Failed to get current position for device {_gantryDeviceId}");
                }

                // Prepare relative movement array (X, Y, Z, U, V, W)
                // We're only changing X and Y, leaving others at 0
                double[] relativeMove = new double[6];
                relativeMove[0] = deltaXmm;  // X axis
                relativeMove[1] = deltaYmm;  // Y axis

                // Execute the movement
                bool success = await _motionKernel.MoveRelativeAsync(_gantryDeviceId, relativeMove);
                if (!success)
                {
                    throw new Exception($"Failed to move gantry device {_gantryDeviceId}");
                }

                _logger.Debug("Gantry movement completed successfully. Delta X: {DeltaX:F3}mm, Delta Y: {DeltaY:F3}mm",
                    deltaXmm, deltaYmm);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during gantry movement. Delta X: {DeltaX:F3}mm, Delta Y: {DeltaY:F3}mm",
                    deltaXmm, deltaYmm);
                throw;
            }
        }

        /// <summary>
        /// Sets new calibration factors for the pixel-to-mm conversion
        /// </summary>
        /// <param name="pixelToMmFactorX">X-axis factor (mm per pixel)</param>
        /// <param name="pixelToMmFactorY">Y-axis factor (mm per pixel)</param>
        public void SetCalibrationFactors(double pixelToMmFactorX, double pixelToMmFactorY)
        {
            if (pixelToMmFactorX <= 0 || pixelToMmFactorY <= 0)
            {
                throw new ArgumentException("Calibration factors must be positive values");
            }

            _settings.PixelToMmFactorX = pixelToMmFactorX;
            _settings.PixelToMmFactorY = pixelToMmFactorY;
            SaveSettings(_settings);

            _logger.Information("Updated calibration factors: X={XFactor}, Y={YFactor}",
                pixelToMmFactorX, pixelToMmFactorY);
        }

        /// <summary>
        /// Gets the current calibration factors
        /// </summary>
        public (double pixelToMmFactorX, double pixelToMmFactorY) GetCalibrationFactors()
        {
            return (_settings.PixelToMmFactorX, _settings.PixelToMmFactorY);
        }

        /// <summary>
        /// Executes a test movement for calibration purposes
        /// </summary>
        /// <param name="relativeMove">The relative movement array [X,Y,Z,U,V,W]</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> ExecuteTestMovementAsync(double[] relativeMove)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CameraGantryController));
            }

            if (_isMoving)
            {
                _logger.Warning("Movement already in progress - cannot execute test movement");
                return false;
            }

            try
            {
                await _movementLock.WaitAsync();
                _isMoving = true;

                double deltaXmm = relativeMove[0];
                double deltaYmm = relativeMove[1];

                _logger.Information(
                    "Executing test movement - Relative movement X: {DeltaX:F3}mm, Y: {DeltaY:F3}mm",
                    deltaXmm, deltaYmm);

                // Notify movement started
                MovementStarted?.Invoke(this, new MovementStartedEventArgs(deltaXmm, deltaYmm));

                // Execute the movement
                bool success = await _motionKernel.MoveRelativeAsync(_gantryDeviceId, relativeMove);

                if (!success)
                {
                    _logger.Warning("Test movement failed for device {DeviceId}", _gantryDeviceId);
                    MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(false, "Movement command failed"));
                    return false;
                }

                // Notify movement completed
                MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(true));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing test movement");
                MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(false, ex.Message));
                return false;
            }
            finally
            {
                _isMoving = false;
                _movementLock.Release();
            }
        }

        /// <summary>
        /// Loads settings from the application configuration
        /// </summary>
        private CameraSettings LoadSettings()
        {
            try
            {
                // TODO: Implement loading from proper settings storage
                // For now, return default settings
                return new CameraSettings
                {
                    PixelToMmFactorX = DefaultPixelToMmFactorX,
                    PixelToMmFactorY = DefaultPixelToMmFactorY
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading camera settings, using defaults");
                return new CameraSettings
                {
                    PixelToMmFactorX = DefaultPixelToMmFactorX,
                    PixelToMmFactorY = DefaultPixelToMmFactorY
                };
            }
        }

        /// <summary>
        /// Saves settings to the application configuration
        /// </summary>
        private void SaveSettings(CameraSettings settings)
        {
            try
            {
                // TODO: Implement saving to proper settings storage
                _logger.Debug("Saved camera settings (not implemented)");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving camera settings");
            }
        }

        /// <summary>
        /// Releases resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _movementLock.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Internal settings class
        /// </summary>
        private class CameraSettings
        {
            public double PixelToMmFactorX { get; set; }
            public double PixelToMmFactorY { get; set; }
        }
    }

    /// <summary>
    /// Event args for when a movement operation starts
    /// </summary>
    public class MovementStartedEventArgs : EventArgs
    {
        public double DeltaXmm { get; }
        public double DeltaYmm { get; }

        public MovementStartedEventArgs(double deltaXmm, double deltaYmm)
        {
            DeltaXmm = deltaXmm;
            DeltaYmm = deltaYmm;
        }
    }

    /// <summary>
    /// Event args for when a movement operation completes
    /// </summary>
    public class MovementCompletedEventArgs : EventArgs
    {
        public bool Success { get; }
        public string ErrorMessage { get; }

        public MovementCompletedEventArgs(bool success, string errorMessage = null)
        {
            Success = success;
            ErrorMessage = errorMessage;
        }
    }
}