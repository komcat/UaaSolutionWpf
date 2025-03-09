using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using CircleDetectorLib;
using Serilog;

namespace UaaSolutionWpf.Windows
{
    /// <summary>
    /// Interaction logic for ImageProcWindow.xaml
    /// </summary>
    public partial class ImageProcWindow : Window
    {
        private EmguCvHough _circleDetector;
        private EmguCvPrepration _imageProcessor;
        private Mat _originalImage;
        private Mat _processedImage;
        private string _currentImagePath;
        private CameraManagerWpf _cameraManager;
        private readonly ILogger _logger;
        private bool _autoProcessing = true;

        // Fields for tracking display updates
        private DateTime _lastUpdateTime = DateTime.Now;
        private int _updateCount = 0;
        private readonly DispatcherTimer _statsUpdateTimer;
        private double _currentUpdateFrequency = 0;
        private readonly object _updateLock = new object();

        public ImageProcWindow()
        {
            // Configure Serilog for logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/vision_motion.log",
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Get a contextualized logger
            _logger = Log.ForContext<ImageProcWindow>();

            InitializeComponent();

            // Initialize the circle detector and image processor
            _circleDetector = new EmguCvHough();
            _imageProcessor = new EmguCvPrepration();

            // Initialize camera manager
            _logger.Information("Initializing camera manager");
            _cameraManager = new CameraManagerWpf(DisplayImage, _logger);

            // Subscribe to events
            _cameraManager.ImageUpdated += CameraManager_ImageUpdated;
            // Setup timer for updating statistics
            _statsUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statsUpdateTimer.Tick += StatsUpdateTimer_Tick;
            _statsUpdateTimer.Start();

            // Subscribe to window closing event for cleanup
            this.Closed += ImageProcWindow_Closed;

            // Initialize status display
            UpdateStatusText("Initializing...");

            // Try to connect to camera
            _logger.Information("Attempting to connect to camera");
            bool connected = _cameraManager.ConnectToCamera();

            if (connected)
            {
                _logger.Information("Camera connected successfully, starting live view");
                _cameraManager.StartLiveView();
                UpdateStatusText("Camera connected, streaming live view");
            }
            else
            {
                _logger.Warning("Failed to connect to camera, image processing will use loaded files only");
                MessageBox.Show("Could not connect to camera. You can still process images from files.",
                              "Camera Connection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateStatusText("Camera not connected, using file input only");
            }
        }

        private void StatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            // Calculate and update the display update frequency
            lock (_updateLock)
            {
                TimeSpan elapsed = DateTime.Now - _lastUpdateTime;
                if (elapsed.TotalSeconds >= 1)
                {
                    _currentUpdateFrequency = _updateCount / elapsed.TotalSeconds;
                    _updateCount = 0;
                    _lastUpdateTime = DateTime.Now;
                }
            }

            // Update the UI with the current statistics
            UpdateFrequencyTextBlock.Text = $"{_currentUpdateFrequency:F1} fps";

            // Get camera resolution if available
            if (_cameraManager != null && DisplayImage.Source is BitmapSource bitmapSource)
            {
                ResolutionTextBlock.Text = $"{bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}";
            }

            // Get processing time from camera stats if available
            if (_cameraManager != null)
            {
                string fps = _cameraManager.GetCurrentFps();
                if (!string.IsNullOrEmpty(fps))
                {
                    UpdateFrequencyTextBlock.Text = $"{fps} fps";
                }
            }
        }

        private void CameraManager_ImageUpdated(object sender, ImageUpdatedEventArgs e)
        {
            // Don't clone the image here - use the reference
            // If you need image processing, do it on a background thread

            // Update the frame counter
            lock (_updateLock)
            {
                _updateCount++;
            }

            // If auto-processing is enabled, process the image
            if (_autoProcessing)
            {
                //Task.Run(() => ProcessImageAsync(e.Image, e.RawData));
            }

            // Update resolution display
            Dispatcher.Invoke(() =>
            {
                ResolutionTextBlock.Text = $"{e.Width}x{e.Height}";
            });
        }

