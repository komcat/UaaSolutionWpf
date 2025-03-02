using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MotionServiceLib;
using Newtonsoft.Json;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Scanning.Core
{
    public class ScanDataCollector : IDisposable
    {
        private readonly string _deviceId;
        private readonly List<ScanMeasurement> _measurements;
        private ScanBaseline _baseline;
        private ScanPeak _currentPeak;
        private readonly string _scanId;

        public ScanDataCollector(string deviceId)
        {
            _deviceId = deviceId;
            _measurements = new List<ScanMeasurement>();
            _scanId = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        public void RecordBaseline(double value, DevicePosition position)
        {
            _baseline = new ScanBaseline
            {
                Value = value,
                Position = ConvertToPosition(position),
                Timestamp = DateTime.Now
            };

            // Initialize peak with baseline
            _currentPeak = new ScanPeak
            {
                Value = value,
                Position = _baseline.Position,
                Timestamp = DateTime.Now,
                Context = "Initial Position"
            };
        }

        public void RecordMeasurement(double value, DevicePosition position, string axis, double stepSize, int direction)
        {
            var measurement = new ScanMeasurement
            {
                Value = value,
                Position = ConvertToPosition(position),
                Timestamp = DateTime.Now,
                Axis = axis,
                StepSize = stepSize,
                Direction = direction > 0 ? "Positive" : "Negative"
            };

            _measurements.Add(measurement);

            // Update peak if necessary
            if (_currentPeak == null || value > _currentPeak.Value)
            {
                _currentPeak = new ScanPeak
                {
                    Value = value,
                    Position = measurement.Position,
                    Timestamp = DateTime.Now,
                    Context = $"{axis} axis scan with {stepSize * 1000:F3} micron steps"
                };
            }
        }

        public Position GetBaselinePosition() => _baseline?.Position;
        public double GetBaselineValue() => _baseline?.Value ?? 0;

        public Position GetPeakPosition() => _currentPeak?.Position;
        public double GetPeakValue() => _currentPeak?.Value ?? double.MinValue;

        public ScanResults GetResults()
        {
            return new ScanResults
            {
                DeviceId = _deviceId,
                ScanId = _scanId,
                StartTime = _measurements.FirstOrDefault()?.Timestamp ?? DateTime.Now,
                EndTime = _measurements.LastOrDefault()?.Timestamp ?? DateTime.Now,
                Baseline = _baseline,
                Peak = _currentPeak,
                TotalMeasurements = _measurements.Count,
                Measurements = _measurements.ToList(),
                Statistics = CalculateStatistics()
            };
        }

        public void SaveResults()
        {
            var results = GetResults();

            string logsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "logs",
                "scanning"
            );
            Directory.CreateDirectory(logsPath);

            string fileName = $"ScanResults_{_deviceId}_{_scanId}.json";
            string fullPath = Path.Combine(logsPath, fileName);

            File.WriteAllText(
                fullPath,
                JsonConvert.SerializeObject(results, Formatting.Indented)
            );
        }

        private ScanStatistics CalculateStatistics()
        {
            if (!_measurements.Any()) return null;

            var values = _measurements.Select(m => m.Value).ToList();
            return new ScanStatistics
            {
                MinValue = values.Min(),
                MaxValue = values.Max(),
                AverageValue = values.Average(),
                StandardDeviation = CalculateStandardDeviation(values),
                TotalDuration = _measurements.Last().Timestamp - _measurements.First().Timestamp,
                TotalMeasurements = _measurements.Count,
                MeasurementsPerAxis = _measurements
                    .GroupBy(m => m.Axis)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            double average = values.Average();
            double sumOfSquaresOfDifferences = values.Sum(val => (val - average) * (val - average));
            return Math.Sqrt(sumOfSquaresOfDifferences / (values.Count - 1));
        }

        private Position ConvertToPosition(DevicePosition devicePosition)
        {
            return new Position
            {
                X = devicePosition.X,
                Y = devicePosition.Y,
                Z = devicePosition.Z,
                U = devicePosition.U,
                V = devicePosition.V,
                W = devicePosition.W
            };
        }

        public void Dispose()
        {
            try
            {
                SaveResults();
            }
            catch
            {
                // Log error but don't throw during disposal
            }
        }
    }

    public class ScanBaseline
    {
        public double Value { get; set; }
        public Position Position { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ScanPeak
    {
        public double Value { get; set; }
        public Position Position { get; set; }
        public DateTime Timestamp { get; set; }
        public string Context { get; set; }
    }

    public class ScanStatistics
    {
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public double AverageValue { get; set; }
        public double StandardDeviation { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public int TotalMeasurements { get; set; }
        public Dictionary<string, int> MeasurementsPerAxis { get; set; }
    }

    public class ScanResults
    {
        public string DeviceId { get; set; }
        public string ScanId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public ScanBaseline Baseline { get; set; }
        public ScanPeak Peak { get; set; }
        public int TotalMeasurements { get; set; }
        public List<ScanMeasurement> Measurements { get; set; }
        public ScanStatistics Statistics { get; set; }
    }

    public class ScanMeasurement
    {
        // Value and position data
        public double Value { get; set; }
        public Position Position { get; set; }
        public DateTime Timestamp { get; set; }

        // Scan context information
        public string Axis { get; set; }
        public double StepSize { get; set; }
        public string Direction { get; set; }

        // Calculated metrics
        public double Gradient { get; set; }
        public double RelativeImprovement { get; set; }  // Improvement from previous measurement

        // Status flags
        public bool IsPeak { get; set; }
        public bool IsValid { get; set; } = true;

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] {Axis} {Direction}: {Value:E3} at {Position}";
        }

        public string ToDetailedString()
        {
            return $"Measurement: {Value:E3}\n" +
                   $"Position: {Position}\n" +
                   $"Time: {Timestamp:HH:mm:ss.fff}\n" +
                   $"Axis: {Axis} ({Direction})\n" +
                   $"Step Size: {StepSize * 1000:F3} microns\n" +
                   $"Gradient: {Gradient:E3}\n" +
                   $"Improvement: {RelativeImprovement:P2}";
        }
    }
}