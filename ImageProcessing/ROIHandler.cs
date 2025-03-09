using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Serilog;
using System.IO;

namespace UaaSolutionWpf
{
    /// <summary>
    /// Handles a Region of Interest (ROI) overlay on a camera image
    /// </summary>
    public class ROIHandler
    {
        private readonly Canvas _overlay;
        private readonly ILogger _logger;
        private readonly Rectangle _roiRectangle;
        private bool isDebug = false;
        // Default ROI size (in pixels) for cropping the actual image
        private readonly int _cropSizePixels = 500;

        // ROI position relative to the image
        private Point _roiCenter;

        /// <summary>
        /// Gets or sets whether the ROI is visible
        /// </summary>
        public bool IsVisible
        {
            get => _roiRectangle.Visibility == Visibility.Visible;
            set => _roiRectangle.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Gets the current ROI bounds in image coordinates
        /// </summary>
        public Rect ROIBounds
        {
            get
            {
                // Calculate the rectangle centered on the display
                return new Rect(
                    _roiCenter.X - _roiRectangle.Width / 2,
                    _roiCenter.Y - _roiRectangle.Height / 2,
                    _roiRectangle.Width,
                    _roiRectangle.Height);
            }
        }

        // Camera and display properties
        private Size _cameraResolution;
        private Size _displaySize;

        /// <summary>
        /// Creates a new ROI Handler for a specific canvas overlay
        /// </summary>
        /// <param name="overlay">The canvas to draw the ROI on</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cropSizePixels">Size in pixels of the cropped area (default 500)</param>
        public ROIHandler(Canvas overlay, ILogger logger, int cropSizePixels = 500)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _logger = logger?.ForContext<ROIHandler>() ?? Log.ForContext<ROIHandler>();
            _cropSizePixels = cropSizePixels;

            // Initialize the resolution values
            _cameraResolution = new Size(1280, 1024); // Default camera resolution
            _displaySize = new Size(_overlay.ActualWidth, _overlay.ActualHeight);

            // Create the ROI rectangle with orange border
            _roiRectangle = new Rectangle
            {
                Stroke = Brushes.Orange,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // Dashed line
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 165, 0)), // Slightly transparent orange
                Width = 300,  // Initial size, will be scaled based on display
                Height = 300, // Initial size, will be scaled based on display
                Visibility = Visibility.Collapsed
            };

            // Add to the canvas
            _overlay.Children.Add(_roiRectangle);

            // Hook up to the canvas size changed event to keep ROI centered
            _overlay.SizeChanged += Overlay_SizeChanged;

