using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.ViewModels;
using UaaSolutionWpf.Config;
using Serilog;
using Newtonsoft.Json;
using System.IO;
using UaaSolutionWpf.Config;

namespace UaaSolutionWpf.Controls
{
    public partial class PneumaticSlideControlItem : UserControl
    {
        private IOManager _ioManager;
        private ILogger _logger;
        private PneumaticSlideConfigManager _configManager;
        private Dictionary<string, PneumaticSlideItem> _slideItems = new Dictionary<string, PneumaticSlideItem>();

        public PneumaticSlideControlItem()
        {
            InitializeComponent();
        }

        public void Initialize(IOManager ioManager, ILogger logger)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Load configuration
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "PneumaticSlides.json");
            _configManager = new PneumaticSlideConfigManager(configPath);

            // Create slide items based on configuration
            CreateSlideItems();
        }

        private void CreateSlideItems()
        {
            // Clear existing items
            SlideItemsPanel.Children.Clear();
            _slideItems.Clear();

            // Get slide configurations
            var slideConfigs = _configManager.GetSlideConfigurations();

            foreach (var config in slideConfigs)
            {
                try
                {
                    // Create slide item control
                    var slideItem = new PneumaticSlideItem();
                    slideItem.Initialize(config, _ioManager);

                    // Add to panel and dictionary
                    SlideItemsPanel.Children.Add(slideItem);
                    _slideItems[config.Id] = slideItem;

                    _logger.Information($"Created Pneumatic Slide Item: {config.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to create Pneumatic Slide Item for {config.Name}");
                }
            }
        }

        public void UpdateSensorState(string sensorName, bool state)
        {
            // Determine which slide and whether it's up or down sensor
            foreach (var slideItem in _slideItems.Values)
            {
                if (slideItem.Configuration.Controls.Sensors.UpSensor == sensorName)
                {
                    slideItem.UpdateSensorStates(state, false);
                }
                else if (slideItem.Configuration.Controls.Sensors.DownSensor == sensorName)
                {
                    slideItem.UpdateSensorStates(false, state);
                }
            }
        }
    }
}