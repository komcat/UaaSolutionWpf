using System.Windows.Controls;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Controls
{
    public partial class PneumaticSlideControl : UserControl
    {
        private PneumaticSlidesViewModel _viewModel;

        public PneumaticSlideControl()
        {
            InitializeComponent();
            _viewModel = new PneumaticSlidesViewModel();
            DataContext = _viewModel;
        }

        public void UpdateSensorState(string sensorName, bool state)
        {
            _viewModel.UpdateSensorState(sensorName, state);
        }
    }
}