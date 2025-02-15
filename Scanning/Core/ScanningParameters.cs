using System;

namespace UaaSolutionWpf.Scanning.Core
{
    public class ScanningParameters
    {
        // Motion control parameters
        public int MotionSettleTimeMs { get; set; } = 400;
        public int ConsecutiveDecreasesLimit { get; set; } = 1;
        public double ImprovementThreshold { get; set; } = 0.01; // 2%

        // Scan range parameters
        public string[] AxesToScan { get; set; } = {  "Z", "X", "Y" };
        public double[] StepSizes { get; set; }

        // Safety parameters
        public double MaxStepSize { get; set; } = 0.5; // mm
        public double MaxTotalDistance { get; set; } = 5.0; // mm
        public double MinValue { get; set; } = double.MinValue;
        public double MaxValue { get; set; } = double.MaxValue;

        // Timing parameters
        public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(30);
        public TimeSpan MeasurementTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public static ScanningParameters CreateDefault()
        {
            return new ScanningParameters
            {
                StepSizes = new[] { 0.001, 0.005, 0.010 } // 1, 5, 10 microns
            };
        }

        public void Validate()
        {
            if (StepSizes == null || StepSizes.Length == 0)
                throw new ArgumentException("At least one step size must be specified");

            if (AxesToScan == null || AxesToScan.Length == 0)
                throw new ArgumentException("At least one axis must be specified");

            foreach (var stepSize in StepSizes)
            {
                if (stepSize <= 0 || stepSize > MaxStepSize)
                    throw new ArgumentException($"Step size {stepSize} is invalid. Must be between 0 and {MaxStepSize}");
            }

            if (MotionSettleTimeMs < 0)
                throw new ArgumentException("Motion settle time cannot be negative");

            if (ConsecutiveDecreasesLimit < 1)
                throw new ArgumentException("Consecutive decreases limit must be at least 1");

            if (ImprovementThreshold < 0 || ImprovementThreshold > 1)
                throw new ArgumentException("Improvement threshold must be between 0 and 1");
        }
    }
}