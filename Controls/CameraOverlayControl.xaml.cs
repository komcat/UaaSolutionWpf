using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Controls
{
    public partial class CameraOverlayControl : UserControl
    {
        private Line _horizontalCenterLine;
        private Line _verticalCenterLine;
        private Path _mouseCrosshair;
        private TextBlock _coordinateDisplay;
        private Point _imageCenter;
        private CameraGantryService _gantryService;
        private ILogger _logger;
        private bool _isEnabled = true;
        public event EventHandler<ClickLocationEventArgs> LocationClicked;

        public CameraOverlayControl()
        {
            InitializeComponent();
            SetupOverlayElements();

            // Register events
            _overlayCanvas.MouseMove += OnMouseMove;
            _overlayCanvas.MouseLeave += OnMouseLeave;
            _overlayCanvas.MouseDown += OnMouseDown;
            SizeChanged += OnSizeChanged;
        }

        public void Initialize(CameraGantryService gantryService, ILogger logger)
        {
            _gantryService = gantryService ?? throw new ArgumentNullException(nameof(gantryService));
            _logger = logger?.ForContext<CameraOverlayControl>();

            // Subscribe to gantry service events
            _gantryService.MovementStarted += OnGantryMovementStarted;
            _gantryService.MovementCompleted += OnGantryMovementCompleted;
        }

        private void OnGantryMovementStarted(object sender, MovementStartedEventArgs e)
        {
            _isEnabled = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Visual feedback that movement is in progress
                _overlayCanvas.Cursor = Cursors.Wait;
                _coordinateDisplay.Text = $"Moving...\nX: {e.DeltaXmm:F3}mm\nY: {e.DeltaYmm:F3}mm";
                _coordinateDisplay.Visibility = Visibility.Visible;
            });
        }

        private void OnGantryMovementCompleted(object sender, MovementCompletedEventArgs e)
        {
            _isEnabled = true;
            Application.Current.Dispatcher.Invoke(() =>
            {
                _overlayCanvas.Cursor = Cursors.Cross;
                if (!e.Success)
                {
                    _coordinateDisplay.Text = "Movement failed!";
                    _coordinateDisplay.Foreground = Brushes.Red;
                    _logger?.Error("Gantry movement failed: {Error}", e.ErrorMessage);
                }
                else
                {
                    _coordinateDisplay.Visibility = Visibility.Collapsed;
                    _coordinateDisplay.Foreground = Brushes.White;
                }
            });
        }

        private async void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isEnabled || _gantryService == null) return;

            if (e.ChangedButton == MouseButton.Left)
            {
                Point mousePos = e.GetPosition(_overlayCanvas);
                double deltaX = mousePos.X - _imageCenter.X;
                double deltaY = _imageCenter.Y - mousePos.Y; // Invert Y for standard coordinate system

                var args = new ClickLocationEventArgs(mousePos, _imageCenter);
                LocationClicked?.Invoke(this, args);

                try
                {
                    // Get scale factor from parent BaslerDisplayViewControl
                    var parent = this.Parent;
                    while (parent != null && !(parent is BaslerDisplayViewControl))
                    {
                        parent = LogicalTreeHelper.GetParent(parent);
                    }

                    if (parent is BaslerDisplayViewControl displayControl)
                    {
                        double scaleFactor = displayControl.GetCurrentScaleFactor();
                        await _gantryService.HandleCameraClick(mousePos, _imageCenter, scaleFactor);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error handling camera click");
                    MessageBox.Show(
                        $"Error processing click: {ex.Message}",
                        "Click Processing Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void SetupOverlayElements()
        {
            // Create center crosshair (yellow)
            _horizontalCenterLine = new Line
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            _verticalCenterLine = new Line
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            // Create mouse crosshair (blue)
            _mouseCrosshair = new Path
            {
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            // Create coordinate display
            _coordinateDisplay = new TextBlock
            {
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                Padding = new Thickness(5),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            // Add elements to canvas
            _overlayCanvas.Children.Add(_horizontalCenterLine);
            _overlayCanvas.Children.Add(_verticalCenterLine);
            _overlayCanvas.Children.Add(_mouseCrosshair);
            _overlayCanvas.Children.Add(_coordinateDisplay);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCenterCrosshair();
        }

        private void UpdateCenterCrosshair()
        {
            if (_overlayCanvas == null) return;

            double width = _overlayCanvas.ActualWidth;
            double height = _overlayCanvas.ActualHeight;

            _imageCenter = new Point(width / 2, height / 2);

            // Update horizontal line
            _horizontalCenterLine.X1 = 0;
            _horizontalCenterLine.X2 = width;
            _horizontalCenterLine.Y1 = _imageCenter.Y;
            _horizontalCenterLine.Y2 = _imageCenter.Y;

            // Update vertical line
            _verticalCenterLine.X1 = _imageCenter.X;
            _verticalCenterLine.X2 = _imageCenter.X;
            _verticalCenterLine.Y1 = 0;
            _verticalCenterLine.Y2 = height;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition(_overlayCanvas);
            UpdateMouseCrosshair(mousePos);
            UpdateCoordinateDisplay(mousePos);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _mouseCrosshair.Visibility = Visibility.Collapsed;
            _coordinateDisplay.Visibility = Visibility.Collapsed;
        }


        private void UpdateMouseCrosshair(Point position)
        {
            // Create crosshair geometry
            const double size = 10;
            var geometry = new GeometryGroup();

            // Horizontal line
            geometry.Children.Add(new LineGeometry(
                new Point(position.X - size, position.Y),
                new Point(position.X + size, position.Y)));

            // Vertical line
            geometry.Children.Add(new LineGeometry(
                new Point(position.X, position.Y - size),
                new Point(position.X, position.Y + size)));

            _mouseCrosshair.Data = geometry;
            _mouseCrosshair.Visibility = Visibility.Visible;
        }

        private void UpdateCoordinateDisplay(Point position)
        {
            double deltaX = position.X - _imageCenter.X;
            double deltaY = _imageCenter.Y - position.Y; // Invert Y for standard coordinate system

            _coordinateDisplay.Text = $"X: {deltaX:F0} px\nY: {deltaY:F0} px";

            // Position the text block near the mouse cursor
            Canvas.SetLeft(_coordinateDisplay, position.X + 15);
            Canvas.SetTop(_coordinateDisplay, position.Y + 15);
            _coordinateDisplay.Visibility = Visibility.Visible;
        }

        private void ShowClickedCoordinates(double deltaX, double deltaY)
        {
            MessageBox.Show(
                $"Clicked Position (relative to center):\nX: {deltaX:F0} pixels\nY: {deltaY:F0} pixels",
                "Coordinate Information",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    public class ClickLocationEventArgs : EventArgs
    {
        public Point ClickPosition { get; }
        public Point ImageCenter { get; }

        public ClickLocationEventArgs(Point clickPosition, Point imageCenter)
        {
            ClickPosition = clickPosition;
            ImageCenter = imageCenter;
        }
    }
}