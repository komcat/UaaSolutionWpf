using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UaaSolutionWpf.ViewModels;

namespace UaaSolutionWpf.Config
{
    public class PneumaticSlideConfigManager
    {
        private readonly string _configFilePath;
        private PneumaticSlideConfig _config;

        public PneumaticSlideConfigManager(string configFilePath)
        {
            _configFilePath = configFilePath ??
                throw new ArgumentNullException(nameof(configFilePath));
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    throw new FileNotFoundException("Configuration file not found.", _configFilePath);
                }

                string jsonContent = File.ReadAllText(_configFilePath);
                _config = JsonConvert.DeserializeObject<PneumaticSlideConfig>(jsonContent);

                // Validate configuration
                ValidateConfiguration();
            }
            catch (Exception ex)
            {
                // Log the error or handle it appropriately
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                throw;
            }
        }

        private void ValidateConfiguration()
        {
            if (_config == null)
            {
                throw new InvalidOperationException("Configuration could not be loaded.");
            }

            if (_config.Slides == null || _config.Slides.Count == 0)
            {
                throw new InvalidOperationException("No slides configured.");
            }

            // Additional validation as needed
            foreach (var slide in _config.Slides)
            {
                ValidateSlideConfiguration(slide);
            }
        }

        private void ValidateSlideConfiguration(SlideConfiguration slide)
        {
            if (string.IsNullOrWhiteSpace(slide.Id))
            {
                throw new InvalidOperationException($"Slide configuration missing ID");
            }

            if (slide.Controls?.Output == null)
            {
                throw new InvalidOperationException($"Slide {slide.Id} missing output controls");
            }

            if (slide.Controls?.Sensors == null)
            {
                throw new InvalidOperationException($"Slide {slide.Id} missing sensor controls");
            }
        }

        public IReadOnlyList<SlideConfiguration> GetSlideConfigurations()
        {
            return _config.Slides.AsReadOnly();
        }

        public GlobalSettings GetGlobalSettings()
        {
            return _config.GlobalSettings;
        }
    }

    public class PneumaticSlideConfig
    {
        [JsonProperty("metadata")]
        public ConfigMetadata Metadata { get; set; }

        [JsonProperty("slides")]
        public List<SlideConfiguration> Slides { get; set; }

        [JsonProperty("globalSettings")]
        public GlobalSettings GlobalSettings { get; set; }
    }

    public class ConfigMetadata
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }

    public class GlobalSettings
    {
        [JsonProperty("defaultTimeoutMs")]
        public int DefaultTimeoutMs { get; set; }

        [JsonProperty("retryAttempts")]
        public int RetryAttempts { get; set; }

        [JsonProperty("retryDelayMs")]
        public int RetryDelayMs { get; set; }
    }
}