using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Text.Json;

namespace UaaSolutionWpf.Controls
{
    /// <summary>
    /// Manages the calibration settings for the camera-to-gantry coordinate mapping
    /// </summary>
    public class CameraCalibrationManager
    {
        private const string SettingsFileName = "CameraCalibration.json";
        private readonly string _settingsFilePath;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private CalibrationSettings _cachedSettings;

        public CameraCalibrationManager(ILogger logger = null)
        {
            _logger = logger?.ForContext<CameraCalibrationManager>() ?? Log.ForContext<CameraCalibrationManager>();

            // Determine settings file path
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

            // Ensure the directory exists
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
                _logger.Information("Created Config directory at {Path}", configPath);
            }

            _settingsFilePath = Path.Combine(configPath, SettingsFileName);
            _logger.Debug("Camera calibration settings file path: {FilePath}", _settingsFilePath);
        }

        /// <summary>
        /// Loads the calibration settings from file
        /// </summary>
        /// <returns>The calibration settings</returns>
        public async Task<CalibrationSettings> LoadSettingsAsync()
        {
            try
            {
                // Return cached settings if available
                if (_cachedSettings != null)
                {
                    return _cachedSettings;
                }

                await _fileLock.WaitAsync();
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        string json = await File.ReadAllTextAsync(_settingsFilePath);
                        var settings = JsonSerializer.Deserialize<CalibrationSettings>(json);

                        if (settings != null &&
                            settings.PixelToMmFactorX > 0 &&
                            settings.PixelToMmFactorY > 0)
                        {
                            _cachedSettings = settings;
                            _logger.Debug("Loaded camera calibration settings: X={XFactor}, Y={YFactor}",
                                settings.PixelToMmFactorX, settings.PixelToMmFactorY);
                            return settings;
                        }
                    }

                    // Create default settings if file doesn't exist or is invalid
                    _cachedSettings = CreateDefaultSettings();
                    await SaveSettingsAsync(_cachedSettings);

                    _logger.Information("Created default camera calibration settings: X={XFactor}, Y={YFactor}",
                        _cachedSettings.PixelToMmFactorX, _cachedSettings.PixelToMmFactorY);

                    return _cachedSettings;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading camera calibration settings");
                // Return default settings in case of error
                return CreateDefaultSettings();
            }
        }

        /// <summary>
        /// Synchronous version of LoadSettingsAsync
        /// </summary>
        public CalibrationSettings LoadSettings()
        {
            try
            {
                // Return cached settings if available
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
                        var settings = JsonSerializer.Deserialize<CalibrationSettings>(json);

                        if (settings != null &&
                            settings.PixelToMmFactorX > 0 &&
                            settings.PixelToMmFactorY > 0)
                        {
                            _cachedSettings = settings;
                            _logger.Debug("Loaded camera calibration settings: X={XFactor}, Y={YFactor}",
                                settings.PixelToMmFactorX, settings.PixelToMmFactorY);
                            return settings;
                        }
                    }

                    // Create default settings if file doesn't exist or is invalid
                    _cachedSettings = CreateDefaultSettings();
                    SaveSettings(_cachedSettings);

                    _logger.Information("Created default camera calibration settings: X={XFactor}, Y={YFactor}",
                        _cachedSettings.PixelToMmFactorX, _cachedSettings.PixelToMmFactorY);

                    return _cachedSettings;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading camera calibration settings");
                // Return default settings in case of error
                return CreateDefaultSettings();
            }
        }

        /// <summary>
        /// Saves calibration settings to file
        /// </summary>
        /// <param name="settings">Settings to save</param>
        public async Task SaveSettingsAsync(CalibrationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.PixelToMmFactorX <= 0 || settings.PixelToMmFactorY <= 0)
            {
                throw new ArgumentException("Calibration factors must be positive values");
            }

            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(settings, options);
                    await File.WriteAllTextAsync(_settingsFilePath, json);

                    _cachedSettings = settings;

                    _logger.Debug("Saved camera calibration settings: X={XFactor}, Y={YFactor}",
                        settings.PixelToMmFactorX, settings.PixelToMmFactorY);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving camera calibration settings");
                throw;
            }
        }

        /// <summary>
        /// Synchronous version of SaveSettingsAsync
        /// </summary>
        public void SaveSettings(CalibrationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.PixelToMmFactorX <= 0 || settings.PixelToMmFactorY <= 0)
            {
                throw new ArgumentException("Calibration factors must be positive values");
            }

            try
            {
                _fileLock.Wait();
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(settings, options);
                    File.WriteAllText(_settingsFilePath, json);

                    _cachedSettings = settings;

                    _logger.Debug("Saved camera calibration settings: X={XFactor}, Y={YFactor}",
                        settings.PixelToMmFactorX, settings.PixelToMmFactorY);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving camera calibration settings");
                throw;
            }
        }

        /// <summary>
        /// Invalidates the cached settings, forcing a reload from file
        /// </summary>
        public void InvalidateCache()
        {
            _cachedSettings = null;
            _logger.Debug("Camera calibration settings cache invalidated");
        }

        /// <summary>
        /// Creates default settings
        /// </summary>
        private CalibrationSettings CreateDefaultSettings()
        {
            return new CalibrationSettings
            {
                PixelToMmFactorX = 0.00427, // Default factor (mm per pixel)
                PixelToMmFactorY = 0.00427, // Default factor (mm per pixel)
                LastUpdated = DateTime.Now
            };
        }
    }

    /// <summary>
    /// Represents camera calibration settings
    /// </summary>
    public class CalibrationSettings
    {
        /// <summary>
        /// Conversion factor from pixels to mm for X-axis
        /// </summary>
        public double PixelToMmFactorX { get; set; }

        /// <summary>
        /// Conversion factor from pixels to mm for Y-axis
        /// </summary>
        public double PixelToMmFactorY { get; set; }

        /// <summary>
        /// Date/time when settings were last updated
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}