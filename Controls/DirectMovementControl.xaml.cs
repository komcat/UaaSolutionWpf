using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using UaaSolutionWpf.Services;
using System.Threading.Tasks;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Hexapod;
using UaaSolutionWpf.Gantry;

namespace UaaSolutionWpf.Controls
{
    public partial class DirectMovementControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private ILogger _logger;
        private PositionRegistry _positionRegistry;
        private HexapodConnectionManager _hexapodManager;
        private AcsGantryConnectionManager _gantryManager;
        private bool _isInitialized;
        private string _selectedDevice;
        private string _selectedPosition;
        private bool _disposed;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<string> PositionsList { get; } = new ObservableCollection<string>();

        public string SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice != value)
                {
                    _selectedDevice = value;
                    OnPropertyChanged();
                    UpdatePositionsList();
                }
            }
        }

        public string SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                if (_selectedPosition != value)
                {
                    _selectedPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public DirectMovementControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void Initialize(
            PositionRegistry positionRegistry,
            HexapodConnectionManager hexapodManager,
            AcsGantryConnectionManager gantryManager,
            ILogger logger)
        {
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _hexapodManager = hexapodManager ?? throw new ArgumentNullException(nameof(hexapodManager));
            _gantryManager = gantryManager ?? throw new ArgumentNullException(nameof(gantryManager));
            _logger = logger?.ForContext<DirectMovementControl>() ?? throw new ArgumentNullException(nameof(logger));

            // Initialize UI state
            DeviceComboBox.SelectedIndex = 0;
            _isInitialized = true;

            UpdatePositionsList();
        }

        private void UpdatePositionsList()
        {
            if (!_isInitialized) return;

            PositionsList.Clear();
            try
            {
                switch (_selectedDevice)
                {
                    case "Left Hexapod":
                        foreach (var pos in _positionRegistry.GetAllHexapodPositions(0))
                            PositionsList.Add(pos.Key);
                        break;
                    case "Bottom Hexapod":
                        foreach (var pos in _positionRegistry.GetAllHexapodPositions(1))
                            PositionsList.Add(pos.Key);
                        break;
                    case "Right Hexapod":
                        foreach (var pos in _positionRegistry.GetAllHexapodPositions(2))
                            PositionsList.Add(pos.Key);
                        break;
                    case "Gantry":
                        foreach (var pos in _positionRegistry.GetAllGantryPositions(4))
                            PositionsList.Add(pos.Key);
                        break;
                }

                PositionComboBox.ItemsSource = PositionsList;
                if (PositionsList.Count > 0)
                    PositionComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating positions list for {Device}", _selectedDevice);
                MessageBox.Show($"Error loading positions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private HexapodConnectionManager.HexapodType GetHexapodType(string deviceName) => deviceName switch
        {
            "Left Hexapod" => HexapodConnectionManager.HexapodType.Left,
            "Bottom Hexapod" => HexapodConnectionManager.HexapodType.Bottom,
            "Right Hexapod" => HexapodConnectionManager.HexapodType.Right,
            _ => throw new ArgumentException($"Not a hexapod: {deviceName}")
        };

        private int GetHexapodId(string deviceName) => deviceName switch
        {
            "Left Hexapod" => 0,
            "Bottom Hexapod" => 1,
            "Right Hexapod" => 2,
            _ => throw new ArgumentException($"Not a hexapod: {deviceName}")
        };

        private async void MoveToButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedDevice) || string.IsNullOrEmpty(_selectedPosition))
            {
                MessageBox.Show("Please select both a device and position", "Validation Error");
                return;
            }

            try
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to move {_selectedDevice} directly to position {_selectedPosition}?\n\n" +
                    "This will bypass motion graph safety checks!",
                    "Confirm Direct Movement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                    return;

                Position targetPosition;

                if (_selectedDevice.Contains("Hexapod"))
                {
                    int hexId = GetHexapodId(_selectedDevice);
                    if (_positionRegistry.TryGetHexapodPosition(hexId, _selectedPosition, out targetPosition))
                    {
                        var hexType = GetHexapodType(_selectedDevice);
                        var controller = _hexapodManager.GetHexapodController(hexType);

                        if (controller != null)
                        {
                            var targetPos = new double[]
                            {
                                targetPosition.X,
                                targetPosition.Y,
                                targetPosition.Z,
                                targetPosition.U,
                                targetPosition.V,
                                targetPosition.W
                            };

                            _logger.Information(
                                "Moving {Device} directly to position {Position}: X={X:F4}, Y={Y:F4}, Z={Z:F4}, U={U:F4}, V={V:F4}, W={W:F4}",
                                _selectedDevice, _selectedPosition,
                                targetPosition.X, targetPosition.Y, targetPosition.Z,
                                targetPosition.U, targetPosition.V, targetPosition.W
                            );

                            MoveToButton.IsEnabled = false;
                            try
                            {
                                await controller.MoveToAbsoluteTarget(targetPos);
                                _logger.Information("Successfully moved {Device} to position {Position}",
                                    _selectedDevice, _selectedPosition);
                            }
                            finally
                            {
                                MoveToButton.IsEnabled = true;
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"No controller found for {_selectedDevice}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Position {_selectedPosition} not found for {_selectedDevice}");
                    }
                }
                else if (_selectedDevice == "Gantry")
                {
                    if (_positionRegistry.TryGetGantryPosition(4, _selectedPosition, out targetPosition))
                    {
                        _logger.Information(
                            "Moving Gantry directly to position {Position}: X={X:F4}, Y={Y:F4}, Z={Z:F4}",
                            _selectedPosition, targetPosition.X, targetPosition.Y, targetPosition.Z
                        );

                        MoveToButton.IsEnabled = false;
                        try
                        {
                            // Move each axis sequentially
                            await _gantryManager.MoveToAbsolutePositionAsync(0, targetPosition.X);
                            await _gantryManager.MoveToAbsolutePositionAsync(1, targetPosition.Y);
                            await _gantryManager.MoveToAbsolutePositionAsync(2, targetPosition.Z);

                            _logger.Information("Successfully moved Gantry to position {Position}",
                                _selectedPosition);
                        }
                        finally
                        {
                            MoveToButton.IsEnabled = true;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Position {_selectedPosition} not found for Gantry");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving {Device} to position {Position}",
                    _selectedDevice, _selectedPosition);
                MessageBox.Show(
                    $"Error during movement: {ex.Message}",
                    "Movement Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ComboBoxItem item)
            {
                SelectedDevice = item.Content.ToString();
                _logger.Debug("Device selected: {Device}", SelectedDevice);
            }
        }

        private void PositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                SelectedPosition = e.AddedItems[0] as string;
                _logger.Debug("Position selected: {Position}", SelectedPosition);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatePositionsList();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}