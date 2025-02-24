using Newtonsoft.Json;
using Serilog;
using System;
using System.IO;
using System.Threading;

namespace UaaSolutionWpf.Services
{
    /// <summary>
    /// Settings class for camera conversion factors
    /// </summary>
    public class CameraConversionSettings
    {
        [JsonProperty("pixelToMillimeterFactorX")]
        public double PixelToMillimeterFactorX { get; set; } = 0.00427;

        [JsonProperty("pixelToMillimeterFactorY")]
        public double PixelToMillimeterFactorY { get; set; } = 0.00427;
    }

    /// <summary>
    /// Manages loading and saving camera conversion settings to JSON
    /// </summary>
    public class CameraSettingsManager
    {
        private readonly string _settingsFilePath;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private CameraConversionSettings _cachedSettings;

        public CameraSettingsManager(ILogger logger)
        {
            _logger = logger?.ForContext<CameraSettingsManager>() ?? throw new ArgumentNullException(nameof(logger));

            // Determine the settings file path
            string appDataPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config");

            // Ensure directory exists
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _settingsFilePath = Path.Combine(appDataPath, "CameraSettings.json");
            Log.Debug("Camera settings file path: {FilePath}", _settingsFilePath);
        }

        /// <summary>
        /// Loads camera conversion settings from file
        /// </summary>
        /// <returns>Camera conversion settings</returns>
        public CameraConversionSettings LoadSettings()
        {
            try
            {
                // If we have cached settings, return them
                if (_cachedSettings != null)
                {
                    return _cachedSettings;
                }

                _fileLock.Wait();
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        string json = File.ReadAllText(_settingsFilePath);
                        var settings = JsonConvert.DeserializeObject<CameraConversionSettings>(json);

                        // Validate settings
                        if (settings != null && settings.PixelToMillimeterFactorX > 0 && settings.PixelToMillimeterFactorY > 0)
                        {
                            _cachedSettings = settings;
                            Log.Debug("Loaded camera conversion settings: X={XFactor}, Y={YFactor}",
                                settings.PixelToMillimeterFactorX,
                                settings.PixelToMillimeterFactorY);
                            return settings;
                        }
                    }

                    // Create default settings if file doesn't exist or is invalid
                    _cachedSettings = new CameraConversionSettings();
                    SaveSettings(_cachedSettings);
                    Log.Information("Created default camera conversion settings: X={XFactor}, Y={YFactor}",
                        _cachedSettings.PixelToMillimeterFactorX,
                        _cachedSettings.PixelToMillimeterFactorY);
                    return _cachedSettings;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading camera settings");
                // Return default settings in case of error
                return new CameraConversionSettings();
            }
        }

        /// <summary>
        /// Saves camera conversion settings to file
        /// </summary>
        /// <param name="settings">Settings to save</param>
        public void SaveSettings(CameraConversionSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            try
            {
                _fileLock.Wait();
                try
                {
                    string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                    File.WriteAllText(_settingsFilePath, json);
                    _cachedSettings = settings;
                    Log.Debug("Saved camera conversion settings: X={XFactor}, Y={YFactor}",
                        settings.PixelToMillimeterFactorX,
                        settings.PixelToMillimeterFactorY);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving camera settings");
                throw;
            }
        }
    }
}