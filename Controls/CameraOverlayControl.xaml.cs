using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace UaaSolutionWpf.Controls
{
    public partial class CameraOverlayControl : UserControl
    {
        private Line _horizontalCenterLine;
        private Line _verticalCenterLine;
        private Path _mouseCrosshair;
        private TextBlock _coordinateDisplay;
        private Point _imageCenter;

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

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                Point mousePos = e.GetPosition(_overlayCanvas);
                double deltaX = mousePos.X - _imageCenter.X;
                double deltaY = _imageCenter.Y - mousePos.Y; // Invert Y for standard coordinate system

                // Raise event or update display with clicked coordinates
                ShowClickedCoordinates(deltaX, deltaY);
            }
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
}