using System;
using System.IO;
using System.Text.Json;


namespace UaaSolutionWpf.Config
{
    public class AppSettings
    {
        public ConnectionSettings ConnectionSettings { get; set; } = new();
        public SensorSettings SensorSettings { get; set; } = new();
        public CameraSettings CameraSettings { get; set; } = new();
        public TimingSettings TimingSettings { get; set; } = new();
        public GeneralSettings GeneralSettings { get; set; } = new();
    }

    public class ConnectionSettings
    {
        public ACSSettings ACS { get; set; } = new();
        public HexapodSettings Hexapods { get; set; } = new();
        public SiphoqServerSettings SiphoqServer { get; set; } = new();
        public EziiOSettings EziiO { get; set; } = new();
    }

    public class ACSSettings
    {
        public string IpAddress { get; set; } = "192.168.0.50";
    }

    public class HexapodSettings
    {
        public HexapodInstance Left { get; set; } = new();
        public HexapodInstance Right { get; set; } = new();
        public HexapodInstance Bottom { get; set; } = new();
    }

    public class HexapodInstance
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
    }

    public class SiphoqServerSettings
    {
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 65432;
    }

    public class EziiOSettings
    {
        public EziiOInstance Input { get; set; } = new();
        public EziiOInstance Output { get; set; } = new();
    }

    public class EziiOInstance
    {
        public string IpAddress { get; set; }
    }

    public class SensorSettings
    {
        public string SelectedSensorName { get; set; } = "";
        public int SelectedSensorIndex { get; set; } = 0;
    }

    public class CameraSettings
    {
        public NeedleOffset NeedleOffset { get; set; } = new();
    }

    public class NeedleOffset
    {
        public double X { get; set; } = 0.0;
        public double Y { get; set; } = 0.0;
    }

    public class TimingSettings
    {
        public int ShotTime { get; set; } = 0;
        public int ManualDispenserShotTime { get; set; } = 0;
    }

    public class GeneralSettings
    {
        public string PresetName { get; set; } = "";
        public int Setting { get; set; } = 0;
    }

    public class ConfigurationManager
    {
        private readonly string _configPath;
        private AppSettings _settings;

        public ConfigurationManager(string configPath = "appsettings.json")
        {
            _configPath = configPath;
            LoadConfiguration();
        }

        public AppSettings Settings => _settings;

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string jsonString = File.ReadAllText(_configPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(jsonString) ?? new AppSettings();
                }
                else
                {
                    _settings = new AppSettings();
                    SaveConfiguration(); // Create default configuration file
                }
            }
            catch (Exception ex)
            {
                // Log the error appropriately
                _settings = new AppSettings();
            }
        }

        public void SaveConfiguration()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string jsonString = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_configPath, jsonString);
            }
            catch (Exception ex)
            {
                // Log the error appropriately
            }
        }

        // Example usage of updating a setting
        public void UpdateHexapodLeftSettings(string ipAddress, int port)
        {
            _settings.ConnectionSettings.Hexapods.Left.IpAddress = ipAddress;
            _settings.ConnectionSettings.Hexapods.Left.Port = port;
            SaveConfiguration();
        }
    }
}