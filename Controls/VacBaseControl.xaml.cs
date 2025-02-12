using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Serilog;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.Controls
{
    public partial class VacBaseControl : UserControl
    {
        private readonly LinearGradientBrush _activeStatusBrush;
        private readonly LinearGradientBrush _inactiveStatusBrush;
        private readonly ILogger _logger;
        private IOManager _ioManager;

        // Constants for vacuum control
        private const string DEVICE_NAME = "IOBottom";
        private const string VACUUM_PIN_NAME = "Vacuum_Base";

        private bool _awaitingStateChange = false;
        private bool _pendingState = false;

        public VacBaseControl()
        {
            InitializeComponent();
            _logger = Log.ForContext<VacBaseControl>();

            // Initialize status indicator brushes
            _activeStatusBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            _activeStatusBrush.GradientStops.Add(new GradientStop(Color.FromRgb(67, 97, 238), 0));
            _activeStatusBrush.GradientStops.Add(new GradientStop(Color.FromRgb(58, 12, 163), 1));

            _inactiveStatusBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            _inactiveStatusBrush.GradientStops.Add(new GradientStop(Color.FromRgb(233, 236, 239), 0));
            _inactiveStatusBrush.GradientStops.Add(new GradientStop(Color.FromRgb(233, 236, 239), 1));

            // Add click event handlers for vacuum control
            VacBaseOnButton.Click += (s, e) => SetVacuumState(true);
            VacBaseOffButton.Click += (s, e) => SetVacuumState(false);

            // Add animation handlers
            VacBaseOnButton.PreviewMouseDown += Button_PreviewMouseDown;
            VacBaseOnButton.PreviewMouseUp += Button_PreviewMouseUp;
            VacBaseOffButton.PreviewMouseDown += Button_PreviewMouseDown;
            VacBaseOffButton.PreviewMouseUp += Button_PreviewMouseUp;
        }

        public void Initialize(IOManager ioManager)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _logger.Information("VacBaseControl initialized with IOManager");

            // Subscribe to IO state changes
            _ioManager.IOStateChanged += OnIOStateChanged;
        }

        private void OnIOStateChanged(object sender, IOStateEventArgs e)
        {
            // Only handle events for our vacuum pin
            if (e.DeviceName == DEVICE_NAME && e.PinName == VACUUM_PIN_NAME && !e.IsInput)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // If we were waiting for a state change and got it
                    if (_awaitingStateChange && e.State == _pendingState)
                    {
                        _awaitingStateChange = false;
                        _logger.Debug("Received expected state change confirmation: {State}", e.State);
                    }

                    VacBaseState = e.State;
                });
            }
        }

        private bool _vacBaseState;
        public bool VacBaseState
        {
            get => _vacBaseState;
            private set
            {
                if (_vacBaseState != value)
                {
                    _vacBaseState = value;
                    UpdateControlStates();
                    _logger.Information("Vacuum base state changed to: {State}", value);
                }
            }
        }

        private void SetVacuumState(bool activate)
        {
            try
            {
                if (_ioManager == null)
                {
                    _logger.Error("IOManager not initialized");
                    MessageBox.Show("IO system not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Set the pending state flags
                _awaitingStateChange = true;
                _pendingState = activate;

                // Send the command
                _ioManager.SetOutputState(DEVICE_NAME, VACUUM_PIN_NAME, activate);

                // Log the attempt
                _logger.Information("Vacuum {State} command sent", activate ? "activate" : "deactivate");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error {Action} vacuum", activate ? "activating" : "deactivating");
                MessageBox.Show(
                    $"Error {(activate ? "activating" : "deactivating")} vacuum: {ex.Message}",
                    "Vacuum Control Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateControlStates()
        {
            // Update status indicator
            StatusIndicator.Background = VacBaseState ? _activeStatusBrush : _inactiveStatusBrush;

            // Update button states
            VacBaseOnButton.IsEnabled = !VacBaseState;
            VacBaseOffButton.IsEnabled = VacBaseState;

            // Animate the change
            var fadeAnimation = new DoubleAnimation
            {
                To = VacBaseState ? 1 : 0.6,
                Duration = TimeSpan.FromMilliseconds(200)
            };

            if (VacBaseState)
            {
                VacBaseOnButton.BeginAnimation(OpacityProperty, fadeAnimation);
                VacBaseOffButton.Opacity = 1;
            }
            else
            {
                VacBaseOffButton.BeginAnimation(OpacityProperty, fadeAnimation);
                VacBaseOnButton.Opacity = 1;
            }
        }

        private void Button_PreviewMouseDown(object sender, RoutedEventArgs e)
        {
            var animation = (Storyboard)FindResource("PressAnimation");
            animation.Begin((Button)sender);
        }

        private void Button_PreviewMouseUp(object sender, RoutedEventArgs e)
        {
            var animation = (Storyboard)FindResource("ReleaseAnimation");
            animation.Begin((Button)sender);
        }
    }
}