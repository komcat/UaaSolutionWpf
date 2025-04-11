using Basler.Pylon;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UaaSolutionWpf
{
    public class CameraStats
    {
        private readonly object _lock = new object();
        private int _frameCount;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _currentFps;
        private long _lastProcessingTime;
        private Size _resolution;

        public double CurrentFps
        {
            get { lock (_lock) return _currentFps; }
            private set { lock (_lock) _currentFps = value; }
        }

        public long ProcessingTime
        {
            get { lock (_lock) return _lastProcessingTime; }
            set { lock (_lock) _lastProcessingTime = value; }
        }

        public Size Resolution
        {
            get { lock (_lock) return _resolution; }
            set { lock (_lock) _resolution = value; }
        }

        public void UpdateFrameCount()
        {
            lock (_lock)
            {
                _frameCount++;
                var elapsed = (DateTime.Now - _lastFpsUpdate).TotalSeconds;
                if (elapsed >= 1.0)
                {
                    CurrentFps = _frameCount / elapsed;
                    _frameCount = 0;
                    _lastFpsUpdate = DateTime.Now;
                }
            }
        }

        public string GetStatsString()
        {
            lock (_lock)
            {
                return $"FPS: {CurrentFps:F1} | Resolution: {Resolution.Width}x{Resolution.Height} | Processing Time: {ProcessingTime}ms";
            }
        }
    }

    public class CameraManagerWpf : IDisposable
    {
        private Camera camera = null;
        private readonly PixelDataConverter converter;
        private readonly Image imageControl;
        private readonly object imageLock = new object();
        private WriteableBitmap currentImage;
        private readonly ILogger _logger;
        private readonly BlockingCollection<IGrabResult> imageQueue;
        private readonly CancellationTokenSource cancellationTokenSource;
        private Task processingTask;
        private volatile bool isProcessing = false;
        private float currentZoom = 1.0f;
        private Size originalImageSize;
        private readonly CameraStats cameraStats;

        private const int MaxFrameRate = 30;
        private const int MaxWidth = 1281;
        private const int MaxHeight = 1025;
        private DateTime _lastFrameProcessedTime = DateTime.MinValue;
        private readonly TimeSpan _frameThrottleInterval = TimeSpan.FromMilliseconds(33); // ~30fps max

        public event EventHandler<Point> ImageClicked;
        public event EventHandler<string> StatsUpdated;
        // Add event for image updates
        public event EventHandler<ImageUpdatedEventArgs> ImageUpdated;

        // Add property to access current image
        public WriteableBitmap CurrentImage
        {
            get
            {
                lock (imageLock)
                {
                    return currentImage?.Clone();
                }
            }
        }
        // Add property for raw image data
        private byte[] currentImageData;
        public byte[] CurrentImageData
        {
            get
            {
                lock (imageLock)
                {
                    return currentImageData?.Clone() as byte[];
                }
            }
        }
        public CameraManagerWpf(Image imageControl, ILogger logger)
        {
            this.imageControl = imageControl ?? throw new ArgumentNullException(nameof(imageControl));
            this._logger = logger?.ForContext<CameraManagerWpf>() ?? throw new ArgumentNullException(nameof(logger));

            converter = new PixelDataConverter
            {
                OutputPixelFormat = PixelType.BGRA8packed
            };

            imageQueue = new BlockingCollection<IGrabResult>();
            cancellationTokenSource = new CancellationTokenSource();
            cameraStats = new CameraStats();

            imageControl.MouseDown += ImageControl_MouseDown;
            processingTask = Task.Run(ProcessImagesAsync, cancellationTokenSource.Token);
        }

        public bool ConnectToCamera()
        {
            try
            {
                camera = new Camera();
                camera.CameraOpened += Configuration.AcquireContinuous;
                camera.Open();

                // Configure auto exposure
                //if (camera.Parameters.Contains(PLCamera.ExposureAuto))
                //{
                //    camera.Parameters[PLCamera.ExposureAuto].SetValue(PLCamera.ExposureAuto.Continuous);
                //    _logger.Information("Set Exposure Auto to Continuous");
                //}
                //else
                //{
                //    _logger.Warning("Camera does not support ExposureAuto parameter");
                //}

                //// Configure auto gain
                //if (camera.Parameters.Contains(PLCamera.GainAuto))
                //{
                //    camera.Parameters[PLCamera.GainAuto].SetValue(PLCamera.GainAuto.Continuous);
                //    _logger.Information("Set Gain Auto to Continuous");
                //}
                //else
                //{
                //    _logger.Warning("Camera does not support GainAuto parameter");
                //}

                // Configure other camera parameters
                ConfigureCamera();

                camera.StreamGrabber.ImageGrabbed += OnImageGrabbed;

                _logger.Information("Camera connected successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing camera");
                return false;
            }
        }
        private void ConfigureCamera()
        {
            try
            {
                if (camera?.IsOpen == true)
                {
                    // Set pixel format to Mono8 or RGB8 based on your needs
                    camera.Parameters[PLCamera.PixelFormat].SetValue(PLCamera.PixelFormat.Mono8);

                    // Set acquisition frame rate if supported
                    if (camera.Parameters.Contains(PLCamera.AcquisitionFrameRate))
                    {
                        camera.Parameters[PLCamera.AcquisitionFrameRate].SetValue(5);
                    }

                    // Set Region of Interest if needed
                    if (camera.Parameters.Contains(PLCamera.Width))
                    {
                        camera.Parameters[PLCamera.Width].SetValue(MaxWidth);
                        camera.Parameters[PLCamera.Height].SetValue(MaxHeight);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error configuring camera parameters");
                throw;
            }
        }
        public string GetCurrentFps()
        {
            if (cameraStats != null)
            {
                return $"{cameraStats.CurrentFps:F1}";
            }
            return "0.0";
        }
        public void StartLiveView()
        {
            if (camera == null || !camera.IsOpen)
            {
                _logger.Warning("Camera is not initialized or opened. Cannot start live view.");
                return;
            }

            try
            {
                camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);

                if (!camera.StreamGrabber.IsGrabbing)
                {
                    camera.StreamGrabber.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByStreamGrabber);
                    _logger.Information("StreamGrabber started");
                }

                isProcessing = true;
                _logger.Information("Live view started");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting live view");
                throw;
            }
        }

        public void StopLiveView()
        {
            try
            {
                isProcessing = false;

                if (camera?.StreamGrabber != null && camera.StreamGrabber.IsGrabbing)
                {
                    camera.StreamGrabber.Stop();
                }

                while (imageQueue.TryTake(out _)) { }

                _logger.Information("Live view stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping live view");
                throw;
            }
        }


        public WriteableBitmap GetCurrentFrame(int timeout = 2000)
        {
            _logger.Information("Attempting to get current frame from camera");

            if (camera == null || !camera.IsOpen)
            {
                _logger.Warning("Cannot get frame: Camera is not initialized or opened");
                return null;
            }

            try
            {
                // Check if we're already in live view mode
                bool isInLiveView = camera.StreamGrabber.IsGrabbing && isProcessing;

                if (isInLiveView)
                {
                    // If we're in live view, just return the current image
                    lock (imageLock)
                    {
                        if (currentImage != null)
                        {
                            _logger.Information("Returning current live view image");
                            return currentImage.Clone();
                        }
                    }

                    // If somehow we don't have a current image yet, wait for one
                    using (var frameReceived = new ManualResetEventSlim(false))
                    {
                        WriteableBitmap capturedFrame = null;

                        EventHandler<ImageUpdatedEventArgs> updateHandler = null;
                        updateHandler = (sender, e) =>
                        {
                            capturedFrame = e.Image.Clone();
                            frameReceived.Set();
                        };

                        try
                        {
                            ImageUpdated += updateHandler;

                            if (frameReceived.Wait(timeout))
                            {
                                _logger.Information("Successfully captured frame from live view");
                                return capturedFrame;
                            }
                            else
                            {
                                _logger.Warning("Timeout waiting for live view frame");
                                return null;
                            }
                        }
                        finally
                        {
                            ImageUpdated -= updateHandler;
                        }
                    }
                }
                else
                {
                    // If not in live view, fall back to Snap method
                    return Snap(timeout);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting current frame");
                return null;
            }
        }

        private void OnImageGrabbed(object sender, ImageGrabbedEventArgs e)
        {
            try
            {
                // Implement frame skipping to prevent queue buildup
                if (DateTime.Now - _lastFrameProcessedTime < _frameThrottleInterval)
                {
                    e.GrabResult?.Dispose();
                    return;
                }

                _lastFrameProcessedTime = DateTime.Now;

                using (IGrabResult grabResult = e.GrabResult)
                {
                    if (grabResult.GrabSucceeded && isProcessing)
                    {
                        // Add to queue, but drop frames if queue gets too large
                        if (imageQueue.Count < 5)
                        {
                            imageQueue.Add(grabResult.Clone());
                        }
                        else
                        {
                            _logger.Warning("Dropping frame - queue too large ({0} items)", imageQueue.Count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in OnImageGrabbed");
            }
        }

        private async Task ProcessImagesAsync()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Use TryTake with timeout instead of Take (which blocks)
                    if (imageQueue.TryTake(out IGrabResult grabResult, 100, cancellationTokenSource.Token))
                    {
                        using (grabResult)
                        {
                            if (grabResult.GrabSucceeded)
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();

                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        UpdateImage(grabResult);

                                        // Update statistics
                                        cameraStats.UpdateFrameCount();
                                        cameraStats.Resolution = new Size(grabResult.Width, grabResult.Height);

                                        // Trigger the stats updated event
                                        StatsUpdated?.Invoke(this, cameraStats.GetStatsString());
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.Error(ex, "Error updating image on UI thread");
                                    }
                                }, DispatcherPriority.Background);

                                sw.Stop();
                                cameraStats.ProcessingTime = sw.ElapsedMilliseconds;
                            }
                        }
                    }
                    else
                    {
                        // If no image was available, yield to other threads
                        await Task.Delay(1);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing image");
                    await Task.Delay(100); // Delay to prevent tight loop on errors
                }
            }
        }
        private void UpdateImage(IGrabResult grabResult)
        {
            lock (imageLock)
            {
                if (currentImage == null ||
                    currentImage.PixelWidth != grabResult.Width ||
                    currentImage.PixelHeight != grabResult.Height)
                {
                    currentImage = new WriteableBitmap(
                        grabResult.Width,
                        grabResult.Height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null);

                    originalImageSize = new Size(grabResult.Width, grabResult.Height);
                }

                // Store raw image data
                if (currentImageData == null || currentImageData.Length != grabResult.PayloadSize)
                {
                    currentImageData = new byte[grabResult.PayloadSize];
                }
                Marshal.Copy(grabResult.PixelDataPointer, currentImageData, 0, (int)grabResult.PayloadSize);

                currentImage.Lock();
                try
                {
                    converter.Convert(
                        currentImage.BackBuffer,
                        currentImage.BackBufferStride * grabResult.Height,
                        grabResult);
                    currentImage.AddDirtyRect(
                        new Int32Rect(0, 0, grabResult.Width, grabResult.Height));
                }
                finally
                {
                    currentImage.Unlock();
                }

                imageControl.Source = currentImage;

                // Raise event with image data - use existing image objects to avoid excessive allocations
                ImageUpdated?.Invoke(this, new ImageUpdatedEventArgs
                {
                    Image = currentImage,
                    RawData = currentImageData,
                    Width = grabResult.Width,
                    Height = grabResult.Height,
                    Format = grabResult.PixelTypeValue
                });
            }
        }


        /// <summary>
        /// Captures a single image from the camera
        /// </summary>
        /// <param name="timeout">Timeout in milliseconds for waiting for a frame</param>
        /// <returns>A WriteableBitmap containing the captured image, or null if no image could be captured</returns>
        public WriteableBitmap Snap(int timeout = 1000)
        {
            _logger.Information("Attempting to snap a single image from camera");

            if (camera == null || !camera.IsOpen)
            {
                _logger.Warning("Cannot snap image: Camera is not initialized or opened");
                return null;
            }

            try
            {
                // If the camera is not already grabbing, start it
                bool wasGrabbing = false;
                if (!camera.StreamGrabber.IsGrabbing)
                {
                    _logger.Information("Starting camera grab for single image");
                    camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.SingleFrame);
                    camera.StreamGrabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);
                }
                else
                {
                    wasGrabbing = true;
                }

                // Use a semaphore to signal when we have an image
                using (SemaphoreSlim signal = new SemaphoreSlim(0, 1))
                {
                    WriteableBitmap snappedImage = null;

                    // Set up a one-time event handler to capture the image
                    EventHandler<ImageGrabbedEventArgs> handler = null;
                    handler = (sender, e) =>
                    {
                        try
                        {
                            using (IGrabResult grabResult = e.GrabResult)
                            {
                                if (grabResult.GrabSucceeded)
                                {
                                    lock (imageLock)
                                    {
                                        // Create new WriteableBitmap if needed
                                        if (snappedImage == null ||
                                            snappedImage.PixelWidth != grabResult.Width ||
                                            snappedImage.PixelHeight != grabResult.Height)
                                        {
                                            snappedImage = new WriteableBitmap(
                                                grabResult.Width,
                                                grabResult.Height,
                                                96,
                                                96,
                                                PixelFormats.Bgra32,
                                                null);
                                        }

                                        // Convert the image
                                        snappedImage.Lock();
                                        try
                                        {
                                            converter.Convert(
                                                snappedImage.BackBuffer,
                                                snappedImage.BackBufferStride * grabResult.Height,
                                                grabResult);
                                            snappedImage.AddDirtyRect(
                                                new Int32Rect(0, 0, grabResult.Width, grabResult.Height));
                                        }
                                        finally
                                        {
                                            snappedImage.Unlock();
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error processing snapped image");
                        }
                        finally
                        {
                            // Signal that we have processed the image
                            signal.Release();
                        }
                    };

                    try
                    {
                        // Register for grabbed images
                        camera.StreamGrabber.ImageGrabbed += handler;

                        // Execute the software trigger if available
                        if (!wasGrabbing)
                        {
                            if (camera.Parameters.Contains(PLCamera.TriggerSoftware))
                            {
                                camera.Parameters[PLCamera.TriggerSoftware].Execute();
                                _logger.Information("Software trigger executed");
                            }
                        }

                        // Wait for the image with timeout
                        if (signal.Wait(timeout))
                        {
                            _logger.Information("Successfully captured snap image");
                            return snappedImage;
                        }
                        else
                        {
                            _logger.Warning("Timeout waiting for snap image");
                            return null;
                        }
                    }
                    finally
                    {
                        // Unregister the event handler
                        camera.StreamGrabber.ImageGrabbed -= handler;

                        // If we started grabbing for this snap, stop it
                        if (!wasGrabbing && camera.StreamGrabber.IsGrabbing)
                        {
                            camera.StreamGrabber.Stop();

                            // If we were in continuous mode before, restore it
                            if (isProcessing)
                            {
                                camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);
                                camera.StreamGrabber.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByStreamGrabber);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error snapping image from camera");
                return null;
            }


        }


        /// <summary>
        /// Saves the current camera image to a file
        /// </summary>
        /// <param name="filePath">Full path where to save the image</param>
        /// <param name="fileFormat">Image format (png, jpeg, bmp, etc.)</param>
        /// <param name="takeNewSnapshot">If true, captures a new image; if false, uses the current image</param>
        /// <returns>True if the save was successful, false otherwise</returns>
        public bool SaveImageToFile(string filePath, string fileFormat = "png", bool takeNewSnapshot = true)
        {
            try
            {
                _logger.Information("Attempting to save camera image to file: {FilePath}", filePath);

                // Use the hybrid method to get a frame
                WriteableBitmap imageToSave = GetCurrentFrame(5000);

                if (imageToSave == null)
                {
                    _logger.Error("Failed to capture image for saving");
                    return false;
                }

                // Create encoder based on specified format
                BitmapEncoder encoder;
                switch (fileFormat.ToLower())
                {
                    case "png":
                        encoder = new PngBitmapEncoder();
                        break;
                    case "jpeg":
                    case "jpg":
                        encoder = new JpegBitmapEncoder { QualityLevel = 90 }; // High quality JPEG
                        break;
                    case "bmp":
                        encoder = new BmpBitmapEncoder();
                        break;
                    case "tiff":
                    case "tif":
                        encoder = new TiffBitmapEncoder();
                        break;
                    default:
                        encoder = new PngBitmapEncoder();
                        break;
                }

                // Create directory if it doesn't exist
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Information("Created directory: {Directory}", directory);
                }

                // Convert WritableBitmap to BitmapFrame and save to file
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    BitmapFrame frame = BitmapFrame.Create(imageToSave);
                    encoder.Frames.Add(frame);
                    encoder.Save(fileStream);
                }

                _logger.Information("Successfully saved image to {FilePath}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving image to file: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Captures a single image from the camera and displays it in the specified Image control
        /// </summary>
        /// <param name="targetImageControl">The Image control to display the captured image</param>
        /// <param name="timeout">Timeout in milliseconds for waiting for a frame</param>
        /// <returns>A WriteableBitmap containing the captured image, or null if no image could be captured</returns>
        public WriteableBitmap SnapToImageControl(Image targetImageControl, int timeout = 1000)
        {
            _logger.Information("Attempting to snap a single image from camera to specified Image control");

            if (targetImageControl == null)
            {
                _logger.Warning("Cannot snap image: Target Image control is null");
                return null;
            }

            WriteableBitmap snappedImage = Snap(timeout);

            if (snappedImage != null)
            {
                // Update the specified image control on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    targetImageControl.Source = snappedImage;
                });

                _logger.Information("Successfully updated target Image control with snapped image");
            }

            return snappedImage;
        }

        private void ImageControl_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (currentImage == null) return;

            var position = e.GetPosition(imageControl);
            float scaleX = (float)currentImage.PixelWidth / (float)imageControl.ActualWidth;
            float scaleY = (float)currentImage.PixelHeight / (float)imageControl.ActualHeight;

            int centerX = (int)imageControl.ActualWidth / 2;
            int centerY = (int)imageControl.ActualHeight / 2;

            int relativeX = (int)((position.X - centerX) * scaleX);
            int relativeY = (int)((position.Y - centerY) * scaleY);

            ImageClicked?.Invoke(this, new Point(relativeX, relativeY));
            _logger.Information("Image clicked at relative position: ({RelativeX}, {RelativeY})", relativeX, relativeY);
        }

        public string GetCameraInfo()
        {
            if (camera == null || !camera.IsOpen)
            {
                return "Camera is not connected.";
            }

            var info = new System.Text.StringBuilder();
            info.AppendLine("Camera Information:");
            info.AppendLine($"Model: {camera.CameraInfo[CameraInfoKey.ModelName]}");
            info.AppendLine($"Serial Number: {camera.CameraInfo[CameraInfoKey.SerialNumber]}");
            info.AppendLine($"Vendor: {camera.CameraInfo[CameraInfoKey.VendorName]}");
            info.AppendLine($"User ID: {camera.CameraInfo[CameraInfoKey.UserDefinedName]}");
            info.AppendLine($"Current FPS: {cameraStats.CurrentFps:F1}");
            info.AppendLine($"Resolution: {cameraStats.Resolution.Width}x{cameraStats.Resolution.Height}");

            return info.ToString();
        }

        public void SetZoom(float zoomFactor)
        {
            currentZoom = zoomFactor;
            if (imageControl != null)
            {
                var transform = new ScaleTransform(currentZoom, currentZoom);
                imageControl.RenderTransform = transform;
            }
        }

        public void Dispose()
        {
            try
            {
                cancellationTokenSource.Cancel();
                StopLiveView();

                if (camera != null)
                {
                    if (camera.IsOpen)
                    {
                        camera.Close();
                    }
                    camera.Dispose();
                }

                converter?.Dispose();
                imageQueue.Dispose();
                cancellationTokenSource.Dispose();
                currentImage = null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in Dispose");
                throw;
            }
        }
    }

    public class ImageUpdatedEventArgs : EventArgs
    {
        public WriteableBitmap Image { get; set; }
        public byte[] RawData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public PixelType Format { get; set; }
    }
}