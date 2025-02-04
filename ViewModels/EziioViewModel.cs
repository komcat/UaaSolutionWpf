using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace UaaSolutionWpf.ViewModels
{
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;

        public void Execute(object parameter) => _execute((T)parameter);
    }

    public class PinViewModel : INotifyPropertyChanged
    {
        private string _pinNumber;
        private string _name;
        private bool _state;

        public event PropertyChangedEventHandler PropertyChanged;

        public string PinNumber
        {
            get => _pinNumber;
            set
            {
                if (_pinNumber != value)
                {
                    _pinNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    // Log the state change for debugging
                    Debug.WriteLine($"Pin {Name} state changed to {value}");
                }
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    public class EziioViewModel : INotifyPropertyChanged
    {
        private string _deviceName;
        private string _ipAddress;
        private bool _isConnected;
        private string _statusMessage;
        private ObservableCollection<PinViewModel> _inputPins;
        private ObservableCollection<PinViewModel> _outputPins;
        private readonly ILogger _logger;

        private readonly Dictionary<string, PinViewModel> _pinLookup = new Dictionary<string, PinViewModel>();

        public EziioViewModel()
        {
            InputPins = new ObservableCollection<PinViewModel>();
            OutputPins = new ObservableCollection<PinViewModel>();
            TogglePinCommand = new RelayCommand<PinViewModel>(ExecuteTogglePin);
        }
        private void ExecuteTogglePin(PinViewModel pin)
        {
            // This will be handled by IOManager
            // The command is just to connect the UI to the IOManager
        }
        private void OnTogglePin(PinViewModel pin)
        {
            if (pin == null) return;
            // This will be connected to the actual toggle logic later
            _logger.Information($"Toggle request for pin {pin.Name} to state {!pin.State}");
        }
        public string DeviceName
        {
            get => _deviceName;
            set
            {
                _deviceName = value;
                OnPropertyChanged();
            }
        }

        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                _ipAddress = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<PinViewModel> InputPins
        {
            get => _inputPins;
            set
            {
                _inputPins = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<PinViewModel> OutputPins
        {
            get => _outputPins;
            set
            {
                _outputPins = value;
                OnPropertyChanged();
            }
        }

        public void UpdatePinState(string pinName, bool state, bool isInput)
        {
            var collection = isInput ? InputPins : OutputPins;
            var pin = collection.FirstOrDefault(p => p.Name == pinName);
            if (pin != null)
            {
                pin.State = state;
            }
        }
        // Add this method to be called when adding pins
        private void AddToLookup(PinViewModel pin)
        {
            _pinLookup[pin.Name] = pin;
        }
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ICommand TogglePinCommand { get; set; }
    }
}