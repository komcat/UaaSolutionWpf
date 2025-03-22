using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using DataClient;
using DataClient.Configuration;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {
        // Step 1: Add these fields to the VisionMotionWindow class
        private ConfigManager _configManager;
        private DataServerManager _serverManager;
        private Dictionary<string, ImprovedServerStatusControl> _serverStatusControls;



        // Step 2: Add this method to initialize the DataClient components
        private void InitializeDataClient()
        {
            try
            {
                _logger.Information("Initializing Data Client components");

                // Create the configuration manager with default path
                _configManager = new ConfigManager("Config/DataServerConfig.json");

                // Create the server manager
                _serverManager = new DataServerManager(_configManager);

                // Subscribe to events
                _serverManager.DataReceived += ServerManager_DataReceived;
                _serverManager.ConnectionStatusChanged += ServerManager_ConnectionStatusChanged;
                _serverManager.DataRateUpdated += ServerManager_DataRateUpdated;

                // Initialize dictionary for server controls
                _serverStatusControls = new Dictionary<string, ImprovedServerStatusControl>();

                // Initialize the server manager
                _ = InitializeServerManagerAsync();

                _logger.Information("Data Client components initialized");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing Data Client");
                MessageBox.Show($"Error initializing Data Client: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // Step 3: Add method to initialize server manager asynchronously
        private async Task InitializeServerManagerAsync()
        {
            try
            {
                // Load configuration
                await _configManager.LoadConfigAsync();

                // Initialize server manager
                await _serverManager.InitializeAsync();

                // Update UI on UI thread
                Dispatcher.Invoke(() => {
                    // Populate server list
                    PopulateServerList();
                });

                // Connect to auto-connect servers
                await ConnectToAutoConnectServers();

                _logger.Information("Server manager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in InitializeServerManagerAsync");
                Dispatcher.Invoke(() => {
                    MessageBox.Show($"Error initializing server manager: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // Step 4: Add method to populate server list
        private void PopulateServerList()
        {
            // Clear existing controls
            ServerListPanel.Children.Clear();
            _serverStatusControls.Clear();

            // Add controls for each server in configuration
            foreach (var serverConfig in _serverManager.GetAllServerConfigs())
            {
                // Create improved server status control
                var serverControl = new ImprovedServerStatusControl(serverConfig);

                // Subscribe to events
                serverControl.ConnectRequested += ServerControl_ConnectRequested;
                serverControl.DisconnectRequested += ServerControl_DisconnectRequested;

                // Add to panel and dictionary
                ServerListPanel.Children.Add(serverControl);
                _serverStatusControls[serverConfig.Id] = serverControl;
            }
        }

        // Step 5: Add method to connect to auto-connect servers
        private async Task ConnectToAutoConnectServers()
        {
            try
            {
                await _serverManager.ConnectToAllAsync(autoConnectOnly: true);
                _logger.Information("Connected to auto-connect servers");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to auto-connect servers");
                Dispatcher.Invoke(() => {
                    MessageBox.Show($"Error connecting to servers: {ex.Message}",
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // Step 6: Add event handlers
        private async void ServerControl_ConnectRequested(object sender, ServerEventArgs e)
        {
            try
            {
                await _serverManager.ConnectToServerAsync(e.ServerId);
                _logger.Information("Connected to server {ServerId}", e.ServerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to server {ServerId}", e.ServerId);
                MessageBox.Show($"Error connecting to server {e.ServerId}: {ex.Message}",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ServerControl_DisconnectRequested(object sender, ServerEventArgs e)
        {
            try
            {
                await _serverManager.DisconnectFromServerAsync(e.ServerId);
                _logger.Information("Disconnected from server {ServerId}", e.ServerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disconnecting from server {ServerId}", e.ServerId);
                MessageBox.Show($"Error disconnecting from server {e.ServerId}: {ex.Message}",
                    "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ServerManager_DataReceived(object sender, ServerDataEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_serverStatusControls.TryGetValue(e.ServerId, out var control))
                    {
                        // Update the control with new data
                        control.UpdateValue(e.Value);

                        // Add to log (limit to last 100 entries)
                        AddToDataLog(e);

                        // Update real-time data manager
                        string channelKey = $"TCP_{e.ServerId}";

                        // Only update if the channel is registered
                        if (realTimeDataManager.IsChannelRegistered(channelKey))
                        {
                            realTimeDataManager.UpdateChannelValue(channelKey, e.Value);
                            _logger.Debug("Updated RealTimeDataManager channel {ChannelKey} with value {Value}", channelKey, e.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing received data for server {ServerId}", e.ServerId);
                }
            });
        }

        private void ServerManager_ConnectionStatusChanged(object sender, ServerConnectionEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_serverStatusControls.TryGetValue(e.ServerId, out var control))
                {
                    // Update connection status
                    control.UpdateConnectionStatus(e.IsConnected);

                    // Add to log
                    string status = e.IsConnected ? "Connected" : "Disconnected";
                    DataLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {e.ServerConfig.Name}: {status}\n");
                    DataLogTextBox.ScrollToEnd();
                }
            });
        }

        private void ServerManager_DataRateUpdated(object sender, ServerDataRateEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_serverStatusControls.TryGetValue(e.ServerId, out var control))
                {
                    // Update data rate display
                    control.UpdateDataRate(e.DataRate);
                }
            });
        }

        private void AddToDataLog(ServerDataEventArgs e)
        {
            // Keep log to a reasonable size (100 lines)
            const int maxLines = 100;

            // Check if we need to trim the log
            while (DataLogTextBox.LineCount >= maxLines)
            {
                int cutoffIndex = DataLogTextBox.Text.IndexOf('\n');
                if (cutoffIndex >= 0)
                {
                    DataLogTextBox.Text = DataLogTextBox.Text.Substring(cutoffIndex + 1);
                }
                else
                {
                    break;
                }
            }

            // Add new log entry
            DataLogTextBox.AppendText(
                $"[{e.Timestamp:HH:mm:ss.fff}] {e.ServerConfig.Name}: " +
                $"{ValueFormatter.FormatWithSIPrefix(e.Value, e.ServerConfig.Unit)}\n");
            DataLogTextBox.ScrollToEnd();
        }

        // Step 7: Add UI setup method for Data Client tab
        private void SetupDataClientTab()
        {
            try
            {
                // Find the Data TCP Clients tab and its Grid
                TabItem dataClientTab = null;
                foreach (var item in MotionControlTabControl.Items)
                {
                    if (item is TabItem tabItem &&
                        tabItem.Header is StackPanel panel &&
                        panel.Children.Count > 0 &&
                        panel.Children[0] is TextBlock textBlock &&
                        textBlock.Text == "Data TCP Clients")
                    {
                        dataClientTab = tabItem;
                        break;
                    }
                }

                if (dataClientTab == null || !(dataClientTab.Content is Grid grid))
                {
                    _logger.Error("Could not find Data TCP Clients tab or its Grid");
                    return;
                }

                // Clear existing content
                grid.Children.Clear();
                grid.RowDefinitions.Clear();

                // Define row structure
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Create header row with buttons
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };

                var connectAllButton = new Button
                {
                    Content = "Connect All",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                connectAllButton.Click += ConnectAllServersButton_Click;

                var disconnectAllButton = new Button
                {
                    Content = "Disconnect All",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                disconnectAllButton.Click += DisconnectAllServersButton_Click;

                var addServerButton = new Button
                {
                    Content = "Add Server",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                addServerButton.Click += AddServerButton_Click;

                var refreshButton = new Button
                {
                    Content = "Refresh",
                    Padding = new Thickness(10, 5, 10, 5)
                };
                refreshButton.Click += RefreshServersButton_Click;

                headerPanel.Children.Add(connectAllButton);
                headerPanel.Children.Add(disconnectAllButton);
                headerPanel.Children.Add(addServerButton);
                headerPanel.Children.Add(refreshButton);

                Grid.SetRow(headerPanel, 0);
                grid.Children.Add(headerPanel);

                // Create server list panel
                ServerListScrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(10, 0, 10, 10)
                };


                Grid.SetRow(ServerListScrollViewer, 1);
                grid.Children.Add(ServerListScrollViewer);

                // Create log header
                var logHeader = new TextBlock
                {
                    Text = "Data Log",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(10, 5, 10, 5)
                };

                Grid.SetRow(logHeader, 2);
                grid.Children.Add(logHeader);

                // Create log text box
                DataLogTextBox = new TextBox
                {
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Margin = new Thickness(10, 0, 10, 10)
                };

                Grid.SetRow(DataLogTextBox, 3);
                grid.Children.Add(DataLogTextBox);

                _logger.Information("Data Client tab UI setup complete");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting up Data Client tab");
            }
        }

        // Step 8: Add button event handlers
        private async void ConnectAllServersButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _serverManager.ConnectToAllAsync(autoConnectOnly: false);
                _logger.Information("Connected to all servers");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to all servers");
                MessageBox.Show($"Error connecting to servers: {ex.Message}",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DisconnectAllServersButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _serverManager.DisconnectFromAllAsync();
                _logger.Information("Disconnected from all servers");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disconnecting from all servers");
                MessageBox.Show($"Error disconnecting from servers: {ex.Message}",
                    "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a dialog to add a new server
            var dialog = new AddServerDialog(_configManager);

            if (dialog.ShowDialog() == true)
            {
                // Refresh the server list
                PopulateServerList();
                _logger.Information("Server added and list refreshed");
            }
        }

        private void RefreshServersButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateServerList();
            _logger.Information("Server list refreshed");
        }

        // Step 9: Add cleanup for window closing
        private void CleanupDataClient()
        {
            try
            {
                if (_serverManager != null)
                {
                    // Disconnect from all servers
                    _ = _serverManager.DisconnectFromAllAsync();

                    // Unsubscribe from events
                    _serverManager.DataReceived -= ServerManager_DataReceived;
                    _serverManager.ConnectionStatusChanged -= ServerManager_ConnectionStatusChanged;
                    _serverManager.DataRateUpdated -= ServerManager_DataRateUpdated;
                }

                _logger.Information("Data Client resources cleaned up");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning up Data Client resources");
            }
        }

        // Step 10: Add Add Server Dialog class
        public class AddServerDialog : Window
        {
            private readonly ConfigManager _configManager;
            private readonly TextBox _idTextBox;
            private readonly TextBox _nameTextBox;
            private readonly TextBox _hostTextBox;
            private readonly TextBox _portTextBox;
            private readonly TextBox _unitTextBox;
            private readonly TextBox _descriptionTextBox;
            private readonly CheckBox _autoConnectCheckBox;
            private readonly CheckBox _logDataCheckBox;

            public AddServerDialog(ConfigManager configManager)
            {
                _configManager = configManager;

                Title = "Add Server";
                Width = 400;
                Height = 400;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

                // Create layout
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

                var mainPanel = new StackPanel { Margin = new Thickness(10) };

                // Create input fields
                _idTextBox = AddField(mainPanel, "Server ID:", "Enter a unique identifier");
                _nameTextBox = AddField(mainPanel, "Name:", "Enter server name");
                _hostTextBox = AddField(mainPanel, "Host:", "Enter hostname or IP");
                _portTextBox = AddField(mainPanel, "Port:", "Enter port number");
                _unitTextBox = AddField(mainPanel, "Unit:", "Enter measurement unit");
                _descriptionTextBox = AddField(mainPanel, "Description:", "Enter description");

                // Create checkboxes
                _autoConnectCheckBox = new CheckBox { Content = "Auto Connect", Margin = new Thickness(0, 5, 0, 5) };
                _logDataCheckBox = new CheckBox { Content = "Log Data", Margin = new Thickness(0, 5, 0, 5) };

                mainPanel.Children.Add(_autoConnectCheckBox);
                mainPanel.Children.Add(_logDataCheckBox);

                Grid.SetRow(mainPanel, 0);
                grid.Children.Add(mainPanel);

                // Create button panel
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };

                var saveButton = new Button
                {
                    Content = "Save",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                saveButton.Click += SaveButton_Click;

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Padding = new Thickness(10, 5, 10, 5),
                    IsCancel = true
                };

                buttonPanel.Children.Add(saveButton);
                buttonPanel.Children.Add(cancelButton);

                Grid.SetRow(buttonPanel, 1);
                grid.Children.Add(buttonPanel);

                Content = grid;
            }

            private TextBox AddField(StackPanel panel, string label, string placeholder)
            {
                var container = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

                container.Children.Add(new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeights.Bold
                });

                var textBox = new TextBox
                {
                    Margin = new Thickness(0, 5, 0, 0),
                    Padding = new Thickness(5),
                };

                container.Children.Add(textBox);
                panel.Children.Add(container);

                return textBox;
            }

            private async void SaveButton_Click(object sender, RoutedEventArgs e)
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(_idTextBox.Text) ||
                    string.IsNullOrWhiteSpace(_nameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(_hostTextBox.Text) ||
                    string.IsNullOrWhiteSpace(_portTextBox.Text) ||
                    string.IsNullOrWhiteSpace(_unitTextBox.Text))
                {
                    MessageBox.Show("Please fill in all required fields.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!int.TryParse(_portTextBox.Text, out int port))
                {
                    MessageBox.Show("Port must be a valid number.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    // Create server config
                    var server = new ServerConfig
                    {
                        Id = _idTextBox.Text,
                        Name = _nameTextBox.Text,
                        Host = _hostTextBox.Text,
                        Port = port,
                        Unit = _unitTextBox.Text,
                        Description = _descriptionTextBox.Text,
                        AutoConnect = _autoConnectCheckBox.IsChecked ?? false,
                        LogData = _logDataCheckBox.IsChecked ?? false
                    };

                    // Add to configuration
                    _configManager.AddServer(server);

                    // Save configuration
                    await _configManager.SaveConfigAsync();

                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving server: {ex.Message}",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Step 11: Register channels with RealTimeDataManager
        // Register TCP server channels with RealTimeDataManager
        private void RegisterDataClientChannels()
        {
            try
            {
                _logger.Information("Registering Data Client channels with RealTimeDataManager");

                // For each server in the configuration, register a channel
                foreach (var server in _serverManager.GetAllServerConfigs())
                {
                    // Create channel key
                    string channelKey = $"TCP_{server.Id}";

                    // Check if channel already exists
                    if (!realTimeDataManager.IsChannelRegistered(channelKey))
                    {
                        // Add channel
                        bool added = realTimeDataManager.AddChannel(channelKey, server.Name, 0, server.Unit);

                        if (added)
                        {
                            _logger.Information("Registered channel {ChannelKey} for server {ServerName}", channelKey, server.Name);
                        }
                        else
                        {
                            _logger.Warning("Failed to register channel {ChannelKey} for server {ServerName}", channelKey, server.Name);
                        }
                    }
                    else
                    {
                        _logger.Debug("Channel {ChannelKey} for server {ServerName} already registered", channelKey, server.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error registering Data Client channels");
            }
        }
    }
}
