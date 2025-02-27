﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    public partial class BaslerDisplayViewControl : UserControl, IDisposable
    {
        private CameraManagerWpf _cameraManager;
        public CameraManagerWpf CameraManager
        {
            get => _cameraManager;
            protected set => _cameraManager = value;
        }

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
            _cameraManager = new CameraManagerWpf(cameraDisplay, _logger);

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

                // Ensure overlay visibility matches checkbox state
                if (chkShowOverlay != null)
                {
                    cameraOverlay.Visibility = chkShowOverlay.IsChecked == true ?
                        Visibility.Visible : Visibility.Collapsed;
                }

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
                if (_cameraManager.ConnectToCamera())
                {
                    btnConnect.IsEnabled = false;
                    btnStartLive.IsEnabled = true;
                    zoomSlider.IsEnabled = true;
                    statusText.Text = "Camera Connected";

                    string cameraInfo = _cameraManager.GetCameraInfo();
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
                _cameraManager.StartLiveView();
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
                _cameraManager.StopLiveView();
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
            if (_cameraManager != null)
            {
                _cameraManager.SetZoom((float)e.NewValue);

                // Update overlay scale to match image zoom
                if (cameraOverlay != null)
                {
                    cameraOverlay.RenderTransform = cameraDisplay.RenderTransform;
                }
            }
        }

        private void chkShowOverlay_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (cameraOverlay != null)
            {
                cameraOverlay.Visibility = chkShowOverlay.IsChecked == true ?
                    Visibility.Visible : Visibility.Collapsed;
                _logger.Information("Overlay visibility changed to: {State}",
                    chkShowOverlay.IsChecked == true ? "Visible" : "Hidden");
            }
        }



        private void btnCameraSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Call the method in CameraOverlayControl
                if (cameraOverlay != null)
                {
                    cameraOverlay.ShowConversionSettings();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error showing camera settings");
                MessageBox.Show($"Error showing camera settings: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public void Dispose()
        {
            try
            {
                _cameraManager?.Dispose();
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