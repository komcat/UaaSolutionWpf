using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.Windows;

namespace UaaSolutionWpf.Services
{
    public class CameraGantryService : IDisposable
    {
        private readonly GantryMovementService _gantryService;
        private readonly DevicePositionMonitor _positionMonitor;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _movementLock = new SemaphoreSlim(1, 1);
        private readonly CameraSettingsManager _settingsManager;
        private CameraConversionSettings _conversionSettings;
        private bool _isMoving;
        private bool _disposed;

        // Movement limits
        private const double MaxRelativeMovement = 5.0; // Maximum movement in mm

        public event EventHandler<MovementStartedEventArgs> MovementStarted;
        public event EventHandler<MovementCompletedEventArgs> MovementCompleted;

        public CameraGantryService(
            GantryMovementService gantryService,
            DevicePositionMonitor positionMonitor, 
            ILogger logger)
        {
            _gantryService = gantryService ?? throw new ArgumentNullException(nameof(gantryService));
            _positionMonitor = positionMonitor ?? throw new ArgumentNullException(nameof(positionMonitor));

            _logger = logger?.ForContext<CameraGantryService>() ?? throw new ArgumentNullException(nameof(logger));

            // Initialize settings manager
            _settingsManager = new CameraSettingsManager(_logger);
            _conversionSettings = _settingsManager.LoadSettings();

            _logger.Information("CameraGantryService initialized with conversion factors: X={XFactor}, Y={YFactor}",
                _conversionSettings.PixelToMillimeterFactorX,
                _conversionSettings.PixelToMillimeterFactorY);
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
                double deltaYPixels = clickLocation.Y - imageCenter.Y; // Invert Y for standard coordinate system

                // Apply scale factor and convert to millimeters
                double deltaXmm = (deltaXPixels / scaleFactor) * _conversionSettings.PixelToMillimeterFactorX;
                double deltaYmm = (deltaYPixels / scaleFactor) * _conversionSettings.PixelToMillimeterFactorY;

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

                // Execute movement using absolute positioning
                await MoveToClickedPosition(deltaXmm, deltaYmm);

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

        private async Task MoveToClickedPosition(double deltaXmm, double deltaYmm)
        {
            try
            {
                // Get current gantry position using DevicePositionMonitor
                var currentPos = await _positionMonitor.GetCurrentPosition("gantry-main");

                // Calculate target absolute position
                double targetX = currentPos.X + deltaXmm;
                double targetY = currentPos.Y + deltaYmm;

                _logger.Debug("Moving gantry from ({CurrentX:F3}, {CurrentY:F3}) to ({TargetX:F3}, {TargetY:F3})",
                    currentPos.X, currentPos.Y, targetX, targetY);

                // Start both X and Y axis movements simultaneously
                var tasks = new List<Task>
                {
                    _gantryService.MoveRelativeAsync((int)GantryMovementService.Axis.X, deltaXmm),
                    _gantryService.MoveRelativeAsync((int)GantryMovementService.Axis.Y, deltaYmm)
                };

                // Wait for both movements to complete
                await Task.WhenAll(tasks);

                _logger.Debug("Gantry movement to ({TargetX:F3}, {TargetY:F3}) completed",
                    targetX, targetY);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during gantry movement. Delta X: {DeltaX:F3}mm, Delta Y: {DeltaY:F3}mm",
                    deltaXmm, deltaYmm);
                throw;
            }
        }
        private async Task MoveGantryRelative(double deltaXmm, double deltaYmm)
        {
            try
            {
                // Create tasks for both axis movements to execute concurrently
                var tasks = new List<Task>();

                // Start X axis movement if needed
                if (deltaXmm != 0)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await _gantryService.MoveRelativeAsync((int)GantryMovementService.Axis.X, deltaXmm);
                        _logger.Debug("X axis move completed with delta: {DeltaX}mm", deltaXmm);
                    }));
                }

                // Start Y axis movement if needed
                if (deltaYmm != 0)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await _gantryService.MoveRelativeAsync((int)GantryMovementService.Axis.Y, deltaYmm);
                        _logger.Debug("Y axis move completed with delta: {DeltaY}mm", deltaYmm);
                    }));
                }

                // Wait for all movements to complete
                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                    _logger.Debug("All axis movements completed simultaneously");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during gantry movement. X: {DeltaX}mm, Y: {DeltaY}mm",
                    deltaXmm, deltaYmm);
                throw;
            }
        }
        /// <summary>
        /// Shows a dialog to edit camera conversion settings
        /// </summary>
        /// <param name="owner">Owner window</param>
        /// <returns>True if settings were changed, false otherwise</returns>
        public bool ShowSettingsDialog(Window owner)
        {
            try
            {
                // Reload settings from file in case they were changed externally
                _conversionSettings = _settingsManager.LoadSettings();

                var dialog = new ConversionSettingsWindow(_conversionSettings, _settingsManager, _logger);
                dialog.Owner = owner;

                var result = dialog.ShowDialog();
                if (result.HasValue && result.Value)
                {
                    // Update current settings
                    _conversionSettings = dialog.Result;
                    _logger.Information("Camera conversion settings updated through dialog");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error showing camera settings dialog");
                MessageBox.Show(owner,
                    $"Error showing settings dialog: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
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