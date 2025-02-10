using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json;
using UaaSolutionWpf.ViewModels;
using UaaSolutionWpf.Controls;

namespace UaaSolutionWpf.IO
{
    public class IOConfiguration
    {
        public Metadata Metadata { get; set; }
        public List<EziioDevice> Eziio { get; set; }
    }

    public class Metadata
    {
        public string Version { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class EziioDevice
    {
        public int DeviceId { get; set; }
        public string Name { get; set; }
        public string IP { get; set; }
        public IOConfig IOConfig { get; set; }
    }

    public class IOConfig
    {
        public List<PinConfig> Outputs { get; set; }
        public List<PinConfig> Inputs { get; set; }
    }

    public class PinConfig
    {
        public int Pin { get; set; }
        public string Name { get; set; }
    }

    public class IOStateEventArgs : EventArgs
    {
        public string DeviceName { get; }
        public string PinName { get; }
        public bool State { get; }
        public bool IsInput { get; }

        public IOStateEventArgs(string deviceName, string pinName, bool state, bool isInput)
        {
            DeviceName = deviceName;
            PinName = pinName;
            State = state;
            IsInput = isInput;
        }
    }

    public class IOManager
    {
        private readonly ILogger _logger;
        private readonly EziioControl _bottomControl;
        private readonly EziioControl _topControl;
        private EziioController2 _bottomController;
        private EziioController2 _topController;
        private EziioViewModel _bottomViewModel;
        private EziioViewModel _topViewModel;
        private IOConfiguration _ioConfig;
        // Add the IOStateChanged event
        public event EventHandler<IOStateEventArgs> IOStateChanged;
        public IOManager(EziioControl bottomControl, EziioControl topControl, ILogger logger)
        {
            _bottomControl = bottomControl;
            _topControl = topControl;
            _logger = logger.ForContext<IOManager>();
        }

        public void Initialize()
        {
            try
            {
                LoadConfiguration();
                InitializeControllers();
                ConnectControllers();
                AssignViewModelsToControls();

                _logger.Information("Successfully initialized IO monitor controls");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize IO monitor controls");
                MessageBox.Show(
                    $"Failed to initialize IO controls: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadConfiguration()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "IOConfig.json");
            string jsonContent = File.ReadAllText(configPath);
            _ioConfig = JsonConvert.DeserializeObject<IOConfiguration>(jsonContent);

            _logger.Information("Loaded IO configuration version {Version}, last updated {LastUpdated}",
                _ioConfig.Metadata.Version,
                _ioConfig.Metadata.LastUpdated);
        }

        private void InitializeControllers()
        {
            var bottomDevice = _ioConfig.Eziio.First(d => d.Name == "IOBottom");
            var topDevice = _ioConfig.Eziio.First(d => d.Name == "IOTop");

            // First create the ViewModels
            _bottomViewModel = CreateViewModel(bottomDevice);
            _topViewModel = CreateViewModel(topDevice);

            // Initialize Controllers
            _bottomController = new EziioController2(bottomDevice, _logger);
            _topController = new EziioController2(topDevice, _logger);

            // Connect toggle commands to controllers
            ConnectToggleCommands(_bottomViewModel, _bottomController);
            ConnectToggleCommands(_topViewModel, _topController);

            // Setup controller events
            SetupControllerEvents(_bottomController, _bottomViewModel);
            SetupControllerEvents(_topController, _topViewModel);
        }
        // In IOManager, modify the ConnectToggleCommands:
        private void ConnectToggleCommands(EziioViewModel viewModel, EziioController2 controller)
        {
            viewModel.TogglePinCommand = new RelayCommand<PinViewModel>(pin =>
            {
                if (pin == null) return;

                // Get the current state from the pin
                bool currentState = pin.State;
                bool newState = !currentState;  // Toggle the state

                _logger.Information($"Processing toggle command for {viewModel.DeviceName} pin {pin.Name} from {(currentState ? "ON" : "OFF")} to {(newState ? "ON" : "OFF")}");

                bool success;
                if (newState)
                {
                     success = controller.ClearOutputByName(pin.Name);  // Make sure we call ClearOutput when toggling OFF
                }
                else
                {
                    success = controller.SetOutput(pin.Name, true);
                }

                if (success)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        pin.State = newState;  // Update the UI state directly
                        _logger.Debug($"[{viewModel.DeviceName}] Updated UI state for {pin.Name} to {(newState ? "ON" : "OFF")}");
                    });
                }

                _logger.Information($"Toggle command result for {viewModel.DeviceName} pin {pin.Name}: {(success ? "Success" : "Failed")}");
            });
        }
        private void InitializeViewModels()
        {
            _bottomViewModel = new EziioViewModel
            {
                DeviceName = "Bottom IO",
                IpAddress = "192.168.0.5",
                StatusMessage = "Initializing..."
            };

            _topViewModel = new EziioViewModel
            {
                DeviceName = "Top IO",
                IpAddress = "192.168.0.3",
                StatusMessage = "Initializing..."
            };
        }
        private EziioViewModel CreateViewModel(EziioDevice device)
        {
            var viewModel = new EziioViewModel
            {
                DeviceName = device.Name,
                IpAddress = device.IP,
                StatusMessage = "Initializing..."
            };

            // Initialize Inputs
            foreach (var input in device.IOConfig.Inputs.OrderBy(x => x.Pin))
            {
                viewModel.InputPins.Add(new PinViewModel
                {
                    PinNumber = input.Pin.ToString(),
                    Name = input.Name,
                    State = false
                });
            }

            // Initialize Outputs
            foreach (var output in device.IOConfig.Outputs.OrderBy(x => x.Pin))
            {
                viewModel.OutputPins.Add(new PinViewModel
                {
                    PinNumber = output.Pin.ToString(),
                    Name = output.Name,
                    State = false
                });
            }

            return viewModel;
        }
        private void LoadPinMappings(string inputMappingFilePath, string outputMappingFilePath)
        {
            var bottomConfig = JsonConvert.DeserializeObject<EziioControllerClass.IOConfig>(
                File.ReadAllText(outputMappingFilePath));
            var topConfig = JsonConvert.DeserializeObject<EziioControllerClass.IOConfig>(
                File.ReadAllText(inputMappingFilePath));

            InitializePinCollection(_bottomViewModel.InputPins, bottomConfig.inputs);
            InitializePinCollection(_bottomViewModel.OutputPins, bottomConfig.outputs);
            InitializePinCollection(_topViewModel.InputPins, topConfig.inputs);
            InitializePinCollection(_topViewModel.OutputPins, topConfig.outputs);
        }



        private void ConnectControllers()
        {
            if (_bottomController.Connect())
            {
                _bottomViewModel.IsConnected = true;
                _bottomViewModel.StatusMessage = "Connected";
            }

            if (_topController.Connect())
            {
                _topViewModel.IsConnected = true;
                _topViewModel.StatusMessage = "Connected";
            }
        }

        private void AssignViewModelsToControls()
        {
            _bottomControl.DataContext = _bottomViewModel;
            _topControl.DataContext = _topViewModel;
        }

        private void InitializePinCollection(ObservableCollection<PinViewModel> collection,
            Dictionary<string, int> mapping)
        {
            if (mapping != null)
            {
                foreach (var kvp in mapping.OrderBy(x => x.Value))
                {
                    collection.Add(new PinViewModel
                    {
                        PinNumber = kvp.Value.ToString(),
                        Name = kvp.Key,
                        State = false
                    });
                }
            }
        }

        // Modify SetupControllerEvents to raise the IOStateChanged event
        private void SetupControllerEvents(EziioController2 controller, EziioViewModel viewModel)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            controller.ConnectionStateChanged += (s, connected) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    viewModel.IsConnected = connected;
                    viewModel.StatusMessage = connected ? "Connected" : "Disconnected";
                });
            };

            controller.InputStateChanged += (s, state) =>
            {
                var pin = viewModel.InputPins?.FirstOrDefault(p => p.Name == state.Name);
                if (pin != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        pin.State = state.State;
                        // Raise the IOStateChanged event for inputs
                        IOStateChanged?.Invoke(this, new IOStateEventArgs(
                            viewModel.DeviceName,
                            state.Name,
                            state.State,
                            true));
                    });
                }
            };

            controller.OutputStateChanged += (s, state) =>
            {
                var pin = viewModel.OutputPins?.FirstOrDefault(p => p.Name == state.Name);
                if (pin != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        pin.State = state.State;
                        // Raise the IOStateChanged event for outputs
                        IOStateChanged?.Invoke(this, new IOStateEventArgs(
                            viewModel.DeviceName,
                            state.Name,
                            state.State,
                            false));
                    });
                }
            };

            controller.StatusMessageUpdated += (s, message) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    viewModel.StatusMessage = message;
                });
            };
        }

        // Add method to get current pin state
        public bool? GetPinState(string deviceName, string pinName, bool isInput)
        {
            EziioViewModel viewModel = null;
            if (_bottomViewModel.DeviceName == deviceName)
                viewModel = _bottomViewModel;
            else if (_topViewModel.DeviceName == deviceName)
                viewModel = _topViewModel;

            if (viewModel == null)
                return null;

            var pin = isInput ?
                viewModel.InputPins.FirstOrDefault(p => p.Name == pinName) :
                viewModel.OutputPins.FirstOrDefault(p => p.Name == pinName);

            return pin?.State;
        }

        public void Dispose()
        {
            _bottomController?.Disconnect();
            _topController?.Disconnect();
        }
    }
}