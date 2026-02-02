using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.Storage;
using System.Net.Http;

namespace SubsonicUWP
{
    public static class TileManager
    {
        private static string _lastTileImageFile = null;

        public static void UpdateTile(SubsonicItem song)
        {
            // Fire and Forget Background Task
            Task.Run(async () =>
            {
                string localImagePath = "";

                try
                {
                    // Download image to local folder so Tile Service can access it
                    if (song != null)
                    {
                        // 1. Try Offline Cache Sidecar
                        try 
                        {
                            var cacheFolder = await ApplicationData.Current.TemporaryFolder.GetFolderAsync("Cache");
                            if (await cacheFolder.TryGetItemAsync($"stream_{song.Id}.jpg") != null)
                            {
                                 localImagePath = $"ms-appdata:///temp/Cache/stream_{song.Id}.jpg";
                            }
                        }
                        catch { }

                        // 2. Fallback to Download
                        if (string.IsNullOrEmpty(localImagePath) && !string.IsNullOrEmpty(song.ImageUrl))
                        {
                            using (var client = new HttpClient())
                            {
                                var buffer = await client.GetByteArrayAsync(new Uri(song.ImageUrl));
                                if (buffer.Length > 0)
                                {
                                    var filename = "tile-" + Guid.NewGuid().ToString() + ".jpg";
                                    var folder = ApplicationData.Current.LocalFolder;
                                    var file = await folder.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting);
                                    await FileIO.WriteBytesAsync(file, buffer);
                                    localImagePath = "ms-appdata:///local/" + filename;

                                    // Clean up old file
                                    if (!string.IsNullOrEmpty(_lastTileImageFile))
                                    {
                                        try 
                                        { 
                                            var oldFile = await folder.GetFileAsync(_lastTileImageFile);
                                            await oldFile.DeleteAsync();
                                        } 
                                        catch {}
                                    }
                                    _lastTileImageFile = filename;
                                }
                            }
                        }
                    }
                }
                catch 
                {
                    // Fallback to no image
                }

                try
                {
                    string xml = "";
                    string title = song != null ? SecurityElement.Escape(song.Title) : "";
                    string artist = song != null ? SecurityElement.Escape(song.Artist) : "";
                    string album = song != null ? SecurityElement.Escape(song.Album) : "";

                    if (!string.IsNullOrEmpty(localImagePath))
                    {
                        // Image + Text (Classic Templates)
                        xml = $@"
                        <tile>
                          <visual version='2'>
                            <binding template='TileMedium' branding='name'>
                              <image src='{localImagePath}' placement='background'/>
                              <text hint-style='caption'>{title}</text>
                              <text hint-style='captionSubtle'>{artist}</text>
                            </binding>
                            <binding template='TileWide' branding='name'>
                              <image src='{localImagePath}' placement='background'/>
                              <text hint-style='base'>{title}</text>
                              <text hint-style='captionSubtle'>{artist}</text>
                              <text hint-style='captionSubtle'>{album}</text>
                            </binding>
                            <binding template='TileLarge' branding='name'>
                              <image src='{localImagePath}' placement='background'/>
                              <text hint-style='base'>{title}</text>
                              <text hint-style='captionSubtle'>{artist}</text>
                              <text hint-style='captionSubtle'>{album}</text>
                            </binding>
                          </visual>
                        </tile>";
                    }
                    else
                    {
                        // Text Only (Classic Templates)
                        xml = $@"
                        <tile>
                          <visual version='2'>
                            <binding template='TileMedium' branding='name'>
                              <text hint-style='caption'>{title}</text>
                              <text hint-style='captionSubtle'>{artist}</text>
                            </binding>
                            <binding template='TileWide' branding='name'>
                              <text hint-style='base'>{title}</text>
                              <text hint-style='captionSubtle'>{artist}</text>
                              <text hint-style='captionSubtle'>{album}</text>
                            </binding>
                            <binding template='TileLarge' branding='name'>
                              <text hint-style='base'>{title}</text>
                              <text hint-style='captionSubtle'>{artist}</text>
                              <text hint-style='captionSubtle'>{album}</text>
                            </binding>
                          </visual>
                        </tile>";
                    }

                    var doc = new XmlDocument();
                    doc.LoadXml(xml);

                    var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                    updater.EnableNotificationQueue(true);
                    
                    var notification = new TileNotification(doc);
                    notification.Tag = "nowplaying"; 
                    updater.Clear(); // Clear previous notifications (Idle tiles or old songs) to prevent cycling
                    updater.Update(notification);
                }
                catch (Exception)
                {
                    // Silent fail in production
                }
            });
        }

        public static async Task PrepareIdleCache()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings.Values;
                bool mixRandom = settings.ContainsKey("MixRandomAlbums") && (bool)settings["MixRandomAlbums"];

                // 1. Get Candidates
                var pinnedAlbums = await PinnedAlbumManager.GetPinnedAlbums();
                var randomSongs = await SubsonicService.Instance.GetRandomSongs(25);
                
