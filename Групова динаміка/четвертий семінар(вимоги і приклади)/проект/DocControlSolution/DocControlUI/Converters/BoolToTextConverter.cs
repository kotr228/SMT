using System;
using System.Globalization;
using System.Windows.Data;

namespace DocControlUI.Converters
{
    /// <summary>
    /// Конвертер для перетворення bool (Access) у текст (Доступ дозволено/заборонено)
    /// </summary>
    public class BoolToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasAccess)
            {
                return hasAccess ? "Доступ дозволено" : "Доступ заборонено";
            }
            return "Невідомо";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
