using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.Controls
{
    public partial class GripperControl : UserControl
    {
        private IOManager _ioManager;
        private string _deviceName;
        private string _pinName;
        private bool _gripperState;

        public GripperControl()
        {
            InitializeComponent();

            // Add click event handlers
            CloseGripperButton.Click += (s, e) => SetGripperState(true);
            OpenGripperButton.Click += (s, e) => SetGripperState(false);
        }

        public void Configure(IOManager ioManager, string deviceName, string pinName, string gripperName = "Gripper")
        {
            _ioManager = ioManager;
            _deviceName = deviceName;
            _pinName = pinName;
            GripperName.Text = gripperName;
        }

        private void SetGripperState(bool closed)
        {
            if (_ioManager == null) return;

            bool success;
            if (closed)
            {
                success = _ioManager.SetOutput(_deviceName, _pinName);
            }
            else
            {
                success = _ioManager.ClearOutput(_deviceName, _pinName);
            }

            if (success)
            {
                _gripperState = closed;
                UpdateButtonStates();
            }
        }

        private void UpdateButtonStates()
        {
            CloseGripperButton.Background = _gripperState ?
                new SolidColorBrush(Color.FromRgb(0, 122, 204)) :  // #007ACC
                Brushes.LightGray;

            OpenGripperButton.Background = !_gripperState ?
                new SolidColorBrush(Color.FromRgb(0, 122, 204)) :  // #007ACC
                Brushes.LightGray;
        }
    }
}