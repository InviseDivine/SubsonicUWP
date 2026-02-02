using System;
using Windows.UI.Xaml.Data;

namespace SubsonicUWP.Converters
{
    public class LocalCoverArtConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string id)
            {
                // Return local cached path
                // Note: This assumes the file exists. If it doesn't, Image control will just be empty (safe).
                return new Uri($"ms-appdata:///temp/Cache/stream_{id}.jpg");
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
