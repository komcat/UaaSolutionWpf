using Serilog;
using System.Windows.Controls;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Controls
{
    public partial class PneumaticSlideControl : UserControl
    {
        private PneumaticSlidesViewModel _viewModel;
        private ILogger _logger;
        private IOManager _ioManager;

        public PneumaticSlideControl()
        {
            InitializeComponent();
        }

        public void Initialize(IOManager ioManager, ILogger logger)
        {
            _ioManager = ioManager;
            _logger = logger;
            _viewModel = new PneumaticSlidesViewModel(ioManager, logger);
            DataContext = _viewModel;
        }

        public void UpdateSensorState(string sensorName, bool state)
        {
            // Map sensor names to the appropriate slide and update its state
            switch (sensorName)
            {
                case "UV_Head_Up":
                    if (state) _viewModel.UVSlide.State = SlideState.Up;
                    break;
                case "UV_Head_Down":
                    if (state) _viewModel.UVSlide.State = SlideState.Down;
                    break;
                case "Dispenser_Head_Up":
                    if (state) _viewModel.DispenserSlide.State = SlideState.Up;
                    break;
                case "Dispenser_Head_Down":
                    if (state) _viewModel.DispenserSlide.State = SlideState.Down;
                    break;
                case "Pick_Up_Tool_Up":
                    if (state) _viewModel.PickUpToolSlide.State = SlideState.Up;
                    break;
                case "Pick_Up_Tool_Down":
                    if (state) _viewModel.PickUpToolSlide.State = SlideState.Down;
                    break;
            }

            // If both sensors are off, set state to Unknown
            if (!state)
            {
                if (sensorName.StartsWith("UV_Head"))
                {
                    var otherSensorState = _viewModel.UVSlide.State;
                    if (otherSensorState == SlideState.Unknown)
                        _viewModel.UVSlide.State = SlideState.Unknown;
                }
                else if (sensorName.StartsWith("Dispenser_Head"))
                {
                    var otherSensorState = _viewModel.DispenserSlide.State;
                    if (otherSensorState == SlideState.Unknown)
                        _viewModel.DispenserSlide.State = SlideState.Unknown;
                }
                else if (sensorName.StartsWith("Pick_Up_Tool"))
                {
                    var otherSensorState = _viewModel.PickUpToolSlide.State;
                    if (otherSensorState == SlideState.Unknown)
                        _viewModel.PickUpToolSlide.State = SlideState.Unknown;
                }
            }
        }

    }
}