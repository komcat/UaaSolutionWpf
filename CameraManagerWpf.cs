using Basler.Pylon;
using Serilog;
using System;
using System.Collections.Concurrent;
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
                if (camera.Parameters.Contains(PLCamera.ExposureAuto))
                {
                    camera.Parameters[PLCamera.ExposureAuto].SetValue(PLCamera.ExposureAuto.Continuous);
                    _logger.Information("Set Exposure Auto to Continuous");
                }
                else
                {
                    _logger.Warning("Camera does not support ExposureAuto parameter");
                }

                // Configure auto gain
                if (camera.Parameters.Contains(PLCamera.GainAuto))
                {
                    camera.Parameters[PLCamera.GainAuto].SetValue(PLCamera.GainAuto.Continuous);
                    _logger.Information("Set Gain Auto to Continuous");
                }
                else
                {
                    _logger.Warning("Camera does not support GainAuto parameter");
                }

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
                        camera.Parameters[PLCamera.AcquisitionFrameRate].SetValue(MaxFrameRate);
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

        private void OnImageGrabbed(object sender, ImageGrabbedEventArgs e)
        {
            try
            {
                using (IGrabResult grabResult = e.GrabResult)
                {
                    if (grabResult.GrabSucceeded && isProcessing)
                    {
                        imageQueue.Add(grabResult.Clone());
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
            while (!imageQueue.IsCompleted)
            {
                try
                {
                    IGrabResult grabResult = await Task.Run(() => imageQueue.Take());
                    using (grabResult)
                    {
                        if (grabResult.GrabSucceeded)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
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
                                    currentImageData = new byte[grabResult.PayloadSize];
                                    Marshal.Copy(grabResult.PixelDataPointer, currentImageData, 0, (int)grabResult.PayloadSize);

                                    currentImage.Lock();
                                    converter.Convert(currentImage.BackBuffer, currentImage.BackBufferStride * grabResult.Height, grabResult);
                                    currentImage.AddDirtyRect(new Int32Rect(0, 0, grabResult.Width, grabResult.Height));
                                    currentImage.Unlock();

                                    imageControl.Source = currentImage;

                                    // Raise event with image data
                                    ImageUpdated?.Invoke(this, new ImageUpdatedEventArgs
                                    {
                                        Image = currentImage.Clone(),
                                        RawData = currentImageData.Clone() as byte[],
                                        Width = grabResult.Width,
                                        Height = grabResult.Height,
                                        Format = grabResult.PixelTypeValue
                                    });
                                }
                            }, DispatcherPriority.Render);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing image");
                }
            }
        }

        private void UpdateImage(IGrabResult grabResult)
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