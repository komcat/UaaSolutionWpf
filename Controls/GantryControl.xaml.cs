using Serilog;
using Serilog.Core;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using UaaSolutionWpf.Services;

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

        public event PropertyChangedEventHandler? PropertyChanged;
        private GantryMovementService _movementService;
        private readonly ILogger _logger;

        public GantryControl()
        {
            InitializeComponent();
            DataContext = this;
            InitializeMicronStepItems();
            _logger = Log.ForContext<GantryControl>();
        }

        public void SetDependencies(GantryMovementService movementService)
        {
            _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
            
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

        private async void OnXPlusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _movementService.MoveRelativeAsync((int)GantryMovementService.Axis.X, selectedStepSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move X axis positive");
                MessageBox.Show("Failed to move X axis: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnXMinusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _movementService.MoveRelativeAsync((int)GantryMovementService.Axis.X, -selectedStepSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move X axis negative");
                MessageBox.Show("Failed to move X axis: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnYPlusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _movementService.MoveRelativeAsync((int)GantryMovementService.Axis.Y, selectedStepSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move Y axis positive");
                MessageBox.Show("Failed to move Y axis: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnYMinusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _movementService.MoveRelativeAsync((int)GantryMovementService.Axis.Y, -selectedStepSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move Y axis negative");
                MessageBox.Show("Failed to move Y axis: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnZPlusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _movementService.MoveRelativeAsync((int)GantryMovementService.Axis.Z, selectedStepSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move Z axis positive");
                MessageBox.Show("Failed to move Z axis: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnZMinusClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await _movementService.MoveRelativeAsync((int)GantryMovementService.Axis.Z, -selectedStepSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move Z axis negative");
                MessageBox.Show("Failed to move Z axis: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            if (StepListBox.SelectedItem is MicronStepItem selectedItem)
            {
                selectedStepSize = selectedItem.Value;
            }
        }

        private void InitializeMicronStepItems()
        {
            // First, clear any existing items
            StepListBox.Items.Clear();
            var micronSteps = new List<MicronStepItem>
            {
                new MicronStepItem { DisplayText = "1 micron", Value = 0.001 },
                new MicronStepItem { DisplayText = "10 micron", Value = 0.010 },
                new MicronStepItem { DisplayText = "50 micron", Value = 0.050 },
                new MicronStepItem { DisplayText = "100 micron", Value = 0.100 },
                new MicronStepItem { DisplayText = "500 micron", Value = 0.500 },
                new MicronStepItem { DisplayText = "1 mm", Value = 1.000},
                new MicronStepItem { DisplayText = "5 mm", Value = 5.000},
                new MicronStepItem { DisplayText = "10 mm", Value = 10.000}
            };

            StepListBox.ItemsSource = micronSteps;
            StepListBox.SelectedIndex = 0; // Select the first item by default
        }
    }
}