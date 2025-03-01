using System;
using System.Threading.Tasks;
using System.Windows;
using MotionServiceLib;
using Serilog;

namespace UaaSolutionWpf
{
    /// <summary>
    /// Extension methods for integrating motion and vision systems
    /// </summary>
    public static class MotionVisionExtensions
    {
        // Default ID for the gantry device
        private const string DEFAULT_GANTRY_ID = "4";  // From your configuration, this appears to be the gantry ID

        // Calibration parameters for image-to-world conversion
        // These values should be determined through a proper calibration procedure
        private static readonly Point DefaultImageCenter = new Point(640, 512);  // Default image center (for 1280x1024 image)
        private static readonly double DefaultPixelsPerMm = 10.0;  // Default scale (10 pixels = 1mm)
        private static readonly double DefaultZHeight = 12.0;  // Default Z-height for movements

        /// <summary>
        /// Moves the gantry to a position corresponding to a clicked point in the camera image
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID (defaults to gantry)</param>
        /// <param name="clickPoint">The point that was clicked in the image (relative to image center)</param>
        /// <param name="logger">Optional logger</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveToImagePointAsync(
            this MotionKernel kernel,
            Point clickPoint,
            string deviceId = DEFAULT_GANTRY_ID,
            ILogger logger = null)
        {
            try
            {
                // Get world coordinates for the clicked point
                var worldPoint = ConvertImageToWorldCoordinates(clickPoint);

                logger?.Information("Moving to image point. Image: ({X}, {Y}) -> World: ({WorldX}, {WorldY})",
                    clickPoint.X, clickPoint.Y, worldPoint.X, worldPoint.Y);

                // Get current position to maintain Z height and other axes
                var currentPos = await kernel.GetCurrentPositionAsync(deviceId);
                if (currentPos == null)
                {
                    logger?.Error("Failed to get current position");
                    return false;
                }

                // Create target position (only changing X and Y)
                var targetPos = new Position
                {
                    X = worldPoint.X,
                    Y = worldPoint.Y,
                    Z = currentPos.Z,  // Maintain current Z height
                    U = currentPos.U,  // Maintain other axes if they exist
                    V = currentPos.V,
                    W = currentPos.W
                };

                // Move to target position
                return await kernel.MoveToAbsolutePositionAsync(deviceId, targetPos);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Error moving to image point");
                return false;
            }
        }

        /// <summary>
        /// Extends MotionKernel with a method to move to an absolute position
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="position">The target position</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveToAbsolutePositionAsync(
            this MotionKernel kernel,
            string deviceId,
            Position position)
        {
            // First check if we have a controller for this device
            if (!kernel.HasControllerForDevice(deviceId))
            {
                return false;
            }

            try
            {
                // Get the specific device
                var device = kernel.GetDevices().Find(d => d.Id == deviceId);
                if (device == null)
                {
                    return false;
                }

                // Create a new temporary named position
                string tempPositionName = $"TempVisionTarget_{DateTime.Now.Ticks}";

                // Teach this position
                await kernel.TeachPositionAsync(deviceId, tempPositionName, position);

                // Move to the position
                bool result = await kernel.MoveToPositionAsync(deviceId, tempPositionName);

                // Clean up by removing the temporary position
                // Note: This assumes there's a way to remove positions, which may need to be added

                return result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts image coordinates (pixels) to world coordinates (mm)
        /// </summary>
        /// <param name="imagePoint">The point in image coordinates, relative to center</param>
        /// <returns>The corresponding world coordinates in mm</returns>
        private static Point ConvertImageToWorldCoordinates(Point imagePoint)
        {
            // Convert from image coordinates to world coordinates
            // This is a simplified linear transformation
            // In a real system, you would need a proper calibration matrix

            double worldX = imagePoint.X / DefaultPixelsPerMm;
            double worldY = imagePoint.Y / DefaultPixelsPerMm;

            return new Point(worldX, worldY);
        }

        /// <summary>
        /// Sets the calibration parameters for image-to-world conversion
        /// </summary>
        /// <param name="imageCenter">The center point of the image in pixels</param>
        /// <param name="pixelsPerMm">The scale factor (pixels per mm)</param>
        /// <param name="zHeight">The default Z height for movements</param>
        public static void SetCalibrationParameters(Point imageCenter, double pixelsPerMm, double zHeight)
        {
            // This would normally update the private static fields
            // But since we can't modify those directly in an extension method example,
            // in a real implementation you would update your calibration parameters here
        }

        /// <summary>
        /// Creates and saves a calibration between image space and world space
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="calibrationPoints">Array of corresponding image-to-world point pairs</param>
        /// <param name="logger">Optional logger</param>
        /// <returns>True if calibration was successful</returns>
        public static bool CalibrateImageToWorldTransform(
            this MotionKernel kernel,
            string deviceId,
            (Point image, Point world)[] calibrationPoints,
            ILogger logger = null)
        {
            if (calibrationPoints == null || calibrationPoints.Length < 3)
            {
                logger?.Error("Calibration requires at least 3 point pairs");
                return false;
            }

            try
            {
                // In a real implementation, you would:
                // 1. Calculate a transformation matrix from the calibration points
                // 2. Save this matrix to be used for coordinate transformations
                // 3. Update the static parameters used by ConvertImageToWorldCoordinates

                // For this example, we'll just log the points
                logger?.Information("Calibration with {Count} points received", calibrationPoints.Length);
                for (int i = 0; i < calibrationPoints.Length; i++)
                {
                    logger?.Information("Calibration Point {Index}: Image({ImgX}, {ImgY}) -> World({WorldX}, {WorldY})",
                        i,
                        calibrationPoints[i].image.X, calibrationPoints[i].image.Y,
                        calibrationPoints[i].world.X, calibrationPoints[i].world.Y);
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Error during calibration");
                return false;
            }
        }
    }
}