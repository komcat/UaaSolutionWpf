using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace UaaSolutionWpf.Controls
{
    public partial class GantryControl : UserControl, INotifyPropertyChanged
    {
        private string robotName = "Gantry1";
        private bool isConnected = false;
        private string ipAddress = "192.168.0.10";
        private int portNumber = 50000;
        private double xPosition = -6.0900;
        private double yPosition = 2.9300;
        private double zPosition = 1.2000;
        private bool isXEnabled = false;
        private bool isYEnabled = false;
        private bool isZEnabled = false;
        private double selectedStepSize = 0.1;

        public bool IsXEnabled
        {
            get => isXEnabled;
            set
            {
                isXEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool IsYEnabled
        {
            get => isYEnabled;
            set
            {
                isYEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool IsZEnabled
        {
            get => isZEnabled;
            set
            {
                isZEnabled = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public GantryControl()
        {
            InitializeComponent();
            DataContext = this;
        }

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

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnXPlusClick(object sender, RoutedEventArgs e)
        {
            XPosition += selectedStepSize;
        }

        private void OnXMinusClick(object sender, RoutedEventArgs e)
        {
            XPosition -= selectedStepSize;
        }

        private void OnYPlusClick(object sender, RoutedEventArgs e)
        {
            YPosition += selectedStepSize;
        }

        private void OnYMinusClick(object sender, RoutedEventArgs e)
        {
            YPosition -= selectedStepSize;
        }

        private void OnZPlusClick(object sender, RoutedEventArgs e)
        {
            ZPosition += selectedStepSize;
        }

        private void OnZMinusClick(object sender, RoutedEventArgs e)
        {
            ZPosition -= selectedStepSize;
        }

        private void OnXEnableClick(object sender, RoutedEventArgs e)
        {
            IsXEnabled = !IsXEnabled;
            EnableXButton.Background = IsXEnabled ?
                System.Windows.Media.Brushes.LightGreen :
                System.Windows.Media.Brushes.LightGray;
        }

        private void OnYEnableClick(object sender, RoutedEventArgs e)
        {
            IsYEnabled = !IsYEnabled;
            EnableYButton.Background = IsYEnabled ?
                System.Windows.Media.Brushes.LightGreen :
                System.Windows.Media.Brushes.LightGray;
        }

        private void OnZEnableClick(object sender, RoutedEventArgs e)
        {
            IsZEnabled = !IsZEnabled;
            EnableZButton.Background = IsZEnabled ?
                System.Windows.Media.Brushes.LightGreen :
                System.Windows.Media.Brushes.LightGray;
        }

        private void OnStepSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                var item = e.AddedItems[0] as ListBoxItem;
                if (item != null)
                {
                    string content = item.Content.ToString();
                    selectedStepSize = double.Parse(content.Split(' ')[0]);
                }
            }
        }
    }
}