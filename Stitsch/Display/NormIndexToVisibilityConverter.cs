using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Simscop.Pl.Wpf.Converters
{
    public class NormIndexToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 仅当 NormIndex == 4 时显示
            if (value is int normIndex && normIndex == 4)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
