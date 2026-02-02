using System;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace SubsonicUWP
{
    public static class DownloadManager
    {
        public static async Task StartDownload(SubsonicItem song)
        {
            if (song == null) return;

            try
            {
                // Use MusicLibrary so files are visible to user
                StorageFolder musicFolder = KnownFolders.MusicLibrary;
                
                // Sanitize filename
                var artist = string.IsNullOrWhiteSpace(song.Artist) ? "Unknown Artist" : Sanitize(song.Artist);
                var title = string.IsNullOrWhiteSpace(song.Title) ? "Unknown Title" : Sanitize(song.Title);
                
                // Save directly to Music folder
                var filename = $"{artist} - {title}.mp3"; 
                var destinationFile = await musicFolder.CreateFileAsync(filename, CreationCollisionOption.GenerateUniqueName);

                // Build Download URL
                var downloadUrl = SubsonicService.Instance.BuildUrl("download.view", $"&id={song.Id}");
                var uri = new Uri(downloadUrl);

                var downloader = new BackgroundDownloader();
                var download = downloader.CreateDownload(uri, destinationFile);
                
                // Start
                var operation = await download.StartAsync();
                
                // Show localized toast? Or just standard toast.
                ShowToast($"Downloaded '{title}'", $"Saved to Music Library");
            }
            catch (Exception ex)
            {
                // Only show error dialog if it fails?
                // Or maybe a Toast for error too?
                // Keep it simple for now.
            }
        }

        private static void ShowToast(string title, string content)
        {
            var template = ToastTemplateType.ToastText02;
            var xml = ToastNotificationManager.GetTemplateContent(template);
            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(content));
            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private static string Sanitize(string input)
        {
            var invalids = System.IO.Path.GetInvalidFileNameChars();
            return string.Join("_", input.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
