using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.Services;
using Serilog;

namespace UaaSolutionWpf.Controls
{
    public class IOPinViewModel : INotifyPropertyChanged
    {
        private bool _status;
        private string _buttonText;

        public string Name { get; set; }
        public string PinNumber { get; set; }
        public ICommand ToggleCommand { get; set; }

        public bool Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    ButtonText = value ? "OFF" : "ON";
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public string ButtonText
        {
            get => _buttonText;
            set
            {
                if (_buttonText != value)
                {
                    _buttonText = value;
                    OnPropertyChanged(nameof(ButtonText));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class IOMonitorControl : UserControl, IDisposable
    {
        private  ILogger _logger;
        private  IOService _ioService;
        private  IOMonitor _ioMonitor;
        private bool _isDisposed;

        public ObservableCollection<IOPinViewModel> OutputPins { get; } = new ObservableCollection<IOPinViewModel>();
        public ObservableCollection<IOPinViewModel> InputPins { get; } = new ObservableCollection<IOPinViewModel>();

        private string _deviceName;
        public string DeviceName
        {
            get => _deviceName;
            set
            {
                _deviceName = value;
                DeviceNameText.Text = value;
            }
        }

        private string _ipAddress;
        public string IPAddress
        {
            get => _ipAddress;
            set
            {
                _ipAddress = value;
                IpAddressText.Text = $"IP: {value}";
            }
        }

        public IOMonitorControl()
        {
            InitializeComponent();
            OutputList.ItemsSource = OutputPins;
            InputList.ItemsSource = InputPins;
        }

        public void Initialize(ILogger logger, IOService ioService, IOMonitor ioMonitor, string deviceName, string ipAddress)
        {
            _logger = logger.ForContext<IOMonitorControl>();
            _ioService = ioService;
            _ioMonitor = ioMonitor;
            DeviceName = deviceName;
            IPAddress = ipAddress;

            _ioMonitor.PinStateChanged += OnPinStateChanged;
            ConnectionStatusText.Text = "Status: Connected";
        }

        public void AddOutputPin(string name, int pinNumber)
        {
            var pinViewModel = new IOPinViewModel
            {
                Name = name,
                PinNumber = pinNumber.ToString(),
                Status = false,
                ButtonText = "ON",
                ToggleCommand = new RelayCommand(param => ToggleOutput(name))
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                OutputPins.Add(pinViewModel);
            });
        }

        public void AddInputPin(string name, int pinNumber)
        {
            var pinViewModel = new IOPinViewModel
            {
                Name = name,
                PinNumber = pinNumber.ToString(),
                Status = false
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                InputPins.Add(pinViewModel);
            });
        }

        private void OnPinStateChanged(object sender, PinStatusInfo pinStatus)
        {
            if (pinStatus.DeviceName != DeviceName) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (pinStatus.PinType == IOPinType.Output)
                {
                    var pin = OutputPins.FirstOrDefault(p => p.Name == pinStatus.PinName);
                    if (pin != null)
                    {
                        pin.Status = pinStatus.State;
                    }
                }
                else
                {
                    var pin = InputPins.FirstOrDefault(p => p.Name == pinStatus.PinName);
                    if (pin != null)
                    {
                        pin.Status = pinStatus.State;
                    }
                }
            });
        }

        private void ToggleOutput(string pinName)
        {
            try
            {
                var pin = OutputPins.FirstOrDefault(p => p.Name == pinName);
                if (pin != null)
                {
                    bool newState = !pin.Status;
                    bool success = _ioService.SetOutput(DeviceName, pinName, newState);

                    if (!success)
                    {
                        _logger.Error("Failed to toggle output {PinName} on device {DeviceName}", pinName, DeviceName);
                        MessageBox.Show(
                            $"Failed to toggle output {pinName}",
                            "IO Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error toggling output {PinName} on device {DeviceName}", pinName, DeviceName);
                MessageBox.Show(
                    $"Error toggling output {pinName}: {ex.Message}",
                    "IO Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        public void UpdateConnectionStatus(bool isConnected)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ConnectionStatusText.Text = isConnected ? "Status: Connected" : "Status: Disconnected";
            });
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_ioMonitor != null)
                {
                    _ioMonitor.PinStateChanged -= OnPinStateChanged;
                }
                _isDisposed = true;
            }
        }

        ~IOMonitorControl()
        {
            Dispose();
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}