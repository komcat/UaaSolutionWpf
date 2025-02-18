using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Services;
using System.Threading;
using System.Windows.Input;
using System.Threading.Tasks;

namespace UaaSolutionWpf.Controls
{
    public partial class EmergencyStopWindow : Window
    {
        private readonly AcsGantryConnectionManager _gantryManager;
        private readonly GantryMovementService _movementService;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly Button stopButton;
        private bool _isStopInProgress = false;

        public EmergencyStopWindow(
            AcsGantryConnectionManager gantryManager,
            GantryMovementService movementService)
        {
            _gantryManager = gantryManager ?? throw new ArgumentNullException(nameof(gantryManager));
            _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
            _cancellationTokenSource = new CancellationTokenSource();

            // Set window properties
            Title = "Emergency Stop";
            Width = 120;
            Height = 120;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;

            // Create main grid
            var grid = new Grid();

            // Create button
            stopButton = new Button
            {
                Width = 100,
                Height = 100,
                Margin = new Thickness(10),
                ToolTip = "Emergency Stop"
            };

            try
            {
                // Load and set the image
                var image = new Image
                {
                    Source = new BitmapImage(new Uri("/Images/stop.png", UriKind.Relative)),
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
                stopButton.Content = image;
            }
            catch (Exception)
            {
                // Fallback if image loading fails
                stopButton.Content = "STOP";
                stopButton.FontSize = 24;
                stopButton.Foreground = System.Windows.Media.Brushes.Red;
            }

            // Add click handler
            stopButton.Click += StopButton_Click;

            // Add button to grid
            grid.Children.Add(stopButton);

            // Set the window content
            Content = grid;

            // Add keyboard event handler
            this.KeyDown += EmergencyStopWindow_KeyDown;

            // Make sure window can receive keyboard focus
            this.Focusable = true;
        }

        private void EmergencyStopWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                StopButton_Click(this, new RoutedEventArgs());
            }
        }

        public CancellationToken GetCancellationToken()
        {
            return _cancellationTokenSource.Token;
        }

        private void ResetCancellationToken()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isStopInProgress)
            {
                return; // Prevent multiple simultaneous stop operations
            }

            try
            {
                _isStopInProgress = true;

                // First, cancel any ongoing operations
                _cancellationTokenSource.Cancel();

                // Immediately stop all motors
                await Task.Run(async () =>
                {
                    // Stop motors multiple times to ensure the stop command is received
                    for (int i = 0; i < 3; i++)
                    {
                        await _gantryManager.StopAllMotorsAsync();
                        await Task.Delay(50); // Small delay between stop commands
                    }
                });

                // Wait a moment to ensure everything has stopped
                await Task.Delay(100);

                // Reset for future operations
                ResetCancellationToken();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop motors: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isStopInProgress = false;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
        }
    }
}