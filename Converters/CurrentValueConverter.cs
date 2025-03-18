using System;
using System.Globalization;
using System.Windows.Data;

namespace UaaSolutionWpf.Converters
{
    public class CurrentValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double current)
            {
                // Handle different ranges with appropriate prefixes
                if (Math.Abs(current) < 1e-9)
                {
                    // If value is effectively zero
                    return "0.000 A";
                }
                else if (Math.Abs(current) < 1e-6)
                {
                    // Nanoamps (nA)
                    return $"{(current * 1e9):F3} nA";
                }
                else if (Math.Abs(current) < 1e-3)
                {
                    // Microamps (µA)
                    return $"{(current * 1e6):F3} µA";
                }
                else if (Math.Abs(current) < 1)
                {
                    // Milliamps (mA)
                    return $"{(current * 1e3):F3} mA";
                }
                else
                {
                    // Amps (A)
                    return $"{current:F3} A";
                }
            }

            return "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}