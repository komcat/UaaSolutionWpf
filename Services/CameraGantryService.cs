using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Services
{
    public class CameraGantryService : IDisposable
    {
        private readonly GantryMovementService _gantryService;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _movementLock = new SemaphoreSlim(1, 1);
        private bool _isMoving;
        private bool _disposed;

        // Conversion factors
        private const double PixelToMillimeterFactorX = 0.00427;
        private const double PixelToMillimeterFactorY = 0.00427;

        // Movement limits
        private const double MaxRelativeMovement = 5.0; // Maximum movement in mm

        public event EventHandler<MovementStartedEventArgs> MovementStarted;
        public event EventHandler<MovementCompletedEventArgs> MovementCompleted;

        public CameraGantryService(GantryMovementService gantryService, ILogger logger)
        {
            _gantryService = gantryService ?? throw new ArgumentNullException(nameof(gantryService));
            _logger = logger?.ForContext<CameraGantryService>() ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleCameraClick(Point clickLocation, Point imageCenter, double scaleFactor)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CameraGantryService));
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

                // Calculate relative movement in pixels
                double deltaXPixels = clickLocation.X - imageCenter.X;
                double deltaYPixels = clickLocation.Y- imageCenter.Y; // Invert Y for standard coordinate system

                // Apply scale factor and convert to millimeters
                double deltaXmm = (deltaXPixels / scaleFactor) * PixelToMillimeterFactorX;
                double deltaYmm = (deltaYPixels / scaleFactor) * PixelToMillimeterFactorY;

                // Validate movement limits
                if (Math.Abs(deltaXmm) > MaxRelativeMovement || Math.Abs(deltaYmm) > MaxRelativeMovement)
                {
                    _logger.Warning(
                        "Requested movement exceeds safety limits. X: {DeltaX:F3}mm, Y: {DeltaY:F3}mm",
                        deltaXmm, deltaYmm);
                    return;
                }

                _logger.Information(
                    "Processing camera click - Relative movement X: {DeltaX:F3}mm, Y: {DeltaY:F3}mm",
                    deltaXmm, deltaYmm);

                // Notify movement started
                MovementStarted?.Invoke(this, new MovementStartedEventArgs(deltaXmm, deltaYmm));

                // Execute movements
                await MoveGantryRelative(deltaXmm, deltaYmm);

                // Notify movement completed
                MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(true));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing camera click");
                MovementCompleted?.Invoke(this, new MovementCompletedEventArgs(false, ex.Message));
                throw;
            }
            finally
            {
                _isMoving = false;
                _movementLock.Release();
            }
        }

        private async Task MoveGantryRelative(double deltaXmm, double deltaYmm)
        {
            try
            {
                // Move X axis first if needed
                if (deltaXmm != 0)
                {
                    await _gantryService.MoveRelativeAsync((int)GantryMovementService.Axis.X, deltaXmm);
                    _logger.Debug("X axis move completed with delta: {DeltaX}mm", deltaXmm);
                }

                // Move Y axis after X movement completes
                if (deltaYmm != 0)
                {
                    await _gantryService.MoveRelativeAsync((int)GantryMovementService.Axis.Y, deltaYmm);
                    _logger.Debug("Y axis move completed with delta: {DeltaY}mm", deltaYmm);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during gantry movement. X: {DeltaX}mm, Y: {DeltaY}mm",
                    deltaXmm, deltaYmm);
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _movementLock.Dispose();
                _disposed = true;
            }
        }
    }

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