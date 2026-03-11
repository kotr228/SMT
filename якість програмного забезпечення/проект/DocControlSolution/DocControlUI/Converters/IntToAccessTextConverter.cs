using System;
using System.Globalization;
using System.Windows.Data;

namespace DocControlUI.Converters
{
    /// <summary>
    /// Конвертер для перетворення кількості директорій у текст
    /// </summary>
    public class IntToAccessTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                if (count == 0)
                    return "🔒 Немає доступу";
                else if (count == 1)
                    return "✅ Доступ до 1 директорії";
                else
                    return $"✅ Доступ до {count} директорій";
            }
            return "Невідомо";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
