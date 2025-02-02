using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Interaction logic for HexapodControl.xaml
    /// </summary>
    public partial class HexapodControl : UserControl, INotifyPropertyChanged
    {
        private string robotName = "Hexapod: ";
        private string ipAddress = "192.168.0.10";
        private bool isConnected = false;
        private int portNumber = 50000;
        private double xPosition = -6.0900;
        private double yPosition = 2.9300;
        private double zPosition = 1.2000;
        private double uPosition = -2.0000;
        private double vPosition = 0.0000;
        private double wPosition = 0.0000;
        private double selectedMicronStep = 0.1;

        public event PropertyChangedEventHandler ? PropertyChanged;

        public string RobotName
        {
            get => robotName;
            set
            {
                robotName = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnected
        {
            get => isConnected;
            set
            {
                isConnected = value;
                OnPropertyChanged();
            }
        }

        public string IpAddress
        {
            get => ipAddress;
            set
            {
                ipAddress = value;
                OnPropertyChanged();
            }
        }

        public int PortNumber
        {
            get => portNumber;
            set
            {
                portNumber = value;
                OnPropertyChanged();
            }
        }

        public double XPosition
        {
            get => xPosition;
            set
            {
                xPosition = value;
                OnPropertyChanged();
            }
        }

        public double YPosition
        {
            get => yPosition;
            set
            {
                yPosition = value;
                OnPropertyChanged();
            }
        }

        public double ZPosition
        {
            get => zPosition;
            set
            {
                zPosition = value;
                OnPropertyChanged();
            }
        }

        public double UPosition
        {
            get => uPosition;
            set
            {
                uPosition = value;
                OnPropertyChanged();
            }
        }

        public double VPosition
        {
            get => vPosition;
            set
            {
                vPosition = value;
                OnPropertyChanged();
            }
        }

        public double WPosition
        {
            get => wPosition;
            set
            {
                wPosition = value;
                OnPropertyChanged();
            }
        }

        // Event handlers for position updates from the robot
        public delegate void PositionUpdateHandler(double newPosition);
        public event PositionUpdateHandler XPositionUpdated;
        public event PositionUpdateHandler YPositionUpdated;
        public event PositionUpdateHandler ZPositionUpdated;
        public event PositionUpdateHandler UPositionUpdated;
        public event PositionUpdateHandler VPositionUpdated;
        public event PositionUpdateHandler WPositionUpdated;

        public HexapodControl()
        {
            InitializeComponent();
            DataContext = this;
            InitializeMicronStepItems();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void MoveAxis(string axis, bool positive)
        {
            // This method can be called from the robot control class
            switch (axis)
            {
                case "X":
                    XPosition += positive ? selectedMicronStep : -selectedMicronStep;
                    XPositionUpdated?.Invoke(XPosition);
                    break;
                case "Y":
                    YPosition += positive ? selectedMicronStep : -selectedMicronStep;
                    YPositionUpdated?.Invoke(YPosition);
                    break;
                case "Z":
                    ZPosition += positive ? selectedMicronStep : -selectedMicronStep;
                    ZPositionUpdated?.Invoke(ZPosition);
                    break;
                case "U":
                    UPosition += positive ? selectedMicronStep : -selectedMicronStep;
                    UPositionUpdated?.Invoke(UPosition);
                    break;
                case "V":
                    VPosition += positive ? selectedMicronStep : -selectedMicronStep;
                    VPositionUpdated?.Invoke(VPosition);
                    break;
                case "W":
                    WPosition += positive ? selectedMicronStep : -selectedMicronStep;
                    WPositionUpdated?.Invoke(WPosition);
                    break;
            }
        }

        // Public methods for updating positions from the robot
        public void UpdateXPosition(double newPosition)
        {
            XPosition = newPosition;
        }

        public void UpdateYPosition(double newPosition)
        {
            YPosition = newPosition;
        }

        public void UpdateZPosition(double newPosition)
        {
            ZPosition = newPosition;
        }

        public void UpdateUPosition(double newPosition)
        {
            UPosition = newPosition;
        }

        public void UpdateVPosition(double newPosition)
        {
            VPosition = newPosition;
        }

        public void UpdateWPosition(double newPosition)
        {
            WPosition = newPosition;
        }



        private void InitializeMicronStepItems()
        {
            // First, clear any existing items
            StepListBox.Items.Clear();
            var micronSteps = new List<MicronStepItem>
            {
                new MicronStepItem { DisplayText = "0.1 micron", Value = 0.0001 },
                new MicronStepItem { DisplayText = "0.2 micron", Value = 0.0002 },
                new MicronStepItem { DisplayText = "0.5 micron", Value = 0.0005 },
                new MicronStepItem { DisplayText = "1 micron", Value = 0.001 },
                new MicronStepItem { DisplayText = "2 micron", Value = 0.002 },
                new MicronStepItem { DisplayText = "3 micron", Value = 0.003 },
                new MicronStepItem { DisplayText = "4 micron", Value = 0.004 },
                new MicronStepItem { DisplayText = "5 micron", Value = 0.005 },
                new MicronStepItem { DisplayText = "10 micron", Value = 0.010 },
                new MicronStepItem { DisplayText = "20 micron", Value = 0.020 },
                new MicronStepItem { DisplayText = "50 micron", Value = 0.050 },
                new MicronStepItem { DisplayText = "100 micron", Value = 0.100 },
                new MicronStepItem { DisplayText = "200 micron", Value = 0.200 },
                new MicronStepItem { DisplayText = "500 micron", Value = 0.500 }
            };

            StepListBox.ItemsSource = micronSteps;
            StepListBox.SelectedIndex = 0; // Select the first item by default
        }

        private void StepListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StepListBox.SelectedItem is MicronStepItem selectedItem)
            {
                selectedMicronStep = selectedItem.Value;
            }
        }
    }
    public class MicronStepItem
    {
        public string DisplayText { get; set; }
        public double Value { get; set; }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}