            _logger.Debug("ROI Handler initialized with crop size {CropSize}x{CropSize} pixels",
                _cropSizePixels, _cropSizePixels);
        }

        /// <summary>
        /// Updates the ROI position when the overlay size changes
        /// </summary>
        private void Overlay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateROIPosition();
        }

        /// <summary>
        /// Sets the camera resolution to adjust ROI scaling
        /// </summary>
        /// <param name="width">Camera width in pixels</param>
        /// <param name="height">Camera height in pixels</param>
        public void SetCameraResolution(int width, int height)
        {
            _cameraResolution = new Size(width, height);
            UpdateROIPosition();
        }

        /// <summary>
        /// Updates the ROI position to keep it centered, accounting for scaling
        /// </summary>
        public void UpdateROIPosition()
        {
            if (_overlay.ActualWidth <= 0 || _overlay.ActualHeight <= 0)
                return;

            _displaySize = new Size(_overlay.ActualWidth, _overlay.ActualHeight);

            // Calculate center position
            _roiCenter = new Point(_overlay.ActualWidth / 2, _overlay.ActualHeight / 2);

            // Calculate ROI display size based on scaling
            double scaleX = _overlay.ActualWidth / _cameraResolution.Width;
            double scaleY = _overlay.ActualHeight / _cameraResolution.Height;

            // Use the minimum scale to ensure ROI fits within the display
            double scale = Math.Min(scaleX, scaleY);

            // Calculate displayed ROI size - scale the crop size
            double displayRoiWidth = _cropSizePixels * scale;
            double displayRoiHeight = _cropSizePixels * scale;

            // Set the rectangle size
            _roiRectangle.Width = displayRoiWidth;
            _roiRectangle.Height = displayRoiHeight;

            // Position the rectangle
            Canvas.SetLeft(_roiRectangle, _roiCenter.X - displayRoiWidth / 2);
            Canvas.SetTop(_roiRectangle, _roiCenter.Y - displayRoiHeight / 2);

            if (isDebug)
            {
                _logger.Debug("ROI updated: Pixels={PixelSize}x{PixelSize}, Display={DisplayWidth:F1}x{DisplayHeight:F1}",
                    _cropSizePixels, _cropSizePixels, displayRoiWidth, displayRoiHeight);
            }
        }

        /// <summary>
        /// Shows the ROI rectangle
        /// </summary>
        public void Show()
        {
            IsVisible = true;
            UpdateROIPosition();
        }

        /// <summary>
        /// Hides the ROI rectangle
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
        }

        /// <summary>
        /// Toggles the visibility of the ROI
        /// </summary>
        public void ToggleVisibility()
        {
            IsVisible = !IsVisible;
            if (IsVisible)
            {
                UpdateROIPosition();
            }
        }

        /// <summary>
        /// Crops the provided image to the ROI
        /// </summary>
        /// <param name="image">The source image</param>
        /// <returns>A new cropped image, or null if cropping failed</returns>
        public WriteableBitmap CropToROI(WriteableBitmap image)
        {
            try
            {
                if (image == null)
                    return null;

                // Calculate the center point in the image
                int centerX = image.PixelWidth / 2;
                int centerY = image.PixelHeight / 2;

                // Create a ROI that's exactly _cropSizePixels x _cropSizePixels in the image
                Int32Rect sourceRect = new Int32Rect(
                    centerX - _cropSizePixels / 2, // Half the width on each side
                    centerY - _cropSizePixels / 2, // Half the height on each side
                    _cropSizePixels,
                    _cropSizePixels
                );

                // Make sure the rectangle is within the image bounds
                sourceRect.X = Math.Max(0, Math.Min(sourceRect.X, image.PixelWidth - 1));
                sourceRect.Y = Math.Max(0, Math.Min(sourceRect.Y, image.PixelHeight - 1));
                sourceRect.Width = Math.Min(sourceRect.Width, image.PixelWidth - sourceRect.X);
                sourceRect.Height = Math.Min(sourceRect.Height, image.PixelHeight - sourceRect.Y);

                _logger.Debug("Cropping ROI: Source={X},{Y},{Width},{Height}, Image={ImageWidth}x{ImageHeight}",
                    sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height,
                    image.PixelWidth, image.PixelHeight);

                if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
                {
                    _logger.Warning("Invalid crop rectangle: {Rect}", sourceRect);
                    return null;
                }

                try
                {
                    // Create a cropped bitmap source
                    CroppedBitmap croppedBitmapSource = new CroppedBitmap(image, sourceRect);

                    // Create a new writeable bitmap with the ROI size
                    WriteableBitmap croppedImage = new WriteableBitmap(
                        sourceRect.Width,
                        sourceRect.Height,
                        image.DpiX,
                        image.DpiY,
                        image.Format,
                        image.Palette);

                    // Copy the pixels
                    croppedImage.Lock();
                    try
                    {
                        croppedBitmapSource.CopyPixels(
                            new Int32Rect(0, 0, sourceRect.Width, sourceRect.Height),
                            croppedImage.BackBuffer,
                            croppedImage.BackBufferStride * sourceRect.Height,
                            croppedImage.BackBufferStride);

                        croppedImage.AddDirtyRect(new Int32Rect(0, 0, croppedImage.PixelWidth, croppedImage.PixelHeight));
                    }
                    finally
                    {
                        croppedImage.Unlock();
                    }

                    _logger.Information("Image cropped to ROI: {Width}x{Height}",
                        croppedImage.PixelWidth, croppedImage.PixelHeight);
                    return croppedImage;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during image cropping: {Message}", ex.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cropping image to ROI");
                return null;
            }
        }
    }
}