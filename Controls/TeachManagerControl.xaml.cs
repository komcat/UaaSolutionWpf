using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using UaaSolutionWpf.Services;
using System.IO;
using UaaSolutionWpf.Motion;
using Serilog.Core;

namespace UaaSolutionWpf.Controls
{
    public partial class TeachManagerControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private ILogger _logger;
        private PositionRegistry _positionRegistry;
        private DevicePositionMonitor _deviceMonitor;
        private bool _isInitialized;
        private string _selectedDevice;
        private string _selectedPosition;

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

        public TeachManagerControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void Initialize(PositionRegistry positionRegistry, DevicePositionMonitor deviceMonitor, ILogger logger)
        {
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _deviceMonitor = deviceMonitor ?? throw new ArgumentNullException(nameof(deviceMonitor));
            _logger = logger?.ForContext<TeachManagerControl>() ?? throw new ArgumentNullException(nameof(logger));

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

        private string GetDeviceId(string deviceName) => deviceName switch
        {
            "Left Hexapod" => "hex-left",
            "Bottom Hexapod" => "hex-bottom",
            "Right Hexapod" => "hex-right",
            "Gantry" => "gantry-main",
            _ => throw new ArgumentException($"Unknown device: {deviceName}")
        };

        private int GetHexapodId(string deviceName) => deviceName switch
        {
            "Left Hexapod" => 0,
            "Bottom Hexapod" => 1,
            "Right Hexapod" => 2,
            _ => throw new ArgumentException($"Not a hexapod: {deviceName}")
        };

        private async void TeachButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedDevice) || string.IsNullOrEmpty(_selectedPosition))
            {
                MessageBox.Show("Please select both a device and position", "Validation Error");
                return;
            }

            try
            {
                string deviceId = GetDeviceId(_selectedDevice);
                var currentPosition = await _deviceMonitor.GetCurrentPosition(deviceId);

                // Update position
                var position = new Position
                {
                    X = currentPosition.X,
                    Y = currentPosition.Y,
                    Z = currentPosition.Z,
                    U = currentPosition.U,
                    V = currentPosition.V,
                    W = currentPosition.W
                };

                bool success = false;
                if (deviceId.StartsWith("hex"))
                {
                    int hexId = GetHexapodId(_selectedDevice);
                    success = _positionRegistry.UpdateHexapodPosition(hexId, _selectedPosition, position);
                }
                else if (deviceId.StartsWith("gantry"))
                {
                    success = _positionRegistry.UpdateGantryPosition(4, _selectedPosition, position);
                }

                if (success)
                {
                    string workingPositionsPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Config",
                        "WorkingPositions.json"
                    );

                    _positionRegistry.SaveToFile(workingPositionsPath);
                    _logger.Information($"Reloading {workingPositionsPath}");
                    _positionRegistry.ReloadPositions();

                    _logger.Information(
                        "Successfully taught position {Position} for {Device}",
                        _selectedPosition,
                        _selectedDevice
                    );

                    MessageBox.Show(
                        $"Successfully taught position {_selectedPosition}",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error teaching position {Position} for {Device}",
                    _selectedPosition, _selectedDevice);
                MessageBox.Show(
                    $"Error teaching position: {ex.Message}",
                    "Error",
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
        private void PositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                SelectedPosition = e.AddedItems[0] as string;
                _logger.Debug("Position selected: {Position}", SelectedPosition);
            }
        }
        public void Dispose()
        {
            // Save any pending changes
            try
            {
                string filePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "WorkingPositions.json"
                );
                _positionRegistry?.SaveToFile(filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving positions during disposal");
            }
        }
    }
}