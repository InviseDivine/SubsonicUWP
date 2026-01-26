using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace SubsonicUWP
{
    public static class DebugLogger
    {
        public static async void Log(string message)
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync("debug_log.txt", CreationCollisionOption.OpenIfExists);
                await FileIO.AppendTextAsync(file, $"{DateTime.Now}: {message}\r\n");
            }
            catch { }
        }
    }
}