        private void UpdateStatusText(string status)
        {
            // Update the status text on the UI thread
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = status;
            });
        }

        private void ImageProcWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                _logger.Information("Cleaning up resources...");

                // Stop the stats update timer
                _statsUpdateTimer.Stop();

                // Dispose of camera manager
                _cameraManager?.Dispose();

                // Dispose of Mat objects
                _originalImage?.Dispose();
                _processedImage?.Dispose();

                _logger.Information("Resources cleaned up successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cleanup");
            }
        }



        private void OneShootProcessButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.Information("Processing image...");
            // Start timing
            var processingStopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Get image from the DisplayImage control
                BitmapSource displayedImage = DisplayImage.Source as BitmapSource;

                if (displayedImage == null)
                {
                    _logger.Warning("No image available to process");
                    MessageBox.Show("No image available to process.", "No Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Calculate center point to crop around
                int centerX = displayedImage.PixelWidth / 2;
                int centerY = displayedImage.PixelHeight / 2;

                // Define crop area (400x400 centered in the image)
                int cropWidth = 400;
                int cropHeight = 400;

                // Calculate crop bounds, ensuring they don't go outside the image
                int cropX = Math.Max(0, centerX - cropWidth / 2);
                int cropY = Math.Max(0, centerY - cropHeight / 2);

                // Adjust width and height if they would go beyond the image bounds
                cropWidth = Math.Min(cropWidth, displayedImage.PixelWidth - cropX);
                cropHeight = Math.Min(cropHeight, displayedImage.PixelHeight - cropY);

                // Create a cropped BitmapSource
                CroppedBitmap croppedImage = new CroppedBitmap(
                    displayedImage,
                    new System.Windows.Int32Rect(cropX, cropY, cropWidth, cropHeight));

                // Get values from the UI
                int minRadius = (int)MinRadiusSlider.Value;
                int maxRadius = (int)MaxRadiusSlider.Value;
                double cannyThreshold = CannySlider.Value;
                double accumulatorThreshold = AccumSlider.Value;

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

                // Display the processed image in the top-right view
                ProcessedImage.Source = resultImage;

                // Update the results text box
                UpdateResultsText(circles);

                // Also show the edge detection image in the bottom-left view
                Mat grayImage = new Mat();
                if (imageMat.NumberOfChannels == 3)
                {
                    CvInvoke.CvtColor(imageMat, grayImage, ColorConversion.Bgr2Gray);
                }
                else
                {
                    grayImage = imageMat.Clone();
                }

                // Apply Gaussian blur to reduce noise
                Mat blurredImage = new Mat();
                CvInvoke.GaussianBlur(grayImage, blurredImage, new System.Drawing.Size(5, 5), 1.5);
                grayImage.Dispose();

                // Apply Canny edge detection
                Mat edgeImage = new Mat();
                CvInvoke.Canny(blurredImage, edgeImage, 50, 150);
                blurredImage.Dispose();

                // Convert edge image to BitmapSource for display
                BitmapSource edgeBitmapSource = ConvertMatToBitmapSource(edgeImage);
                EdgeImage.Source = edgeBitmapSource;

                // Now handle the full image with circle overlay for the bottom right view
                if (circles.Length > 0)
                {
                    // Convert the original full image to Mat
                    Mat fullImageMat = ConvertBitmapSourceToMat(displayedImage);

                    // Transform the circles from ROI coordinates to full image coordinates
                    CircleF[] transformedCircles = new CircleF[circles.Length];
                    for (int i = 0; i < circles.Length; i++)
                    {
                        CircleF circle = circles[i];
                        // Adjust center coordinates by adding the crop offset
                        float adjustedX = circle.Center.X + cropX;
                        float adjustedY = circle.Center.Y + cropY;
                        transformedCircles[i] = new CircleF(new PointF(adjustedX, adjustedY), circle.Radius);
                    }

                    // Draw circles on the full image
                    Mat fullImageWithCircles = _circleDetector.DrawCircles(
                        fullImageMat,
                        transformedCircles,
                        new MCvScalar(0, 0, 255), // Red color
                        2); // Thickness

                    // Also draw a rectangle to show the ROI area
                    CvInvoke.Rectangle(
                        fullImageWithCircles,
                        new System.Drawing.Rectangle(cropX, cropY, cropWidth, cropHeight),
                        new MCvScalar(0, 255, 0), // Green color
                        2); // Thickness

                    // Convert back to BitmapSource for display
                    BitmapSource fullResultImage = ConvertMatToBitmapSource(fullImageWithCircles);
                    CircleDetectionImage.Source = fullResultImage;

                    // Clean up
                    fullImageMat.Dispose();
                    fullImageWithCircles.Dispose();
                }
                else
                {
                    // If no circles were found, just display the original image with the ROI rectangle
                    Mat fullImageMat = ConvertBitmapSourceToMat(displayedImage);

                    // Draw a rectangle to show the ROI area
                    CvInvoke.Rectangle(
                        fullImageMat,
                        new System.Drawing.Rectangle(cropX, cropY, cropWidth, cropHeight),
                        new MCvScalar(0, 255, 0), // Green color
                        2); // Thickness

                    // Convert back to BitmapSource for display
                    BitmapSource fullResultImage = ConvertMatToBitmapSource(fullImageMat);
                    CircleDetectionImage.Source = fullResultImage;

                    // Clean up
                    fullImageMat.Dispose();
                }

                // Clean up
                imageMat.Dispose();
                processedImage.Dispose();
                edgeImage.Dispose();

                // Stop timing and get elapsed milliseconds
                processingStopwatch.Stop();
                long processingTimeMs = processingStopwatch.ElapsedMilliseconds;

                // Update processing time display
                ProcessingTimeTextBlock.Text = $"{processingTimeMs} ms";

                _logger.Information("Image cropped and processed for circle detection. Processing time: {0} ms", processingTimeMs);
                UpdateStatusText($"Processed image: Found {circles.Length} circles. Processing time: {processingTimeMs} ms");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing and cropping image");
                MessageBox.Show($"Error processing image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void UpdateResultsText(CircleF[] circles)
        {
            // Create result information for the text box
            string results = $"Detected {circles.Length} circles:\n";
            for (int i = 0; i < circles.Length; i++)
            {
                CircleF circle = circles[i];
                results += $"Circle {i + 1}: Center=({circle.Center.X:F1}, {circle.Center.Y:F1}), Radius={circle.Radius:F1}\n";
            }

            ResultsTextBox.Text = results;
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
            formattedBitmap.DestinationFormat = System.Windows.Media.PixelFormats.Bgr24; // Use a format EmguCV works well with
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
            System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, mat.DataPointer, pixelData.Length);

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
                System.Runtime.InteropServices.Marshal.Copy(bgr.DataPointer, pixelData, 0, pixelData.Length);

                // Create BitmapSource
                WriteableBitmap bitmap = new WriteableBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);

                bitmap.WritePixels(
                    new System.Windows.Int32Rect(0, 0, width, height),
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