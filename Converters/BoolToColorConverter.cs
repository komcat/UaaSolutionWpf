using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UaaSolutionWpf.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        // Default colors if not set through XAML
        public Brush TrueValue { get; set; } = new SolidColorBrush(Colors.Green);
        public Brush FalseValue { get; set; } = new SolidColorBrush(Colors.Red);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }

            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}