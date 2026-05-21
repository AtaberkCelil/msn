using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Client
{
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string ?? "Offline";
            return status switch
            {
                "Online" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                "Busy" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                "Away" => new SolidColorBrush(Color.FromRgb(249, 115, 22)),
                _ => new SolidColorBrush(Color.FromRgb(156, 163, 175))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
