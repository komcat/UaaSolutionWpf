using System;
using System.Windows.Controls;
using UaaSolutionWpf.ViewModels;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.Controls
{
    public partial class PneumaticSlideItem : UserControl
    {
        private SlideViewModel _viewModel;
        private IOManager _ioManager;
        private SlideConfiguration _configuration;

        public SlideConfiguration Configuration => _configuration;

        public PneumaticSlideItem()
        {
            InitializeComponent();
        }

        public void Initialize(SlideConfiguration config, IOManager ioManager)
        {
            _configuration = config ?? throw new ArgumentNullException(nameof(config));
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));

            // Create view model
            _viewModel = new SlideViewModel(config, ioManager);
            DataContext = _viewModel;

            // Configure toggle switch
            SlideToggle.Configure(
                ioManager,
                config.Controls.Output.Device,
                config.Controls.Output.PinName,
                config.Name);

            // Subscribe to toggle switch state changes
            SlideToggle.StateChanged += OnToggleSwitchStateChanged;

            // Check initial sensor states
            CheckInitialSensorStates();
        }

        private void CheckInitialSensorStates()
        {
            if (_configuration?.Controls?.Sensors == null || _ioManager == null)
                return;

            try
            {
                // Get initial states for up and down sensors
                bool? upSensorState = _ioManager.GetPinState(
                    _configuration.Controls.Sensors.Device,
                    _configuration.Controls.Sensors.UpSensor,
                    true
                );

                bool? downSensorState = _ioManager.GetPinState(
                    _configuration.Controls.Sensors.Device,
                    _configuration.Controls.Sensors.DownSensor,
                    true
                );

                // Update states if we have valid sensor readings
                if (upSensorState.HasValue || downSensorState.HasValue)
                {
                    UpdateSensorStates(
                        upSensorState ?? false,
                        downSensorState ?? false
                    );
                }
            }
            catch (Exception ex)
            {
                // Log any errors in checking initial sensor states
                System.Diagnostics.Debug.WriteLine($"Error checking initial sensor states for {_configuration.Name}: {ex.Message}");
            }
        }

        private void OnToggleSwitchStateChanged(object sender, bool state)
        {
            if (_configuration != null)
            {
                // Attempt to set the output based on the toggle
                bool success = state
                    ? _ioManager.SetOutput(
                        _configuration.Controls.Output.Device,
                        _configuration.Controls.Output.PinName)
                    : _ioManager.ClearOutput(
                        _configuration.Controls.Output.Device,
                        _configuration.Controls.Output.PinName);

                if (!success)
                {
                    // If setting output fails, revert the toggle
                    SlideToggle.UpdateState(!state);
                }
            }
        }

        public void UpdateSensorStates(bool upSensorState, bool downSensorState)
        {
            SlideState newState = SlideState.Unknown;

            if (upSensorState)
            {
                newState = SlideState.Up;
            }
            else if (downSensorState)
            {
                newState = SlideState.Down;
            }

            // Update view model state without affecting toggle switch
            if (_viewModel != null)
            {
                _viewModel.State = newState;
            }
        }
    }
}