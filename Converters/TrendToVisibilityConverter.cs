using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;
using UaaSolutionWpf.Controls;

namespace UaaSolutionWpf.Converters
{
    public class TrendToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SingleSensorDisplayControl.TrendDirection trend)
            {
                return trend != SingleSensorDisplayControl.TrendDirection.Stable ?
                    Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
