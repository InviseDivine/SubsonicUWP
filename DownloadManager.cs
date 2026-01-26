using System;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;

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
                var operation = download.StartAsync();
                
                // Notify User
                var dialog = new Windows.UI.Popups.MessageDialog($"Started downloading '{title}' to Music Library", "Download Started");
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new Windows.UI.Popups.MessageDialog($"Error starting download: {ex.Message}", "Download Failed");
                await dialog.ShowAsync();
            }
        }

        private static string Sanitize(string input)
        {
            var invalids = System.IO.Path.GetInvalidFileNameChars();
            return string.Join("_", input.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
