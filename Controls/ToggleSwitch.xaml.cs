using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.Controls
{
    public partial class ToggleSwitch : UserControl
    {
        private IOManager _ioManager;
        private string _deviceName;
        private string _pinName;
        private bool _updatingFromIO;

        public event EventHandler<bool> StateChanged;

        public ToggleSwitch()
        {
            InitializeComponent();
            UpdateStateText(false);
        }

        public void Configure(IOManager ioManager, string deviceName, string pinName, string label)
        {
            _ioManager = ioManager;
            _deviceName = deviceName;
            _pinName = pinName;
            Label.Text = label;

            // Subscribe to IO state changes
            if (_ioManager != null)
            {
                _ioManager.IOStateChanged += IOManager_IOStateChanged;

                // Set initial state
                var initialState = _ioManager.GetPinState(_deviceName, _pinName, false);
                if (initialState.HasValue)
                {
                    UpdateState(initialState.Value);
                }
            }
        }

        private void IOManager_IOStateChanged(object sender, IOStateEventArgs e)
        {
            // Only handle if it's our pin
            if (e.DeviceName == _deviceName && e.PinName == _pinName && !e.IsInput)
            {
                _updatingFromIO = true;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateState(e.State);
                    });
                }
                finally
                {
                    _updatingFromIO = false;
                }
            }
        }

        private void Switch_Click(object sender, RoutedEventArgs e)
        {
            if (_ioManager == null || _updatingFromIO) return;

            bool newState = Switch.IsChecked ?? false;
            bool success;

            if (newState)
            {
                success = _ioManager.SetOutput(_deviceName, _pinName);
            }
            else
            {
                success = _ioManager.ClearOutput(_deviceName, _pinName);
            }

            if (!success)
            {
                // Revert the toggle state if IO operation failed
                Switch.IsChecked = !newState;
                return;
            }

            UpdateStateText(newState);
            StateChanged?.Invoke(this, newState);
        }

        private void UpdateStateText(bool isOn)
        {
            StateText.Text = isOn ? "On" : "Off";
        }

        public bool IsOn
        {
            get => Switch.IsChecked ?? false;
            set
            {
                Switch.IsChecked = value;
                UpdateStateText(value);
            }
        }

        public void UpdateState(bool state)
        {
            Switch.IsChecked = state;
            UpdateStateText(state);
        }
    }
}