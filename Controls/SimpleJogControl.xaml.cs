using System;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Controls
{
    public partial class SimpleJogControl : UserControl, INotifyPropertyChanged
    {
        private double selectedMicronStep = 0.1;
        private string selectedDevice;
        private GlobalJogController _jogController;
        private ILogger _logger;

        public event PropertyChangedEventHandler PropertyChanged;

        public double StepSize
        {
            get => selectedMicronStep;
            set
            {
                selectedMicronStep = value;
                OnPropertyChanged();
            }
        }

        public string SelectedDevice
        {
            get => selectedDevice;
            set
            {
                selectedDevice = value;
                OnPropertyChanged();
                UpdateUVWButtonsState();
            }
        }

        public SimpleJogControl()
        {
            InitializeComponent();
            DataContext = this;
            _logger = Log.ForContext<SimpleJogControl>();

            InitializeMicronStepItems();
            SetupEventHandlers();

            // Set initial device selection
            DeviceListBox.SelectedIndex = 0;
            SelectedDevice = (DeviceListBox.SelectedItem as ListBoxItem)?.Content.ToString();

            // Set initial step size selection
            StepListBox.SelectedIndex = 0;

            // Update UVW button states based on initial selection
            UpdateUVWButtonsState();
        }

        private void UpdateUVWButtonsState()
        {
            bool isHexapod = SelectedDevice?.ToLower().Contains("hexapod") ?? false;

            // Update button states
            bool enableRotation = isHexapod;
            if (BtnUPlus != null)
            {
                BtnUPlus.IsEnabled = enableRotation;
                BtnUMinus.IsEnabled = enableRotation;
                BtnVPlus.IsEnabled = enableRotation;
                BtnVMinus.IsEnabled = enableRotation;
                BtnWPlus.IsEnabled = enableRotation;
                BtnWMinus.IsEnabled = enableRotation;

                // Update visual appearance
                double opacity = enableRotation ? 1.0 : 0.5;
                BtnUPlus.Opacity = opacity;
                BtnUMinus.Opacity = opacity;
                BtnVPlus.Opacity = opacity;
                BtnVMinus.Opacity = opacity;
                BtnWPlus.Opacity = opacity;
                BtnWMinus.Opacity = opacity;
            }

            _logger.Debug("Updated UVW button states for device: {Device}, Rotation enabled: {EnableRotation}",
                SelectedDevice, enableRotation);
        }

        private void SetupEventHandlers()
        {
            // Device selection
            DeviceListBox.SelectionChanged += (s, e) =>
            {
                if (e.AddedItems.Count > 0 && e.AddedItems[0] is ListBoxItem item)
                {
                    SelectedDevice = item.Content.ToString();
                    _logger.Information("Device selection changed to: {Device}", SelectedDevice);
                }
            };

            // Translation movements (XYZ)
            BtnLeft.Click += async (s, e) => await Move(new Vector3(-(float)selectedMicronStep, 0, 0));
            BtnRight.Click += async (s, e) => await Move(new Vector3((float)selectedMicronStep, 0, 0));
            BtnIn.Click += async (s, e) => await Move(new Vector3(0, (float)selectedMicronStep, 0));
            BtnOut.Click += async (s, e) => await Move(new Vector3(0, -(float)selectedMicronStep, 0));
            BtnUp.Click += async (s, e) => await Move(new Vector3(0, 0, (float)selectedMicronStep));
            BtnDown.Click += async (s, e) => await Move(new Vector3(0, 0, -(float)selectedMicronStep));

            // Rotation movements (UVW)
            BtnUPlus.Click += async (s, e) => await MoveRotation(new Vector3((float)selectedMicronStep, 0, 0));
            BtnUMinus.Click += async (s, e) => await MoveRotation(new Vector3(-(float)selectedMicronStep, 0, 0));
            BtnVPlus.Click += async (s, e) => await MoveRotation(new Vector3(0, (float)selectedMicronStep, 0));
            BtnVMinus.Click += async (s, e) => await MoveRotation(new Vector3(0, -(float)selectedMicronStep, 0));
            BtnWPlus.Click += async (s, e) => await MoveRotation(new Vector3(0, 0, (float)selectedMicronStep));
            BtnWMinus.Click += async (s, e) => await MoveRotation(new Vector3(0, 0, -(float)selectedMicronStep));

            // Step size controls
            BtnStepPlus.Click += (s, e) =>
            {
                var currentIndex = StepListBox.SelectedIndex;
                if (currentIndex < StepListBox.Items.Count - 1)
                {
                    StepListBox.SelectedIndex = currentIndex + 1;
                    var selectedItem = StepListBox.SelectedItem as MicronStepItem;
                    _logger.Information("Step size increased to: {StepSize} ({DisplayText})",
                        selectedItem?.Value, selectedItem?.DisplayText);
                }
            };

            BtnStepMinus.Click += (s, e) =>
            {
                var currentIndex = StepListBox.SelectedIndex;
                if (currentIndex > 0)
                {
                    StepListBox.SelectedIndex = currentIndex - 1;
                    var selectedItem = StepListBox.SelectedItem as MicronStepItem;
                    _logger.Information("Step size decreased to: {StepSize} ({DisplayText})",
                        selectedItem?.Value, selectedItem?.DisplayText);
                }
            };
        }

        public void Initialize(
            HexapodMovementService rightHexapod,
            GantryMovementService gantry,
            ILogger logger,
            HexapodMovementService leftHexapod = null,
            HexapodMovementService bottomHexapod = null)
        {
            _logger = logger.ForContext<SimpleJogControl>();
            _jogController = new GlobalJogController(
                rightHexapod,
                gantry,
                logger,
                bottomHexapod,
                leftHexapod
            );

            UpdateUVWButtonsState();
            _logger.Information("SimpleJogControl initialized with all movement services");
        }

        private async Task Move(Vector3 movement)
        {
            try
            {
                if (_jogController == null)
                {
                    MessageBox.Show("Jog controller not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool moveLeft = false, moveRight = false, moveBottom = false, moveGantry = false;

                switch (SelectedDevice?.ToLower())
                {
                    case var device when device?.Contains("left hexapod") == true:
                        moveLeft = true;
                        break;
                    case var device when device?.Contains("right hexapod") == true:
                        moveRight = true;
                        break;
                    case var device when device?.Contains("bottom hexapod") == true:
                        moveBottom = true;
                        break;
                    case var device when device?.Contains("gantry") == true:
                        moveGantry = true;
                        break;
                    default:
                        moveLeft = moveRight = moveBottom = moveGantry = true;
                        break;
                }

                _logger.Information("Moving {Device} by {Movement}", SelectedDevice, movement);
                await _jogController.JogGlobal(movement, moveLeft, moveRight, moveBottom, moveGantry);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during movement");
                MessageBox.Show($"Error during movement: {ex.Message}", "Movement Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveRotation(Vector3 rotation)
        {
            try
            {
                if (_jogController == null)
                {
                    MessageBox.Show("Jog controller not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!SelectedDevice?.ToLower().Contains("hexapod") ?? true)
                {
                    _logger.Warning("Rotation attempted on non-hexapod device: {Device}", SelectedDevice);
                    return;
                }

                bool moveLeft = SelectedDevice.ToLower().Contains("left");
                bool moveRight = SelectedDevice.ToLower().Contains("right");
                bool moveBottom = SelectedDevice.ToLower().Contains("bottom");

                _logger.Information("Rotating {Device} by {Rotation}", SelectedDevice, rotation);
                await _jogController.JogRotation(rotation, moveLeft, moveRight, moveBottom);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during rotation");
                MessageBox.Show($"Error during rotation: {ex.Message}", "Rotation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeMicronStepItems()
        {
            StepListBox.Items.Clear();
            var micronSteps = new List<MicronStepItem>
            {
                new MicronStepItem { DisplayText = "0.1 micron", Value = 0.0001 },
                new MicronStepItem { DisplayText = "0.2 micron", Value = 0.0002 },
                new MicronStepItem { DisplayText = "0.5 micron", Value = 0.0005 },
                new MicronStepItem { DisplayText = "1 micron", Value = 0.001 },
                new MicronStepItem { DisplayText = "5 micron", Value = 0.005 },
                new MicronStepItem { DisplayText = "10 micron", Value = 0.010 },
                new MicronStepItem { DisplayText = "50 micron", Value = 0.050 },
                new MicronStepItem { DisplayText = "100 micron", Value = 0.100 },
                new MicronStepItem { DisplayText = "200 micron", Value = 0.200 },
                new MicronStepItem { DisplayText = "500 micron", Value = 0.500 },
                new MicronStepItem { DisplayText = "1000 micron", Value = 1.000 }
            };

            StepListBox.ItemsSource = micronSteps;
            StepListBox.SelectedIndex = 0;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void StepListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StepListBox.SelectedItem is MicronStepItem selectedItem)
            {
                selectedMicronStep = selectedItem.Value;
                _logger.Debug("Step size changed to: {StepSize}", selectedMicronStep);
            }
        }
    }
}