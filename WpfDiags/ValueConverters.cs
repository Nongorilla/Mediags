using System;
using System.Windows.Data;
using NongFormat;

namespace AppView
{
    public class ComparisonConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        { return value.Equals (param); }

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        { return value.Equals (true) ? param : Binding.DoNothing; }
    }

    public class HashToggle : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => ((int) value & (int) param) != 0;

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => value.Equals (true) ? (Hashes) param : (Hashes) ~ (int) param;
    }

    public class ValidationToggle : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => ((int) value & (int) param) != 0;

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => value.Equals (true) ? (Validations) param : (Validations) ~(int) param;
    }
}
