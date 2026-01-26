using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace SubsonicUWP
{
    public class StringNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false; // Default: Null/Empty = Visible

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string s = value as string;
            bool isNullOrEmpty = string.IsNullOrEmpty(s);
            
            if (Invert)
            {
                 // Null/Empty -> Collapsed
                 return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                 // Null/Empty -> Visible
                 return isNullOrEmpty ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class SliderTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double seconds)
            {
                TimeSpan ts = TimeSpan.FromSeconds(seconds);
                if (ts.TotalHours >= 1)
                     return ts.ToString(@"h\:mm\:ss");
                else
                     return ts.ToString(@"m\:ss");
            }
            return "0:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class VolumeTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double vol)
            {
                return (vol * 100).ToString("0");
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
            {
                return (Windows.UI.Xaml.Media.Brush)Windows.UI.Xaml.Application.Current.Resources["SystemControlHighlightAccentBrush"];
            }
            return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
            {
                return new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
            }
            return (Windows.UI.Xaml.Media.Brush)Windows.UI.Xaml.Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isVisible = value is bool b && b;
            if (Invert) isVisible = !isVisible;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return (Visibility)value == Visibility.Visible;
        }
    }
}
