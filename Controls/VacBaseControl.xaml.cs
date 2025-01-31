using System.Windows.Controls;

namespace UaaSolutionWpf.Controls
{
    public partial class VacBaseControl : UserControl
    {
        public VacBaseControl()
        {
            InitializeComponent();

            // Add click event handlers
            VacBaseOnButton.Click += (s, e) => VacBaseState = true;
            VacBaseOffButton.Click += (s, e) => VacBaseState = false;
        }

        private bool _vacBaseState;
        public bool VacBaseState
        {
            get => _vacBaseState;
            set
            {
                _vacBaseState = value;
                UpdateButtonStates();
            }
        }

        private void UpdateButtonStates()
        {
            VacBaseOnButton.Background = VacBaseState ?
                System.Windows.Media.Brushes.DodgerBlue :
                System.Windows.Media.Brushes.LightGray;

            VacBaseOffButton.Background = !VacBaseState ?
                System.Windows.Media.Brushes.LightGray :
                System.Windows.Media.Brushes.Transparent;
        }
    }
}