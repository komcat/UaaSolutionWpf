using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using MotionServiceLib;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Extension methods and helpers for integrating BaslerDisplayViewControl with gantry movement
    /// </summary>
    public static class BaslerDisplayViewExtension
    {
        /// <summary>
        /// Connects a BaslerDisplayViewControl to a CameraGantryController to enable click-to-move functionality
        /// </summary>
        /// <param name="displayControl">The BaslerDisplayViewControl instance</param>
        /// <param name="motionKernel">The MotionKernel instance</param>
        /// <param name="gantryDeviceId">The ID of the gantry device to control</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>The created CameraGantryController instance</returns>
        public static CameraGantryController EnableGantryControl(
            this BaslerDisplayViewControl displayControl,
            MotionKernel motionKernel,
            string gantryDeviceId,
            ILogger logger = null)
        {
            if (displayControl == null)
                throw new ArgumentNullException(nameof(displayControl));

            if (motionKernel == null)
                throw new ArgumentNullException(nameof(motionKernel));

            if (string.IsNullOrEmpty(gantryDeviceId))
                throw new ArgumentNullException(nameof(gantryDeviceId));

            // Create a logger if not provided
            logger = logger ?? Log.ForContext(typeof(BaslerDisplayViewExtension));

            // Create the controller
            var gantryController = new CameraGantryController(motionKernel, gantryDeviceId, logger);

            // Add a border around the display to indicate it's enabled for click-to-move
            displayControl.BorderBrush = Brushes.Green;
            displayControl.BorderThickness = new Thickness(2);

            // Add a tooltip
            displayControl.ToolTip = "Click on the image to move the gantry to that position";

            // Add click handler
            displayControl.PreviewMouseDown += (sender, e) => HandleImageClick(displayControl, gantryController, e, logger);

            // Add cursor change on mouse enter
            displayControl.MouseEnter += (sender, e) => displayControl.Cursor = Cursors.Cross;
            displayControl.MouseLeave += (sender, e) => displayControl.Cursor = Cursors.Arrow;

            // Add a status indicator
            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0)
            };

            var statusText = new TextBlock
            {
                Text = "Click-to-Move Ready",
                Foreground = Brushes.White,
                Margin = new Thickness(5)
            };

            statusPanel.Children.Add(statusText);

            // Find the main grid and add the status panel
            if (displayControl.Content is Grid mainGrid)
            {
                mainGrid.Children.Add(statusPanel);
            }

            // Set up movement status updates
            gantryController.MovementStarted += (sender, args) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    statusText.Text = $"Moving: X={args.DeltaXmm:F2}mm, Y={args.DeltaYmm:F2}mm";
                    statusPanel.Background = new SolidColorBrush(Color.FromArgb(192, 255, 165, 0)); // Orange
                    displayControl.IsEnabled = false; // Disable during movement
                });
            };

            gantryController.MovementCompleted += (sender, args) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (args.Success)
                    {
                        statusText.Text = "Movement Complete";
                        statusPanel.Background = new SolidColorBrush(Color.FromArgb(128, 0, 128, 0)); // Green
                    }
                    else
                    {
                        statusText.Text = $"Movement Failed: {args.ErrorMessage}";
                        statusPanel.Background = new SolidColorBrush(Color.FromArgb(192, 255, 0, 0)); // Red
                    }

                    // Restore original state after a delay
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(3)
                    };

                    timer.Tick += (s, e) =>
                    {
                        statusText.Text = "Click-to-Move Ready";
                        statusPanel.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
                        timer.Stop();
                    };

                    timer.Start();
                    displayControl.IsEnabled = true; // Re-enable after movement
                });
            };

            // Add calibration menu item to context menu
            var contextMenu = new ContextMenu();
            var calibrateMenuItem = new MenuItem { Header = "Calibrate Camera-to-Gantry" };
            calibrateMenuItem.Click += (sender, args) => ShowCalibrationWindow(displayControl, gantryController, logger);
            contextMenu.Items.Add(calibrateMenuItem);

            displayControl.ContextMenu = contextMenu;

            return gantryController;
        }

        /// <summary>
        /// Handle mouse click on the image to move the gantry
        /// </summary>
        private static async void HandleImageClick(
            BaslerDisplayViewControl displayControl,
            CameraGantryController gantryController,
            MouseButtonEventArgs e,
            ILogger logger)
        {
            try
            {
                // Only handle left mouse button clicks
                if (e.ChangedButton != MouseButton.Left)
                    return;

                // Get the click position
                Point clickPoint = e.GetPosition(displayControl);

                // Get the display control dimensions
                Size displaySize = new Size(displayControl.ActualWidth, displayControl.ActualHeight);

                // Calculate the center point
                Point centerPoint = new Point(displaySize.Width / 2, displaySize.Height / 2);

                // Get scale factor if the image is scaled
                double scaleFactor = 1.0;
                // If there's a way to get the actual scale factor from your Basler control, use that instead

                logger.Debug("Image clicked at {ClickPoint} (Center: {CenterPoint}, Scale: {Scale})",
                    clickPoint, centerPoint, scaleFactor);

                // Move the gantry to the clicked position
                await gantryController.HandleImageClickAsync(
                    clickPoint,
                    centerPoint,
                    displaySize,
                    scaleFactor,
                    CameraGantryController.MoveMode.Center);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error handling image click");
                MessageBox.Show(
                    $"Error moving to clicked position: {ex.Message}",
                    "Movement Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Show the calibration window
        /// </summary>
        private static void ShowCalibrationWindow(
            BaslerDisplayViewControl displayControl,
            CameraGantryController gantryController,
            ILogger logger)
        {
            try
            {
                // Create the calibration manager
                var calibrationManager = new CameraCalibrationManager(logger);

                // Create and show the calibration window
                var window = new CameraCalibrationWindow(gantryController, calibrationManager, logger)
                {
                    Owner = Window.GetWindow(displayControl)
                };

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error showing calibration window");
                MessageBox.Show(
                    $"Error showing calibration window: {ex.Message}",
                    "Calibration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}