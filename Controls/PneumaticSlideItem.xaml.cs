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
        private bool _isInitialized;

        public SlideConfiguration Configuration => _configuration;

        public PneumaticSlideItem()
        {
            InitializeComponent();
        }

        public async Task InitializeAsync(SlideConfiguration config, IOManager ioManager)
        {
            if (_isInitialized)
                return;

            _configuration = config ?? throw new ArgumentNullException(nameof(config));
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));

            try
            {
                // Create view model on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    _viewModel = new SlideViewModel(config, ioManager);
                    DataContext = _viewModel;
                });

                // Configure toggle switch
                await Dispatcher.InvokeAsync(() =>
                {
                    SlideToggle.Configure(
                        ioManager,
                        config.Controls.Output.Device,
                        config.Controls.Output.PinName,
                        config.Name);

                    // Subscribe to toggle switch state changes
                    SlideToggle.StateChanged += OnToggleSwitchStateChanged;
                });

                // Check initial sensor states asynchronously
                await CheckInitialSensorStatesAsync();

                // Subscribe to IO state changes
                _ioManager.IOStateChanged += OnIOStateChanged;

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing slide item {config.Name}: {ex.Message}");
                throw;
            }
        }
        private async Task CheckInitialSensorStatesAsync()
        {
            if (_configuration?.Controls?.Sensors == null || _ioManager == null)
                return;

            try
            {
                // Get initial states for up and down sensors
                var upSensorStateTask = Task.Run(() => _ioManager.GetPinState(
                    _configuration.Controls.Sensors.Device,
                    _configuration.Controls.Sensors.UpSensor,
                    true
                ));

                var downSensorStateTask = Task.Run(() => _ioManager.GetPinState(
                    _configuration.Controls.Sensors.Device,
                    _configuration.Controls.Sensors.DownSensor,
                    true
                ));

                await Task.WhenAll(upSensorStateTask, downSensorStateTask);

                bool? upSensorState = await upSensorStateTask;
                bool? downSensorState = await downSensorStateTask;

                // Update states if we have valid sensor readings
                if (upSensorState.HasValue || downSensorState.HasValue)
                {
                    await UpdateSensorStatesAsync(
                        upSensorState ?? false,
                        downSensorState ?? false
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking initial sensor states for {_configuration.Name}: {ex.Message}");
            }
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

        private async void OnToggleSwitchStateChanged(object sender, bool state)
        {
            if (_configuration == null) return;

            try
            {
                // Run IO operations on background thread
                bool success = await Task.Run(() => state
                    ? _ioManager.SetOutput(
                        _configuration.Controls.Output.Device,
                        _configuration.Controls.Output.PinName)
                    : _ioManager.ClearOutput(
                        _configuration.Controls.Output.Device,
                        _configuration.Controls.Output.PinName));

                if (!success)
                {
                    // If setting output fails, revert the toggle on UI thread
                    await Dispatcher.InvokeAsync(() => SlideToggle.UpdateState(!state));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in toggle switch state change: {ex.Message}");
                await Dispatcher.InvokeAsync(() => SlideToggle.UpdateState(!state));
            }
        }

        private void OnIOStateChanged(object sender, IOStateEventArgs e)
        {
            if (_configuration?.Controls?.Sensors == null) return;

            // Check if this state change is relevant for our sensors
            if (e.DeviceName == _configuration.Controls.Sensors.Device)
            {
                if (e.PinName == _configuration.Controls.Sensors.UpSensor)
                {
                    UpdateSensorStatesAsync(e.State, false).ConfigureAwait(false);
                }
                else if (e.PinName == _configuration.Controls.Sensors.DownSensor)
                {
                    UpdateSensorStatesAsync(false, e.State).ConfigureAwait(false);
                }
            }
        }

        public async Task UpdateSensorStatesAsync(bool upSensorState, bool downSensorState)
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

            // Update view model state on UI thread
            if (_viewModel != null)
            {
                await Dispatcher.InvokeAsync(() => _viewModel.State = newState);
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