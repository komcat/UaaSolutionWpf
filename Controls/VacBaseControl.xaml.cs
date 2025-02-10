using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UaaSolutionWpf.Controls
{
    public partial class VacBaseControl : UserControl
    {
        private readonly LinearGradientBrush _activeStatusBrush;
        private readonly LinearGradientBrush _inactiveStatusBrush;

        public VacBaseControl()
        {
            InitializeComponent();

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

            // Add click event handlers
            VacBaseOnButton.Click += (s, e) => VacBaseState = true;
            VacBaseOffButton.Click += (s, e) => VacBaseState = false;

            // Add animation handlers
            VacBaseOnButton.PreviewMouseDown += Button_PreviewMouseDown;
            VacBaseOnButton.PreviewMouseUp += Button_PreviewMouseUp;
            VacBaseOffButton.PreviewMouseDown += Button_PreviewMouseDown;
            VacBaseOffButton.PreviewMouseUp += Button_PreviewMouseUp;
        }

        private bool _vacBaseState;
        public bool VacBaseState
        {
            get => _vacBaseState;
            set
            {
                _vacBaseState = value;
                UpdateControlStates();
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