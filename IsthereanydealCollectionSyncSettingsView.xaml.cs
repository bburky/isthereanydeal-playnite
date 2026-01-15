using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace IsthereanydealCollectionSync
{
    public partial class IsthereanydealCollectionSyncSettingsView : UserControl
    {
        public IsthereanydealCollectionSyncSettingsView()
        {
            InitializeComponent();
        }
    }

    [ValueConversion(typeof(string), typeof(string[]))]
    public class StringToStringArray : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is null)
            {
                return "";
            }

            return string.Join(",", (string[])value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is null)
            {
                return new string[0];
            }

            string text = (string)value;

            return text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToArray();
        }
    }
}