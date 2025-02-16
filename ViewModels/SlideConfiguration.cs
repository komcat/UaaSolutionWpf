using Newtonsoft.Json;

namespace UaaSolutionWpf.ViewModels
{
    public class SlideConfiguration
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("device")]
        public string Device { get; set; }

        [JsonProperty("controls")]
        public SlideControls Controls { get; set; }

        [JsonProperty("safetyChecks")]
        public SafetyChecks SafetyChecks { get; set; }
    }

    public class SlideControls
    {
        [JsonProperty("output")]
        public OutputControl Output { get; set; }

        [JsonProperty("sensors")]
        public SensorControl Sensors { get; set; }
    }

    public class OutputControl
    {
        [JsonProperty("device")]
        public string Device { get; set; }

        [JsonProperty("pinName")]
        public string PinName { get; set; }

        [JsonProperty("setToMoveUp")]
        public bool SetToMoveUp { get; set; }

        [JsonProperty("clearToMoveDown")]
        public bool ClearToMoveDown { get; set; }
    }

    public class SensorControl
    {
        [JsonProperty("device")]
        public string Device { get; set; }

        [JsonProperty("upSensor")]
        public string UpSensor { get; set; }

        [JsonProperty("downSensor")]
        public string DownSensor { get; set; }
    }

    public class SafetyChecks
    {
        [JsonProperty("requireSensorConfirmation")]
        public bool RequireSensorConfirmation { get; set; }

        [JsonProperty("timeoutMs")]
        public int TimeoutMs { get; set; }
    }
}