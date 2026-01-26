using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;

namespace SubsonicUWP
{
    public class SubsonicService
    {
        private static SubsonicService _instance;
        public static SubsonicService Instance => _instance ?? (_instance = new SubsonicService());

        private HttpClient _client;
        
        // TODO: Load from Settings
        public string ServerUrl { get; set; } = "http://demo.subsonic.org"; 
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";

        public SubsonicService()
        {
            _client = new HttpClient();
        }

        public async Task<bool> PingServer()
        {
            var url = BuildUrl("ping.view");
            try
            {
                var response = await _client.GetStringAsync(url);
                return response.Contains("ok");
            }
            catch
            {
                return false;
            }
        }

        public string GetCoverArtUrl(string coverArtId)
        {
            if (string.IsNullOrEmpty(coverArtId)) return null;
            return BuildUrl("getCoverArt.view", $"&id={coverArtId}&size=600");
        }

        public string GetStreamUrl(string id)
        {
             return BuildUrl("stream.view", $"&id={id}");
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetRandomSongs(int size = 50)
        {
            var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
            var url = BuildUrl("getRandomSongs.view", $"&size={size}");
            
            var xml = await _client.GetStringAsync(url);
            var doc = XDocument.Parse(xml);
            var ns = doc.Root.Name.Namespace; // Usually empty for Subsonic but good to be safe
            var nodes = doc.Descendants(ns + "song");

            foreach (var elem in nodes)
            {
                list.Add(new SubsonicItem 
                { 
                    Id = (string)elem.Attribute("id"),
                    Title = (string)elem.Attribute("title"),
                    Artist = (string)elem.Attribute("artist"),
                    CoverArtId = (string)elem.Attribute("coverArt"),
                    Duration = (int?)elem.Attribute("duration") ?? 0,
                    IsStarred = elem.Attribute("starred") != null,
                    ArtistId = (string)elem.Attribute("artistId"),
                    AlbumId = (string)elem.Attribute("albumId") ?? (string)elem.Attribute("parent") 
                });
            }
            return list;
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetStarred()
        {
            var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
            var url = BuildUrl("getStarred.view");

            var xml = await _client.GetStringAsync(url);
            var doc = new Windows.Data.Xml.Dom.XmlDocument();
            doc.LoadXml(xml);
            // Subsonic returns <starred><song .../><album .../></starred>
            // We'll focus on songs for now, but could add albums too.
            var nodes = doc.SelectNodes("//*[local-name()='song']");

            foreach (var node in nodes)
            {
                var elem = node as Windows.Data.Xml.Dom.XmlElement;
                list.Add(new SubsonicItem
                {
                    Id = elem.GetAttribute("id"),
                    Title = elem.GetAttribute("title"),
                    Artist = elem.GetAttribute("artist"),
                    CoverArtId = elem.GetAttribute("coverArt"),
                    Duration = int.TryParse(elem.GetAttribute("duration"), out int d) ? d : 0,
                    IsStarred = true, // Returned by getStarred, so obviously starred
                    Created = DateTime.TryParse(elem.GetAttribute("created"), out DateTime c) ? c : DateTime.MinValue,
                    ArtistId = elem.GetAttribute("artistId"),
                    AlbumId = !string.IsNullOrEmpty(elem.GetAttribute("albumId")) ? elem.GetAttribute("albumId") : elem.GetAttribute("parent")
                });
            }
            return list;
        }

        public async Task<bool> StarItem(string id)
        {
            var url = BuildUrl("star.view", $"&id={id}");
            try
            {
                var response = await _client.GetStringAsync(url);
                return response.Contains("ok");
            }
            catch { return false; }
        }

        public async Task<bool> UnstarItem(string id)
        {
            var url = BuildUrl("unstar.view", $"&id={id}");
            try
            {
                var response = await _client.GetStringAsync(url);
                return response.Contains("ok");
            }
            catch { return false; }
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetArtists()
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             var url = BuildUrl("getIndexes.view"); 
             
             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             // Flatten artist structure for simple list, ignoring namespace issues
             var nodes = doc.SelectNodes("//*[local-name()='artist']");
             foreach (var node in nodes) {
                 var elem = node as Windows.Data.Xml.Dom.XmlElement;
                 list.Add(new SubsonicItem {
                      Id = elem.GetAttribute("id"),
                      Title = elem.GetAttribute("name"), 
                      Artist = "Artist", 
                      CoverArtId = null 
                 });
             }
             return list;
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetAlbumList(string type = "newest", int offset = 0, int size = 20)
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             var url = BuildUrl("getAlbumList.view", $"&type={type}&size={size}&offset={offset}");

             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             var nodes = doc.GetElementsByTagName("album");
             foreach (var node in nodes) {
                 var elem = node as Windows.Data.Xml.Dom.XmlElement;
                 list.Add(new SubsonicItem {
                      Id = elem.GetAttribute("id"),
                      Title = elem.GetAttribute("title"),
                      Artist = elem.GetAttribute("artist"),
                      CoverArtId = elem.GetAttribute("coverArt"),
                      IsStarred = !string.IsNullOrEmpty(elem.GetAttribute("starred"))
                 });
             }
             return list;
        }

        public async Task<bool> CreatePlaylist(string name, System.Collections.Generic.IEnumerable<string> songIds)
        {
            var ids = string.Join("&songId=", songIds); 
            var url = BuildUrl("createPlaylist.view", $"&name={Uri.EscapeDataString(name)}&songId={ids}");
            try
            {
                var response = await _client.GetStringAsync(url);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(response);
                var root = doc.DocumentElement;
                return root.GetAttribute("status") == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdatePlaylist(string playlistId, System.Collections.Generic.IEnumerable<string> songIds)
        {
            var ids = string.Join("&songId=", songIds);
            var url = BuildUrl("createPlaylist.view", $"&playlistId={playlistId}&songId={ids}");
            try
            {
                var response = await _client.GetStringAsync(url);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(response);
                var root = doc.DocumentElement;
                return root.GetAttribute("status") == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> AddToPlaylist(string playlistId, string songId)
        {
            var url = BuildUrl("updatePlaylist.view", $"&playlistId={playlistId}&songIdToAdd={songId}");
            try
            {
                var response = await _client.GetStringAsync(url);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(response);
                var root = doc.DocumentElement;
                return root.GetAttribute("status") == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RenamePlaylist(string playlistId, string newName)
        {
            var url = BuildUrl("updatePlaylist.view", $"&playlistId={playlistId}&name={Uri.EscapeDataString(newName)}");
            try
            {
                var response = await _client.GetStringAsync(url);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(response);
                var root = doc.DocumentElement;
                return root.GetAttribute("status") == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeletePlaylist(string id)
        {
            var url = BuildUrl("deletePlaylist.view", $"&id={id}");
            try
            {
                var response = await _client.GetStringAsync(url);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(response);
                var root = doc.DocumentElement;
                return root.GetAttribute("status") == "ok";
            }
            catch
            {
                return false;
            }
        }

        public string BuildUrl(string method, string query = "")
        {
            var salt = "s" + new Random().Next(1000, 9999);
            var token = CreateMD5(Password + salt);
            return $"{ServerUrl}/rest/{method}?u={Username}&t={token}&s={salt}&v=1.12.0&c=SubsonicUWP{query}";
        }

        private string CreateMD5(string input)
        {
            var hasher = Windows.Security.Cryptography.Core.HashAlgorithmProvider.OpenAlgorithm(Windows.Security.Cryptography.Core.HashAlgorithmNames.Md5);
            var buff = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(input, Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
            var hashed = hasher.HashData(buff);
            return Windows.Security.Cryptography.CryptographicBuffer.EncodeToHexString(hashed);
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetAlbum(string id)
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             var url = BuildUrl("getAlbum.view", $"&id={id}");

             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             var nodes = doc.GetElementsByTagName("song");
             foreach (var node in nodes)
             {
                 var elem = node as Windows.Data.Xml.Dom.XmlElement;
                 DateTime.TryParse(elem.GetAttribute("created"), out DateTime created);
                 
                 list.Add(new SubsonicItem 
                 { 
                     Id = elem.GetAttribute("id"),
                     Title = elem.GetAttribute("title"),
                     Artist = elem.GetAttribute("artist"),
                     CoverArtId = elem.GetAttribute("coverArt"),
                     Duration = int.TryParse(elem.GetAttribute("duration"), out int d) ? d : 0,
                     Album = elem.GetAttribute("album"),
                     Created = created,
                     IsStarred = !string.IsNullOrEmpty(elem.GetAttribute("starred")),
                     ArtistId = elem.GetAttribute("artistId"),
                     AlbumId = !string.IsNullOrEmpty(elem.GetAttribute("albumId")) ? elem.GetAttribute("albumId") : elem.GetAttribute("parent")
                 });
             }
             return list;
        }

        public async Task<SubsonicItem> GetSong(string id)
        {
             var url = BuildUrl("getSong.view", $"&id={id}");
             try
             {
                var xml = await _client.GetStringAsync(url);
                var doc = new Windows.Data.Xml.Dom.XmlDocument();
                doc.LoadXml(xml);
                var node = doc.GetElementsByTagName("song").FirstOrDefault();
                if (node != null)
                {
                    var elem = node as Windows.Data.Xml.Dom.XmlElement;
                    DateTime.TryParse(elem.GetAttribute("created"), out DateTime created);
                    
                    return new SubsonicItem 
                    { 
                        Id = elem.GetAttribute("id"),
                        Title = elem.GetAttribute("title"),
                        Artist = elem.GetAttribute("artist"),
                        CoverArtId = elem.GetAttribute("coverArt"),
                        Duration = int.TryParse(elem.GetAttribute("duration"), out int d) ? d : 0,
                        Album = elem.GetAttribute("album"),
                        Created = created,
                        IsStarred = !string.IsNullOrEmpty(elem.GetAttribute("starred")),
                        ArtistId = elem.GetAttribute("artistId"),
                        AlbumId = !string.IsNullOrEmpty(elem.GetAttribute("albumId")) ? elem.GetAttribute("albumId") : elem.GetAttribute("parent")
                    };
                }
             }
             catch { }
             return null;
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetRecentSongs(int albumOffset = 0, int albumCount = 6)
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();

             var albums = await GetAlbumList("newest", albumOffset, albumCount);
             if (albums.Count > 0)
             {
                 var tasks = albums.Select(a => GetAlbum(a.Id));
                 var results = await Task.WhenAll(tasks);
                 
                 var allSongs = new System.Collections.Generic.List<SubsonicItem>();
                 foreach (var albumTracks in results)
                 {
                     allSongs.AddRange(albumTracks);
                 }
                 
                 foreach (var song in allSongs.OrderByDescending(s => s.Created))
                 {
                     list.Add(song);
                 }
             }
             
             if (list.Count == 0 && albumOffset == 0) return await GetRandomSongs();
             
             return list;
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetPlaylists()
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             var url = BuildUrl("getPlaylists.view");

             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             var nodes = doc.GetElementsByTagName("playlist");
             foreach (var node in nodes)
             {
                 var elem = node as Windows.Data.Xml.Dom.XmlElement;
                 list.Add(new SubsonicItem 
                 { 
                     Id = elem.GetAttribute("id"),
                     Title = elem.GetAttribute("name"),
                     Artist = $"{elem.GetAttribute("songCount")} Songs", 
                     CoverArtId = null 
                 });
             }
             return list;
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetPlaylist(string id)
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             var url = BuildUrl("getPlaylist.view", $"&id={id}");

             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             var nodes = doc.GetElementsByTagName("entry");
             foreach (var node in nodes)
             {
                 var elem = node as Windows.Data.Xml.Dom.XmlElement;
                 list.Add(new SubsonicItem 
                 { 
                     Id = elem.GetAttribute("id"),
                     Title = elem.GetAttribute("title"),
                     Artist = elem.GetAttribute("artist"),
                     CoverArtId = elem.GetAttribute("coverArt"),
                     Duration = int.TryParse(elem.GetAttribute("duration"), out int d) ? d : 0,
                     ArtistId = elem.GetAttribute("artistId"),
                     AlbumId = !string.IsNullOrEmpty(elem.GetAttribute("albumId")) ? elem.GetAttribute("albumId") : elem.GetAttribute("parent")
                 });
             }
             return list;
        }

        public async Task<System.Collections.ObjectModel.ObservableCollection<SubsonicItem>> GetArtistTopSongs(string artistName)
        {
             var list = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             if (string.IsNullOrEmpty(artistName)) return list;
             
             var url = BuildUrl("getTopSongs.view", $"&artist={Uri.EscapeDataString(artistName)}&count=50");

             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             var nodes = doc.GetElementsByTagName("song");
             foreach (var node in nodes)
             {
                 var elem = node as Windows.Data.Xml.Dom.XmlElement;
                 list.Add(new SubsonicItem 
                 { 
                     Id = elem.GetAttribute("id"),
                     Title = elem.GetAttribute("title"),
                     Artist = elem.GetAttribute("artist"),
                     CoverArtId = elem.GetAttribute("coverArt"),
                     Duration = int.TryParse(elem.GetAttribute("duration"), out int d) ? d : 0,
                     ArtistId = elem.GetAttribute("artistId"),
                     AlbumId = !string.IsNullOrEmpty(elem.GetAttribute("albumId")) ? elem.GetAttribute("albumId") : elem.GetAttribute("parent")
                 });
             }
             return list;
        }

        public async Task<Tuple<SubsonicItem, System.Collections.ObjectModel.ObservableCollection<SubsonicItem>>> GetArtist(string id)
        {
             var albums = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
             var artistParams = new SubsonicItem();
             
             var url = BuildUrl("getArtist.view", $"&id={id}");

             var xml = await _client.GetStringAsync(url);
             var doc = new Windows.Data.Xml.Dom.XmlDocument();
             doc.LoadXml(xml);
             var artistParamsNode = doc.GetElementsByTagName("artist").FirstOrDefault();
             if (artistParamsNode != null)
             {
                 var elem = artistParamsNode as Windows.Data.Xml.Dom.XmlElement;
                 artistParams.Id = elem.GetAttribute("id");
                 artistParams.Title = elem.GetAttribute("name");
                 artistParams.CoverArtId = elem.GetAttribute("coverArt"); 
                 
                 var albumNodes = elem.SelectNodes("*[local-name()='album']"); 
                 foreach (var node in albumNodes)
                 {
                     var albElem = node as Windows.Data.Xml.Dom.XmlElement;
                     albums.Add(new SubsonicItem 
                     { 
                         Id = albElem.GetAttribute("id"),
                         Title = albElem.GetAttribute("name"),
                         Artist = albElem.GetAttribute("artist"),
                         CoverArtId = albElem.GetAttribute("coverArt"),
                         Duration = int.TryParse(albElem.GetAttribute("duration"), out int d) ? d : 0,
                         Created = DateTime.TryParse(albElem.GetAttribute("created"), out DateTime c) ? c : DateTime.MinValue,
                         IsStarred = !string.IsNullOrEmpty(albElem.GetAttribute("starred"))
                     });
                 }
             }
             return new Tuple<SubsonicItem, System.Collections.ObjectModel.ObservableCollection<SubsonicItem>>(artistParams, albums);
        }
        public async Task<SearchResult> Search(string query)
        {
             var result = new SearchResult();
             var url = BuildUrl("search3.view", $"&query={Uri.EscapeDataString(query)}&songCount=20&albumCount=10&artistCount=10");
             
             try 
             {
                 var xml = await _client.GetStringAsync(url);
                 var doc = new Windows.Data.Xml.Dom.XmlDocument();
                 doc.LoadXml(xml);
                 var searchResultNode = doc.GetElementsByTagName("searchResult3").FirstOrDefault();
                 
                 if (searchResultNode != null)
                 {
                     var songNodes = searchResultNode.SelectNodes("*[local-name()='song']");
                     foreach (var node in songNodes)
                     {
                         var elem = node as Windows.Data.Xml.Dom.XmlElement;
                         result.Songs.Add(new SubsonicItem 
                         { 
                            Id = elem.GetAttribute("id"),
                            Title = elem.GetAttribute("title"),
                            Artist = elem.GetAttribute("artist"),
                            CoverArtId = elem.GetAttribute("coverArt"),
                            Duration = int.TryParse(elem.GetAttribute("duration"), out int d) ? d : 0,
                            IsStarred = !string.IsNullOrEmpty(elem.GetAttribute("starred")),
                            ArtistId = elem.GetAttribute("artistId"),
                            AlbumId = !string.IsNullOrEmpty(elem.GetAttribute("albumId")) ? elem.GetAttribute("albumId") : elem.GetAttribute("parent")
                         });
                     }
                     
                     var albumNodes = searchResultNode.SelectNodes("*[local-name()='album']");
                     foreach (var node in albumNodes)
                     {
                         var elem = node as Windows.Data.Xml.Dom.XmlElement;
                         result.Albums.Add(new SubsonicItem 
                         { 
                            Id = elem.GetAttribute("id"),
                            Title = elem.GetAttribute("title"), // Album title usually 'name' or 'title' in search3 depending on version, usually 'name' or 'title'. Check docs. 
                            // Actually search3 returns 'name' for album name usually, but let's check 'name' then 'title'
                            IsStarred = !string.IsNullOrEmpty(elem.GetAttribute("starred")),
                            Artist = elem.GetAttribute("artist"),
                            CoverArtId = elem.GetAttribute("coverArt")
                         });
                         // Fix title if empty
                         if (string.IsNullOrEmpty(result.Albums.Last().Title)) result.Albums.Last().Title = elem.GetAttribute("name");
                     }

                     var artistNodes = searchResultNode.SelectNodes("*[local-name()='artist']");
                     foreach (var node in artistNodes)
                     {
                         var elem = node as Windows.Data.Xml.Dom.XmlElement;
                         result.Artists.Add(new SubsonicItem 
                         { 
                            Id = elem.GetAttribute("id"),
                            Title = elem.GetAttribute("name"),
                            Artist = "Artist",
                            CoverArtId = elem.GetAttribute("coverArt")
                         });
                     }
                 }
             }
             catch { }
             return result;
        }
    }

    public class SearchResult
    {
        public System.Collections.ObjectModel.ObservableCollection<SubsonicItem> Songs { get; set; } = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
        public System.Collections.ObjectModel.ObservableCollection<SubsonicItem> Albums { get; set; } = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
        public System.Collections.ObjectModel.ObservableCollection<SubsonicItem> Artists { get; set; } = new System.Collections.ObjectModel.ObservableCollection<SubsonicItem>();
    }
}
