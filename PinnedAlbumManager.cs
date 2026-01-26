using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Data.Json;

namespace SubsonicUWP
{
    public static class PinnedAlbumManager
    {
        private const string FILENAME = "pinned_albums.json";
        private static List<SubsonicItem> _cache = null;

        public static async Task<List<SubsonicItem>> GetPinnedAlbums()
        {
            if (_cache != null) return _cache;

            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(FILENAME);
                var json = await FileIO.ReadTextAsync(file);
                
                // Simple manual deserialization or use a library if available
                // Assuming SubsonicItem has properties we can resurrect
                // For now, let's use a simple JSON array parsing
                // Since we don't have System.Text.Json easily available in this legacy context without NuGet, 
                // we'll use Windows.Data.Json or simple string split if complex.
                // improved: Use Windows.Data.Json
                
                var jsonArray = JsonArray.Parse(json);
                _cache = new List<SubsonicItem>();

                foreach (var val in jsonArray)
                {
                    var obj = val.GetObject();
                    var item = new SubsonicItem();
                    item.Id = GetString(obj, "Id");
                    item.Title = GetString(obj, "Title");
                    item.Album = GetString(obj, "Album");
                    item.Artist = GetString(obj, "Artist");
                    item.CoverArtId = GetString(obj, "CoverArt");
                    _cache.Add(item);
                }
            }
            catch 
            {
                _cache = new List<SubsonicItem>();
            }

            return _cache;
        }

        private static string GetString(JsonObject obj, string key)
        {
            return obj.Keys.Contains(key) ? obj.GetNamedString(key) : null;
        }

        public static async Task PinAlbum(SubsonicItem album)
        {
            var albums = await GetPinnedAlbums();
            if (albums.Any(x => x.Id == album.Id)) return;

            albums.Add(album);
            await Save();
            
            // Trigger Tile Update
            await TileManager.PrepareIdleCache();
            TileManager.UpdateFromCache();
        }

        public static async Task UnpinAlbum(string albumId)
        {
            var albums = await GetPinnedAlbums();
            var existing = albums.FirstOrDefault(x => x.Id == albumId || (x.Album == albumId)); // Handle ID vs Name matching loosely? No, strict ID.
            
            // Subsonic "Album" items usually have ID.
            if (existing != null)
            {
                albums.Remove(existing);
                await Save();
                
                await TileManager.PrepareIdleCache();
                TileManager.UpdateFromCache();
            }
            else
            {
                // Try finding by ID match
                var match = albums.FirstOrDefault(x => x.Id == albumId);
                if (match != null)
                {
                    albums.Remove(match);
                    await Save();
                    await TileManager.PrepareIdleCache();
                    TileManager.UpdateFromCache();
                }
            }
        }

        public static async Task<bool> IsPinned(string albumId)
        {
            var albums = await GetPinnedAlbums();
            return albums.Any(x => x.Id == albumId);
        }

        private static async Task Save()
        {
            if (_cache == null) return;

            var jsonArray = new JsonArray();
            foreach (var item in _cache)
            {
                var obj = new JsonObject();
                obj.SetNamedValue("Id", JsonValue.CreateStringValue(item.Id ?? ""));
                obj.SetNamedValue("Title", JsonValue.CreateStringValue(item.Title ?? ""));
                obj.SetNamedValue("Album", JsonValue.CreateStringValue(item.Album ?? ""));
                obj.SetNamedValue("Artist", JsonValue.CreateStringValue(item.Artist ?? ""));
                obj.SetNamedValue("CoverArt", JsonValue.CreateStringValue(item.CoverArtId ?? ""));
                jsonArray.Add(obj);
            }

            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync(FILENAME, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, jsonArray.ToString());
        }
    }
}
