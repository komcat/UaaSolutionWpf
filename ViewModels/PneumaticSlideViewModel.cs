using Serilog;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.ViewModels
{
    public enum SlideState
    {
        Unknown,
        Up,
        Down
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();
    }

    public class PneumaticSlidesViewModel : ViewModelBase
    {
        public PneumaticSlideViewModel UVSlide { get; set; }
        public PneumaticSlideViewModel DispenserSlide { get; set; }
        public PneumaticSlideViewModel PickUpToolSlide { get; set; }

        private readonly ILogger _logger;
        private readonly IOManager _ioManager;

        public PneumaticSlidesViewModel()
        {
            // Default constructor for design-time support
        }

        public PneumaticSlidesViewModel(IOManager ioManager, ILogger logger)
        {
            _ioManager = ioManager;
            _logger = logger;

            // Initialize slides with proper IO mappings
            InitializeSlides();
        }

        private void InitializeSlides()
        {
            // UV Head slide configuration
            UVSlide = new PneumaticSlideViewModel(
                "UV Head",
                _ioManager,
                _logger,
                "IOBottom",           // Device name from IOConfig.json
                "UV_Head",            // Output name for the head
                "",                   // No down output needed
                "UV_Head_Up",         // Input sensor name for up position
                "UV_Head_Down"        // Input sensor name for down position
            );

            // Dispenser slide configuration
            DispenserSlide = new PneumaticSlideViewModel(
                "Dispenser Head",
                _ioManager,
                _logger,
                "IOBottom",
                "Dispenser_Head",
                "",
                "Dispenser_Head_Up",
                "Dispenser_Head_Down"
            );

            // Pick Up Tool slide configuration
            PickUpToolSlide = new PneumaticSlideViewModel(
                "Pick Up Tool",
                _ioManager,
                _logger,
                "IOBottom",
                "Pick_Up_Tool",
                "",
                "Pick_Up_Tool_Up",
                "Pick_Up_Tool_Down"
            );
        }
    }

    public class PneumaticSlideViewModel : ViewModelBase
    {
        private string _name;
        private SlideState _state;
        private readonly IOManager _ioManager;
        private readonly ILogger _logger;
        private readonly string _deviceName;
        private readonly string _upOutputName;
        private readonly string _downOutputName;
        private readonly string _upSensorName;
        private readonly string _downSensorName;

        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public SlideState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        public PneumaticSlideViewModel(
            string name,
            IOManager ioManager,
            ILogger logger,
            string deviceName,
            string upOutputName,
            string downOutputName,
            string upSensorName,
            string downSensorName)
        {
            _name = name;
            _ioManager = ioManager;
            _logger = logger;
            _deviceName = deviceName;
            _upOutputName = upOutputName;
            _downOutputName = downOutputName;
            _upSensorName = upSensorName;
            _downSensorName = downSensorName;
            _state = SlideState.Unknown;

            MoveUpCommand = new RelayCommand(() => MoveSlide(true));
            MoveDownCommand = new RelayCommand(() => MoveSlide(false));

            // Subscribe to IO state changes
            _ioManager.IOStateChanged += OnIOStateChanged;
        }

        private void MoveSlide(bool up)
        {
            try
            {
                // Clear both outputs first if we're using two outputs
                if (!string.IsNullOrEmpty(_downOutputName))
                {
                    _ioManager.ClearOutput(_deviceName, _upOutputName);
                    _ioManager.ClearOutput(_deviceName, _downOutputName);
                }

                // For single output control, true = up, false = down
                bool targetState = up;
                var outputName = !string.IsNullOrEmpty(_downOutputName) ?
                    (up ? _upOutputName : _downOutputName) :
                    _upOutputName;

                if (targetState)
                {
                    _ioManager.ClearOutput(_deviceName, outputName);
                }
                else
                {
                    _ioManager.SetOutput(_deviceName, outputName);
                }

                _logger.Information($"Moving {Name} {(up ? "up" : "down")}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error moving {Name} {(up ? "up" : "down")}");
            }
        }

        // This is the method we'll use to update sensor states
        private void OnIOStateChanged(object sender, IOStateEventArgs e)
        {
            if (e.DeviceName != _deviceName || !e.IsInput) return;

            if (e.PinName == _upSensorName && e.State)
            {
                State = SlideState.Up;
            }
            else if (e.PinName == _downSensorName && e.State)
            {
                State = SlideState.Down;
            }
            else if (!e.State && (e.PinName == _upSensorName || e.PinName == _downSensorName))
            {
                State = SlideState.Unknown;
            }
        }
    }
}