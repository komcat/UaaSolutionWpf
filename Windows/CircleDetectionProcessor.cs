using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using CircleDetectorLib;
using Serilog;

namespace UaaSolutionWpf.Windows
{
    /// <summary>
    /// Handles circle detection and visualization on cropped images
    /// </summary>
    public class CircleDetectionProcessor
    {
        private readonly EmguCvHough _circleDetector;
        private readonly ILogger _logger;
        private Window _displayWindow;
        private Image _imageControl;
        private TextBlock _infoTextBlock;

        public CircleDetectionProcessor(ILogger logger)
        {
            _circleDetector = new EmguCvHough();
            _logger = logger?.ForContext<CircleDetectionProcessor>() ??
                      Log.ForContext<CircleDetectionProcessor>();
        }

        /// <summary>
        /// Process the cropped image to detect circles and display results
        /// </summary>
        /// <param name="croppedImage">The cropped image to process</param>
        /// <param name="minRadius">Minimum radius for circle detection</param>
        /// <param name="maxRadius">Maximum radius for circle detection</param>
        /// <param name="cannyThreshold">Canny edge detection threshold</param>
        /// <param name="accumulatorThreshold">Hough transform accumulator threshold</param>
        /// <returns>Window containing the processed image with circles</returns>
        public Window ProcessAndDisplayCircles(
            BitmapSource croppedImage,
            int minRadius = 40,
            int maxRadius = 80,
            double cannyThreshold = 110,
            double accumulatorThreshold = 40)
        {
            try
            {
                // Create or get the display window
                EnsureDisplayWindow();

                _logger.Information("Converting image for processing");

                // Convert BitmapSource to Mat for EmguCV processing
                Mat imageMat = ConvertBitmapSourceToMat(croppedImage);

                _logger.Information("Detecting circles with parameters: MinRadius={0}, MaxRadius={1}, CannyThreshold={2}, AccumulatorThreshold={3}",
                    minRadius, maxRadius, cannyThreshold, accumulatorThreshold);

                // Detect circles
                CircleF[] circles = _circleDetector.DetectCircles(
                    imageMat,
                    minRadius,
                    maxRadius,
                    cannyThreshold,
                    accumulatorThreshold,
                    minDistBetweenCircles: Math.Min(minRadius * 2, 20));

                _logger.Information("Detected {0} circles", circles.Length);

                // Draw circles on the image
                Mat processedImage = _circleDetector.DrawCircles(
                    imageMat,
                    circles,
                    new MCvScalar(0, 0, 255), // Red color
                    2);  // Thickness

                // Convert back to BitmapSource for display
                BitmapSource resultImage = ConvertMatToBitmapSource(processedImage);

                // Display the processed image
                _imageControl.Source = resultImage;

                // Update info text
                string circleInfo = $"Detected {circles.Length} circles\n";
                foreach (CircleF circle in circles)
                {
                    circleInfo += $"Center: ({circle.Center.X:F1}, {circle.Center.Y:F1}), R: {circle.Radius:F1}\n";
                }
                _infoTextBlock.Text = circleInfo;

                // Update window title
                _displayWindow.Title = $"Circle Detection - {circles.Length} found";

                // Clean up
                imageMat.Dispose();
                processedImage.Dispose();

                return _displayWindow;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing image for circle detection");
                throw;
            }
        }

        /// <summary>
        /// Create the display window if it doesn't exist yet
        /// </summary>
        private void EnsureDisplayWindow()
        {
            if (_displayWindow == null)
            {
                _displayWindow = new Window
                {
                    Title = "Circle Detection",
                    Width = 250,
                    Height = 300, // Extra space for info text
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.CanResize,
                    SizeToContent = SizeToContent.Height
                };

                // Create a stack panel to hold the image and info
                StackPanel stackPanel = new StackPanel();

                // Create an Image control for the window
                _imageControl = new Image
                {
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(5)
                };

                // Create a text block for information
                _infoTextBlock = new TextBlock
                {
                    Margin = new Thickness(5),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                };

                // Add controls to the stack panel
                stackPanel.Children.Add(_imageControl);
                stackPanel.Children.Add(_infoTextBlock);

                // Set the stack panel as the window content
                _displayWindow.Content = stackPanel;

                // Handle window closing
                _displayWindow.Closed += (s, e) => {
                    _displayWindow = null;
                    _imageControl = null;
                    _infoTextBlock = null;
                };
            }

            // Make sure window is visible
            if (!_displayWindow.IsVisible)
            {
                _displayWindow.Show();
            }
        }

        /// <summary>
        /// Convert BitmapSource to Emgu CV Mat
        /// </summary>
        private Mat ConvertBitmapSourceToMat(BitmapSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            // Create a FormatConvertedBitmap to ensure we have a consistent pixel format
            FormatConvertedBitmap formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = source;
            formattedBitmap.DestinationFormat = PixelFormats.Bgr24; // Use a format EmguCV works well with
            formattedBitmap.EndInit();

            // Get the pixel data
            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int bytesPerPixel = (formattedBitmap.Format.BitsPerPixel + 7) / 8;
            int stride = width * bytesPerPixel;

            byte[] pixelData = new byte[height * stride];
            formattedBitmap.CopyPixels(pixelData, stride, 0);

            // Create a Mat of the proper size and type
            Mat mat = new Mat(height, width, DepthType.Cv8U, 3); // 3 channels for BGR

            // Copy the pixel data to the Mat
            Marshal.Copy(pixelData, 0, mat.DataPointer, pixelData.Length);

            return mat;
        }

        /// <summary>
        /// Convert Emgu CV Mat to BitmapSource
        /// </summary>
        private BitmapSource ConvertMatToBitmapSource(Mat image)
        {
            try
            {
                // Make sure we're working with 8-bit BGR
                Mat bgr = new Mat();
                if (image.NumberOfChannels == 1)
                {
                    CvInvoke.CvtColor(image, bgr, ColorConversion.Gray2Bgr);
                }
                else if (image.NumberOfChannels == 4)
                {
                    CvInvoke.CvtColor(image, bgr, ColorConversion.Bgra2Bgr);
                }
                else
                {
                    bgr = image.Clone();
                }

                // Get image data
                int width = bgr.Width;
                int height = bgr.Height;
                int channels = bgr.NumberOfChannels;
                int stride = width * channels;

                // Create buffer for pixel data
                byte[] pixelData = new byte[stride * height];

                // Copy Mat data to pixel buffer
                Marshal.Copy(bgr.DataPointer, pixelData, 0, pixelData.Length);

                // Create BitmapSource
                WriteableBitmap bitmap = new WriteableBitmap(
                    width, height, 96, 96, PixelFormats.Bgr24, null);

                bitmap.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    pixelData,
                    stride,
                    0);

                // Freeze for cross-thread usage
                bitmap.Freeze();

                // Clean up
                if (!bgr.Equals(image))
                {
                    bgr.Dispose();
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error converting Mat to BitmapSource");
                throw;
            }
        }
    }
}