using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DocControlUI.Converters
{
    /// <summary>
    /// Конвертер для перетворення bool у колір (зелений - online, сірий - offline)
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOnline)
            {
                return isOnline
                    ? new SolidColorBrush(Colors.LimeGreen)
                    : new SolidColorBrush(Colors.Gray);
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
