using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    public partial class BaslerDisplayViewControl : UserControl
    {
        private CameraManagerWpf cameraManager;
        private readonly ILogger _logger;

        public event EventHandler<CameraConnectionEventArgs> CameraConnected;
        public event EventHandler<CameraConnectionEventArgs> CameraDisconnected;
        public event EventHandler<LiveViewEventArgs> LiveViewStarted;
        public event EventHandler<LiveViewEventArgs> LiveViewStopped;

        public BaslerDisplayViewControl()
        {
            InitializeComponent();

            // Initialize Serilog if not already initialized
            if (Log.Logger is ILogger)
            {
                _logger = Log.Logger.ForContext<BaslerDisplayViewControl>();
            }
            else
            {
                _logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Debug()
                    .WriteTo.File("logs/camera_log_.txt", rollingInterval: RollingInterval.Day)
                    .CreateLogger();
            }

            // Initialize camera manager
            cameraManager = new CameraManagerWpf(cameraDisplay, _logger);

            // Register for image size changes
            cameraDisplay.SizeChanged += CameraDisplay_SizeChanged;

            // Disable controls initially
            btnStartLive.IsEnabled = false;
            btnStopLive.IsEnabled = false;
            zoomSlider.IsEnabled = false;
        }

        private void CameraDisplay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (cameraOverlay != null)
            {
                // Update overlay to match image size
                cameraOverlay.Width = cameraDisplay.ActualWidth;
                cameraOverlay.Height = cameraDisplay.ActualHeight;

                // Update the image container if needed
                if (cameraDisplay.Source is BitmapSource bitmapSource)
                {
                    imageContainer.Width = bitmapSource.PixelWidth;
                    imageContainer.Height = bitmapSource.PixelHeight;
                }
            }
        }

        public double GetCurrentScaleFactor()
        {
            if (cameraDisplay.Source is BitmapSource bitmapSource && bitmapSource.PixelWidth > 0)
            {
                return cameraDisplay.ActualWidth / bitmapSource.PixelWidth;
            }
            return 1.0;
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cameraManager.ConnectToCamera())
                {
                    btnConnect.IsEnabled = false;
                    btnStartLive.IsEnabled = true;
                    zoomSlider.IsEnabled = true;
                    statusText.Text = "Camera Connected";

                    string cameraInfo = cameraManager.GetCameraInfo();
                    _logger.Information(cameraInfo);

                    CameraConnected?.Invoke(this, new CameraConnectionEventArgs
                    {
                        CameraInfo = cameraInfo,
                        IsConnected = true
                    });
                }
                else
                {
                    statusText.Text = "Failed to connect to camera";
                    _logger.Error("Failed to connect to camera");

                    CameraConnected?.Invoke(this, new CameraConnectionEventArgs
                    {
                        IsConnected = false,
                        ErrorMessage = "Failed to connect to camera"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to camera");
                CameraConnected?.Invoke(this, new CameraConnectionEventArgs
                {
                    IsConnected = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        private void btnStartLive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                cameraManager.StartLiveView();
                btnStartLive.IsEnabled = false;
                btnStopLive.IsEnabled = true;
                statusText.Text = "Live view active";

                LiveViewStarted?.Invoke(this, new LiveViewEventArgs { IsActive = true });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting live view");
                LiveViewStarted?.Invoke(this, new LiveViewEventArgs
                {
                    IsActive = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        private void btnStopLive_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                cameraManager.StopLiveView();
                btnStartLive.IsEnabled = true;
                btnStopLive.IsEnabled = false;
                statusText.Text = "Live view stopped";

                LiveViewStopped?.Invoke(this, new LiveViewEventArgs { IsActive = false });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping live view");
                LiveViewStopped?.Invoke(this, new LiveViewEventArgs
                {
                    IsActive = true,
                    ErrorMessage = ex.Message
                });
            }
        }

        private void zoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (cameraManager != null)
            {
                cameraManager.SetZoom((float)e.NewValue);

                // Update overlay scale to match image zoom
                if (cameraOverlay != null)
                {
                    cameraOverlay.RenderTransform = cameraDisplay.RenderTransform;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                cameraManager?.Dispose();
                _logger.Information("Camera control disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing camera manager");
            }
        }
    }

    public class CameraConnectionEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string CameraInfo { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class LiveViewEventArgs : EventArgs
    {
        public bool IsActive { get; set; }
        public string ErrorMessage { get; set; }
    }
}