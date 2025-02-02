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
            }
        }

        public SimpleJogControl()
        {
            InitializeComponent();
            DataContext = this;
            _logger = Log.ForContext<SimpleJogControl>();

            // Set default selection
            DeviceListBox.SelectedIndex = 0;
            StepListBox.SelectedIndex = 0;

            InitializeMicronStepItems();
            SetupEventHandlers();

            _logger.Information("Construct SimpleJogControl");
        }

        private void SetupEventHandlers()
        {
            // Device selection handler
            DeviceListBox.SelectionChanged += (s, e) =>
            {
                if (e.AddedItems.Count > 0)
                {
                    var item = e.AddedItems[0] as ListBoxItem;
                    SelectedDevice = item?.Content.ToString();
                }
            };

            // XYZ Translation buttons
            BtnLeft.Click += async (s, e) => await Move(new Vector3(-(float)selectedMicronStep, 0, 0));
            BtnRight.Click += async (s, e) => await Move(new Vector3((float)selectedMicronStep, 0, 0));
            BtnIn.Click += async (s, e) => await Move(new Vector3(0, (float)selectedMicronStep, 0));
            BtnOut.Click += async (s, e) => await Move(new Vector3(0, -(float)selectedMicronStep, 0));
            BtnUp.Click += async (s, e) => await Move(new Vector3(0, 0, (float)selectedMicronStep));
            BtnDown.Click += async (s, e) => await Move(new Vector3(0, 0, -(float)selectedMicronStep));

            // Step buttons for changing step size selection
            BtnStepPlus.Click += (s, e) => {
                var currentIndex = StepListBox.SelectedIndex;
                if (currentIndex < StepListBox.Items.Count - 1)
                {
                    StepListBox.SelectedIndex = currentIndex + 1;
                    var selectedItem = StepListBox.Items[currentIndex + 1] as MicronStepItem;
                    _logger.Information("Step size increased to: {StepSize} ({DisplayText})",
                        selectedItem.Value,
                        selectedItem.DisplayText);
                }
            };

            BtnStepMinus.Click += (s, e) => {
                var currentIndex = StepListBox.SelectedIndex;
                if (currentIndex > 0)
                {
                    StepListBox.SelectedIndex = currentIndex - 1;
                    var selectedItem = StepListBox.Items[currentIndex - 1] as MicronStepItem;
                    _logger.Information("Step size decreased to: {StepSize} ({DisplayText})",
                        selectedItem.Value,
                        selectedItem.DisplayText);
                }
            };

            // Rotation buttons
            BtnUPlus.Click += async (s, e) => await MoveRotation(new Vector3((float)selectedMicronStep, 0, 0));
            BtnUMinus.Click += async (s, e) => await MoveRotation(new Vector3(-(float)selectedMicronStep, 0, 0));
            BtnVPlus.Click += async (s, e) => await MoveRotation(new Vector3(0, (float)selectedMicronStep, 0));
            BtnVMinus.Click += async (s, e) => await MoveRotation(new Vector3(0, -(float)selectedMicronStep, 0));
            BtnWPlus.Click += async (s, e) => await MoveRotation(new Vector3(0, 0, (float)selectedMicronStep));
            BtnWMinus.Click += async (s, e) => await MoveRotation(new Vector3(0, 0, -(float)selectedMicronStep));
        }
        public void Initialize(
    HexapodMovementService leftHexapod,
    HexapodMovementService rightHexapod,
    HexapodMovementService bottomHexapod,
    GantryMovementService gantry,
    ILogger logger)
        {
            _logger = logger.ForContext<SimpleJogControl>();  // Add this line
            _jogController = new GlobalJogController(
                leftHexapod,
                rightHexapod,
                bottomHexapod,
                gantry,
                logger
            );
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

                // Determine which devices to move based on selection
                bool moveLeft = false, moveRight = false, moveBottom = false, moveGantry = false;

                switch (SelectedDevice?.ToLower())
                {
                    case "left hexapod":
                        moveLeft = true;
                        break;
                    case "right hexapod":
                        moveRight = true;
                        break;
                    case "bottom hexapod":
                        moveBottom = true;
                        break;
                    case "gantry":
                        moveGantry = true;
                        break;
                    default:
                        // If no specific device is selected, move all
                        moveLeft = moveRight = moveBottom = moveGantry = true;
                        break;
                }

                _logger.Information("Moving {Device} by {Movement}", SelectedDevice, movement);
                await _jogController.JogGlobal(movement, moveLeft, moveRight, moveBottom, moveGantry);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during jog movement");
                MessageBox.Show($"Error during movement: {ex.Message}", "Movement Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveRotation(Vector3 rotation)
        {
            try
            {
                // Only apply rotational movements to hexapods
                if (SelectedDevice?.ToLower().Contains("hexapod") == true)
                {
                    _logger.Information("Rotation requested for {Device}: {Rotation}", SelectedDevice, rotation);

                    // Determine which hexapod to rotate
                    bool moveLeft = SelectedDevice.ToLower().Contains("left");
                    bool moveRight = SelectedDevice.ToLower().Contains("right");
                    bool moveBottom = SelectedDevice.ToLower().Contains("bottom");

                    // For rotation movements, we pass Vector3.Zero for translation
                    await _jogController.JogGlobal(Vector3.Zero, moveLeft, moveRight, moveBottom, false);

                    // Note: Currently the GlobalJogController needs to be updated to handle rotations
                    // This is just a placeholder for the rotation implementation
                }
                else
                {
                    _logger.Warning("Rotation attempted on non-hexapod device: {Device}", SelectedDevice);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during rotation movement");
                MessageBox.Show($"Error during rotation: {ex.Message}", "Rotation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void InitializeMicronStepItems()
        {
            // First, clear any existing items
            StepListBox.Items.Clear();
            var micronSteps = new List<MicronStepItem>
            {

                new MicronStepItem { DisplayText = "1 micron", Value = 0.001 },
                new MicronStepItem { DisplayText = "5 micron", Value = 0.005 },
                new MicronStepItem { DisplayText = "10 micron", Value = 0.010 },
                new MicronStepItem { DisplayText = "50 micron", Value = 0.050 },
                new MicronStepItem { DisplayText = "100 micron", Value = 0.100 },
                new MicronStepItem { DisplayText = "200 micron", Value = 0.200 },
                new MicronStepItem { DisplayText = "500 micron", Value = 0.500 }
            };

            StepListBox.ItemsSource = micronSteps;
            StepListBox.SelectedIndex = 0; // Select the first item by default
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
            }
        }
    }
}