                var randomAlbums = randomSongs
                     .Where(x => !string.IsNullOrEmpty(x.CoverArtId)) // Ensure it has art
                     .GroupBy(x => x.CoverArtId) // Group by Image, not Album name
                     .Select(g => g.First())
                     .Where(x => !pinnedAlbums.Any(p => p.Id == x.Id || p.CoverArtId == x.CoverArtId))
                     .ToList();

                var candidates = new System.Collections.Generic.List<SubsonicItem>();

                if (mixRandom)
                {
                    // Mix Mode: Combine all and shuffle together
                    candidates.AddRange(pinnedAlbums);
                    candidates.AddRange(randomAlbums);
                    candidates = candidates.OrderBy(x => Guid.NewGuid()).ToList();
                }
                else
                {
                    // Prioritize Pinned: Shuffle Pinned
                    candidates.AddRange(pinnedAlbums.OrderBy(x => Guid.NewGuid()));
                    
                    // Only fallback to random if NO pinned albums exist at all
                    if (candidates.Count == 0)
                    {
                        candidates.AddRange(randomAlbums.OrderBy(x => Guid.NewGuid()));
                    }
                }

                var folder = ApplicationData.Current.LocalFolder;

                // 2. Cache images (Target 5 slots)
                int count = 0;
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    foreach (var item in candidates)
                    {
                        if (count >= 5) break;
                        if (string.IsNullOrEmpty(item.ImageUrl)) continue;
                        
                        try
                        {
                            var buffer = await client.GetByteArrayAsync(new Uri(item.ImageUrl));
                            if (buffer.Length > 0)
                            {
                                var filename = $"idle_cache_{count}.jpg";
                                var file = await folder.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting);
                                await FileIO.WriteBytesAsync(file, buffer);
                                
                                // Save Metadata
                                settings[$"IdleCache_Title_{count}"] = item.Album ?? "";
                                settings[$"IdleCache_Artist_{count}"] = item.Artist ?? "";
                                settings[$"IdleCache_Image_{count}"] = filename;
                                count++;
                            }
                        }
                        catch { }
                    }
                }

                // Clear remaining slots if any (cleanup old data)
                for (int i = count; i < 5; i++)
                {
                    settings.Remove($"IdleCache_Title_{i}");
                    settings.Remove($"IdleCache_Artist_{i}");
                    settings.Remove($"IdleCache_Image_{i}");
                    
                    try
                    {
                        var oldFile = await folder.TryGetItemAsync($"idle_cache_{i}.jpg");
                        if (oldFile != null) await oldFile.DeleteAsync();
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static async void CleanupOldImages()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    // Delete "tile-GUID.jpg" files (Now Playing history)
                    // Don't delete "idle_cache_*.jpg" here as they are persistent fallback
                    if (file.Name.StartsWith("tile-") && file.Name.EndsWith(".jpg"))
                    {
                        try { await file.DeleteAsync(); } catch { }
                    }
                }
            }
            catch { }
        }

        public static void UpdateFromCache()
        {
             try
             {
                var settings = ApplicationData.Current.LocalSettings.Values;
                var notifications = new System.Collections.Generic.List<TileNotification>();

                for (int i = 0; i < 5; i++)
                {
                    if (!settings.ContainsKey($"IdleCache_Title_{i}")) break;
                    
                    try
                    {
                        string title = (string)settings[$"IdleCache_Title_{i}"];
                        string artist = (string)settings[$"IdleCache_Artist_{i}"];
                        string filename = (string)settings[$"IdleCache_Image_{i}"];
                        
                        string localImagePath = "ms-appdata:///local/" + filename;
                        
                        string xml = $@"
                        <tile>
                          <visual version='2'>
                            <binding template='TileMedium' branding='name'>
                              <image src='{localImagePath}' placement='background'/>
                              <text hint-style='caption'>{SecurityElement.Escape(title)}</text>
                            </binding>
                            <binding template='TileWide' branding='name'>
                              <image src='{localImagePath}' placement='background'/>
                              <text hint-style='base'>{SecurityElement.Escape(title)}</text>
                              <text hint-style='captionSubtle'>{SecurityElement.Escape(artist)}</text>
                            </binding>
                            <binding template='TileLarge' branding='name'>
                              <image src='{localImagePath}' placement='background'/>
                              <text hint-style='base'>{SecurityElement.Escape(title)}</text>
                              <text hint-style='captionSubtle'>{SecurityElement.Escape(artist)}</text>
                            </binding>
                          </visual>
                        </tile>";

                        var doc = new XmlDocument();
                        doc.LoadXml(xml);
                        var notification = new TileNotification(doc);
                        notification.Tag = $"idle_{i}";
                        notifications.Add(notification);
                    }
                    catch { }
                }

                if (notifications.Count > 0)
                {
                    var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                    updater.EnableNotificationQueue(true);
                    updater.Clear();
                    foreach (var n in notifications) updater.Update(n);
                }
             }
             catch { }
        }

        public static void ClearTile()
        {
             try
             {
                 TileUpdateManager.CreateTileUpdaterForApplication().Clear();
             }
             catch { }
        }
    }
    
    public static class SecurityElement
    {
        public static string Escape(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
