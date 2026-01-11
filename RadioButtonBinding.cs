using System;
using System.Windows.Data;

namespace IsthereanydealCollectionSync
{
    [ValueConversion(typeof(bool), typeof(bool))]
    // https://stackoverflow.com/a/3361553
    public class BoolInverterConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
             return !(bool)value;
        }

        #endregion
    }
}
