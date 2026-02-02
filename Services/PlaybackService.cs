using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace SubsonicUWP.Services
{
    public class PlaybackService
    {
        private static PlaybackService _instance;
        public static PlaybackService Instance => _instance ?? (_instance = new PlaybackService());

        public Windows.Media.Playback.MediaPlayer Player { get; } = new Windows.Media.Playback.MediaPlayer();
        public SubsonicItem CurrentSong { get; set; }

        // In-Memory Index Cache to prevent reading JSON from disk on every operation
        private System.Collections.Generic.List<SubsonicItem> _memoryIndex;
        private readonly SemaphoreSlim _indexLock = new SemaphoreSlim(1, 1);
        private bool _indexLoaded = false;
        
        // OPTIMIZATION: Debounce Index Writes
        private bool _indexDirty = false;
        private Timer _indexSaveTimer;
        
        public bool IsRamDoubleBufferingEnabled { get; set; } = false;

        public PlaybackService()
        {
             Player.AudioCategory = Windows.Media.Playback.MediaPlayerAudioCategory.Media;
             // Ensure it continues in background
             Player.CommandManager.IsEnabled = false; 
             
             // Save index every 15 seconds if dirty, preventing I/O spikes during track changes
             _indexSaveTimer = new Timer(async _ => await SaveIndexToDisk(), null, 15000, 15000);
        }

        private readonly ConcurrentDictionary<string, DownloadContext> _downloads = new ConcurrentDictionary<string, DownloadContext>();
        private readonly HttpClient _client = new HttpClient();

        public class DownloadContext
        {
            public string SongId { get; set; }
            public string FilePath { get; set; }
            public long TotalBytes { get; set; }
            
            // Volatile to ensure atomic reads across threads without locks
            private long _downloadedBytes;
            public long DownloadedBytes 
            {
                get => Interlocked.Read(ref _downloadedBytes);
                set => Interlocked.Exchange(ref _downloadedBytes, value);
            }
            
            public volatile bool IsComplete;
            public volatile bool IsFailed;
            public SemaphoreSlim Signal { get; set; } = new SemaphoreSlim(0);
            
            // RAM Double-Buffering
            public MemoryStream RamStream { get; set; }
            public object StreamLock { get; } = new object();
            public SemaphoreSlim DiskWriteSemaphore { get; set; }

            public async Task WaitForDataAsync()
            {
                // Wait without timeout. The signal will be released when data arrives or download finishes.
                await Signal.WaitAsync();
                
                // Release purely to act as a permanent signal or pulse? 
                // Actually, in the new BufferingStream logic, we assume wait consumes one.
                // But if multiple threads read? (Unlikely).
                // But just in case, if the download is COMPLETE, we should probably always let through.
                // However, the optimized stream logic checks IsComplete explicitly.
            }
            
            public void NotifyDataWritten()
            {
                // Release only if count is 0 to prevent building up a massive count
                if (Signal.CurrentCount == 0) Signal.Release();
            }
            
            public double Progress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes : 0;
        }
        
        public event EventHandler<string> DownloadCompleted;

        public DownloadContext GetRawContext(string id)
        {
            if (_downloads.TryGetValue(id, out var ctx)) return ctx;
            return null;
        }

        private readonly ConcurrentQueue<SubsonicItem> _downloadQueue = new ConcurrentQueue<SubsonicItem>();
        private readonly SemaphoreSlim _downloadConcurrency = new SemaphoreSlim(3, 3);
        private bool _isProcessingQueue = false;

        public void EnqueueDownload(SubsonicItem item, bool isTransient = false)
        {
            // Transient downloads usually go via StartDownload directly (PlayTrack), 
            // but if we ever queue them, we need to track this state.
            // For now, assuming Enqueue is mostly for "Add to Cache" (Permanent).
            // But to be safe, we could stick it on the item or a wrapper?
            // Simpler: Just rely on context. If it's "Add to Cache", transient=false.
            _downloadQueue.Enqueue(item);
            _ = ProcessDownloadQueue(isTransient);
        }

        public void EnqueueDownloads(System.Collections.Generic.IEnumerable<SubsonicItem> items, bool isTransient = false)
        {
            foreach (var item in items) _downloadQueue.Enqueue(item);
            _ = ProcessDownloadQueue(isTransient);
        }

        private async Task ProcessDownloadQueue(bool isTransient = false)
        {
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;

            while (!_downloadQueue.IsEmpty)
            {
                await _downloadConcurrency.WaitAsync();
                
                if (_downloadQueue.TryDequeue(out var song))
                {
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            // Check if already active
                            if (_downloads.ContainsKey(song.Id)) 
                            {
                                // Already downloading or downloaded. Skip.
                                return;
                            }

                            var context = new DownloadContext 
                            { 
                                SongId = song.Id, 
                                TotalBytes = -1
                            };
                            _downloads[song.Id] = context;
                            
                            await DownloadLoop(song, context, isTransient);
                        }
                        finally
                        {
                            _downloadConcurrency.Release();
                        }
                    });
                }
                else
                {
                    _downloadConcurrency.Release();
                }
            }
            _isProcessingQueue = false;
        }

        // --- OPTIMIZED DOWNLOAD LOOP ---

        public async Task<DownloadContext> StartDownload(SubsonicItem song, bool isTransient = false)
        {
            if (_downloads.TryGetValue(song.Id, out var existing)) return existing;

            var context = new DownloadContext 
            { 
                SongId = song.Id, 
                TotalBytes = -1 // Unknown initially
            };
            
            _downloads[song.Id] = context;
            _ = Task.Run(() => DownloadLoop(song, context, isTransient));
            return context;
        }
        
        private class DownloadContextWrapper : DownloadContext { }

        // Events for Queue Management
        public event EventHandler<SubsonicItem> AddToQueueRequested;
        public event EventHandler<SubsonicItem> PlayNextRequested;
        public event EventHandler<Tuple<System.Collections.Generic.List<SubsonicItem>, int>> PlayTracksRequested;
        
        public void AddToQueue(SubsonicItem song) => AddToQueueRequested?.Invoke(this, song);
        
        public void PlayNext(SubsonicItem song) => PlayNextRequested?.Invoke(this, song);
        
        public void PlayTracks(System.Collections.Generic.List<SubsonicItem> tracks, int startIndex) 
        {
            PlayTracksRequested?.Invoke(this, new Tuple<System.Collections.Generic.List<SubsonicItem>, int>(tracks, startIndex));
        }
        
        // --- OPTIMIZED INDEX MANAGEMENT ---

        // --- INDEX MANAGEMENT (DEBOUNCED) ---

        private async Task EnsureIndexLoaded()
        {
            if (_indexLoaded) return;
            await _indexLock.WaitAsync();
            try
            {
                if (_indexLoaded) return;
                var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                var file = await folder.CreateFileAsync("local_index.json", CreationCollisionOption.OpenIfExists);
                string json = await FileIO.ReadTextAsync(file);
                
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                    {
                        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(System.Collections.Generic.List<SubsonicItem>));
                        _memoryIndex = (System.Collections.Generic.List<SubsonicItem>)serializer.ReadObject(ms);
                    }
                }
                else _memoryIndex = new System.Collections.Generic.List<SubsonicItem>();
                
                _indexLoaded = true;
            }
            catch { _memoryIndex = new System.Collections.Generic.List<SubsonicItem>(); }
            finally { _indexLock.Release(); }
        }

        private async Task UpdateMasterIndex(SubsonicItem item, bool isRemoval)
        {
            // Just update memory and mark dirty. Zero I/O here.
            await EnsureIndexLoaded();
            await _indexLock.WaitAsync();
            try
            {
                _memoryIndex.RemoveAll(x => x.Id == item.Id);
                if (!isRemoval) _memoryIndex.Add(item);
                _indexDirty = true;
            }
            finally { _indexLock.Release(); }
        }

        private async Task SaveIndexToDisk()
        {
            if (!_indexDirty || !_indexLoaded) return;
            
            System.Collections.Generic.List<SubsonicItem> snapshot = null;
            await _indexLock.WaitAsync();
            try
            {
                if (!_indexDirty) return;
                snapshot = new System.Collections.Generic.List<SubsonicItem>(_memoryIndex); // Quick copy
                _indexDirty = false;
            }
            finally { _indexLock.Release(); }

            if (snapshot != null)
            {
                try
                {
                    var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                    var file = await folder.CreateFileAsync("local_index.json", CreationCollisionOption.ReplaceExisting);
                    using (var stream = await file.OpenStreamForWriteAsync())
                    {
                        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(System.Collections.Generic.List<SubsonicItem>));
                        serializer.WriteObject(stream, snapshot);
                    }
                }
                catch { _indexDirty = true; /* Retry later */ }
            }
        }
        
        // Helper redirect for older internal calls (though we replaced them mostly)
        private async Task UpdateMasterIndexInternal(SubsonicItem item, bool isRemoval)
        {
             // Map to new method logic if forced
             await UpdateMasterIndex(item, isRemoval);
        }
        
        public async Task RemoveFromCache(string id)
        {
            await _indexLock.WaitAsync();
            try
            {
                var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                
                // Delete Files
                try { (await folder.GetFileAsync($"stream_{id}.mp3")).DeleteAsync().AsTask().Wait(); } catch {}
                try { (await folder.GetFileAsync($"stream_{id}.mp3.0")).DeleteAsync().AsTask().Wait(); } catch {}
                try { (await folder.GetFileAsync($"stream_{id}.json")).DeleteAsync().AsTask().Wait(); } catch {}
                try { (await folder.GetFileAsync($"stream_{id}.tmp")).DeleteAsync().AsTask().Wait(); } catch {}
                try { (await folder.GetFileAsync($"stream_{id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {}
                
                // Update Index
                await UpdateMasterIndexInternal(new SubsonicItem { Id = id }, true);
                
                // Remove from active downloads if running?
                if (_downloads.ContainsKey(id))
                {
                    // Cancel? For now just let it fail/handle itself or user can't delete playing song?
                    // Cache Manager allows delete. If playing, it might error.
                }
            }
            catch { }
            finally
            {
                _indexLock.Release();
            }
        }

        public async Task<int> GetCacheFileCount()
        {
            try
            {
                var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                var files = await folder.GetFilesAsync();
                return files.Count(f => f.Name.EndsWith(".mp3") && !f.Name.EndsWith(".mp3.0"));
            }
            catch { return 0; }
        }

        public async void CleanCache(System.Collections.Generic.IEnumerable<string> keepIds)
        {
             var keepList = keepIds?.ToList() ?? new System.Collections.Generic.List<string>();
             
             await Task.Run(async () =>
             {
                 try
                 {
                     int maxSizeMB = 0;
                     var settings = ApplicationData.Current.LocalSettings.Values;
                     if (settings.ContainsKey("MaxCacheSizeMB"))
                         maxSizeMB = (int)settings["MaxCacheSizeMB"];
                     
                     bool manualMode = false;
                     if (settings.ContainsKey("ManualCacheMode"))
                         manualMode = (bool)settings["ManualCacheMode"];

                     var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                     var files = await folder.GetFilesAsync();
                     
                     if (maxSizeMB == 0)
                     {
                         // Strategy: 
                         // If Manual Mode: Only delete files that HAVE a .tmp sibling (Transient)
                         // If Auto Mode: Delete everything (Aggressive)

                         if (manualMode)
                         {
                             // Manual Mode: Delete only .tmp marked files (unless in keepList)
                             foreach (var file in files)
                             {
                                 if (file.Name.EndsWith(".tmp"))
                                 {
                                     string id = file.Name.Replace("stream_", "").Replace(".tmp", "");
                                     
                                     // Skip protected IDs
                                     if (keepList.Contains(id)) continue;
                                     
                                     // Delete The transient group
                                     try { await file.DeleteAsync(); } catch {}
                                     try { (await folder.GetFileAsync($"stream_{id}.mp3")).DeleteAsync().AsTask().Wait(); } catch {}
                                     try { (await folder.GetFileAsync($"stream_{id}.json")).DeleteAsync().AsTask().Wait(); } catch {}
                                     try { (await folder.GetFileAsync($"stream_{id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {}
                                     
                                     await UpdateMasterIndex(new SubsonicItem { Id = id }, true);
                                 }
                             }
                         }
                         else
                         {
                             // Aggressive cleanup (Legacy/Default) - But respect keepList
                             foreach (var file in files)
                             {
                                 bool isKeep = false;
                                 foreach (var kid in keepList)
                                 {
                                     if (file.Name.Contains(kid)) { isKeep = true; break; }
                                 }
                                 if (isKeep) continue;

                                 if (file.Name == "local_index.json") continue;

                                 try 
                                 { 
                                     var name = file.Name;
                                     await file.DeleteAsync(); 
                                     
                                     if (name.StartsWith("stream_") && name.EndsWith(".mp3"))
                                     {
                                         var id = name.Substring(7).Replace(".mp3", "");
                                         await UpdateMasterIndex(new SubsonicItem { Id = id }, true);
                                     }
                                 } catch { }
                             }
                         }
                     }
                     else
                     {
                         // LRU Cleanup
                         long totalSize = 0;
                         var fileList = new System.Collections.Generic.List<StorageFile>();
                         foreach (var file in files)
                         {
                             if (file.Name.EndsWith(".json") || file.Name.EndsWith(".tmp") || file.Name.EndsWith(".mp3.0") || file.Name.EndsWith(".jpg")) continue; 
                             var props = await file.GetBasicPropertiesAsync();
                             totalSize += (long)props.Size;
                             fileList.Add(file);
                         }
                         
                         long limitBytes = (long)maxSizeMB * 1024 * 1024;
                         
                         if (totalSize > limitBytes)
                         {
                             var filesWithProps = new System.Collections.Generic.List<Tuple<StorageFile, DateTimeOffset>>();
                             foreach (var file in fileList)
                             {
                                 var p = await file.GetBasicPropertiesAsync();
                                 filesWithProps.Add(new Tuple<StorageFile, DateTimeOffset>(file, p.DateModified));
                             }
                             
                             var sorted = filesWithProps.OrderBy(x => x.Item2).ToList();
                             
                             foreach (var item in sorted)
                             {
                                 if (totalSize <= limitBytes) break;
                                 
                                 // Check if protected
                                 bool isKeep = false;
                                 foreach (var kid in keepList)
                                 {
                                     if (item.Item1.Name.Contains(kid)) { isKeep = true; break; }
                                 }
                                 if (isKeep) continue;
                                 
                                 var sz = (await item.Item1.GetBasicPropertiesAsync()).Size;
                                 var name = item.Item1.Name;
                                 
                                 await item.Item1.DeleteAsync();
                                 totalSize -= (long)sz;
                                 
                                 if (name.StartsWith("stream_") && name.EndsWith(".mp3"))
                                 {
                                     var id = name.Substring(7).Replace(".mp3", "");
                                     await UpdateMasterIndex(new SubsonicItem { Id = id }, true);
                                     
                                     try { (await folder.GetFileAsync($"stream_{id}.mp3.0")).DeleteAsync().AsTask().Wait(); } catch {}
                                     try { (await folder.GetFileAsync($"stream_{id}.json")).DeleteAsync().AsTask().Wait(); } catch {}
                                     try { (await folder.GetFileAsync($"stream_{id}.tmp")).DeleteAsync().AsTask().Wait(); } catch {}
                                     try { (await folder.GetFileAsync($"stream_{id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {}
                                 }
                             }
                         }
                     }
                 }
                 catch { }
             });
        }

        public async Task RemoveAllTransientMarkers()
        {
            await Task.Run(async () =>
            {
                try
                {
                    var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                    var files = await folder.GetFilesAsync();
                    foreach (var file in files)
                    {
                        if (file.Name.EndsWith(".tmp"))
                        {
                            try { await file.DeleteAsync(); } catch { }
                        }
                    }
                }
                catch { }
            });
        }

        public async Task EnsureTransient(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                if (await folder.TryGetItemAsync($"stream_{id}.tmp") == null)
                {
                    await folder.CreateFileAsync($"stream_{id}.tmp", CreationCollisionOption.ReplaceExisting);
                }
            }
            catch { }
        }

        public async Task PromoteTransient(string id)
        {
             if (string.IsNullOrEmpty(id)) return;
             await Task.Run(async () =>
             {
                 try
                 {
                     var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                     var file = await folder.TryGetItemAsync($"stream_{id}.tmp") as StorageFile;
                     if (file != null)
                     {
                         await file.DeleteAsync();
                         // Touch the MP3 to refresh LRU
                         await TouchCachedFile(id);
                     }
                 }
                 catch { }
             });
        }

        public async Task ClearAllCache(System.Collections.Generic.IEnumerable<string> keepIds)
        {
             // Deletes ALL files (Permanent and Transient) EXCEPT the specified IDs.
             var keepList = keepIds?.ToList() ?? new System.Collections.Generic.List<string>();
             
             await Task.Run(async () =>
             {
                 try
                 {
                     var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                     var files = await folder.GetFilesAsync();
                     
                     foreach (var file in files)
                     {
                         if (file.Name.StartsWith("stream_") && file.Name.EndsWith(".mp3"))
                         {
                             string id = file.Name.Substring(7).Replace(".mp3", "");
                             
                             if (keepList.Contains(id)) continue;
                             
                             // Delete EVERYTHING else
                             try { await file.DeleteAsync(); } catch {}
                             try { (await folder.GetFileAsync($"stream_{id}.json")).DeleteAsync().AsTask().Wait(); } catch {}
                             try { (await folder.GetFileAsync($"stream_{id}.mp3.0")).DeleteAsync().AsTask().Wait(); } catch {}
                             try { (await folder.GetFileAsync($"stream_{id}.tmp")).DeleteAsync().AsTask().Wait(); } catch {} // Also delete .tmp
                             try { (await folder.GetFileAsync($"stream_{id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {}
                             
                             await UpdateMasterIndex(new SubsonicItem { Id = id }, true);
                         }
                     }
                 }
                 catch { }
             });
        }

        public async Task TouchCachedFile(string id)
        {
             // Updates the timestamp of the file to "Now" to protect it from LRU cleanup
             // Fire and Forget - do not block playback start
             _ = Task.Run(async () =>
             {
                 try
                 {
                     var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                     var file = await folder.GetFileAsync($"stream_{id}.mp3");
                     if (file != null)
                     {
                         // Use try-catch liberally here as file might be locked or gone
                         System.IO.File.SetLastWriteTimeUtc(file.Path, DateTime.UtcNow);
                     }
                 }
                 catch { }
             });
        }

        private async Task DownloadLoop(SubsonicItem song, DownloadContext context, bool isTransient = false)
        {
            const int MAX_RETRIES = 3;
            int retries = 0;
            string url = SubsonicService.Instance.GetStreamUrl(song.Id);

            // Larger buffer for network reads (81KB)
            byte[] buffer = new byte[81920]; 

            while (retries <= MAX_RETRIES)
            {
                StorageFolder cacheFolder = null;
                StorageFile mediaFile = null;
                // Markers
                StorageFile markerFile = null;
                StorageFile jsonFile = null;
                StorageFile tmpMarker = null;
                FileStream diskStream = null;

                try
                {
                    StorageFolder tempRoot = ApplicationData.Current.TemporaryFolder;
                    cacheFolder = await tempRoot.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                    
                    bool alreadyCached = false;
                    
                    // --- EXISTING CACHE CHECK LOGIC ---
                    try 
                    { 
                        mediaFile = await cacheFolder.GetFileAsync($"stream_{song.Id}.mp3");
                        
                        try 
                        {
                            var marker = await cacheFolder.GetFileAsync($"stream_{song.Id}.mp3.0");
                            // Incomplete download
                            await mediaFile.DeleteAsync();
                            await marker.DeleteAsync();
                            try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.json")).DeleteAsync().AsTask().Wait(); } catch {}
                            try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.tmp")).DeleteAsync().AsTask().Wait(); } catch {}
                            try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {}
                            await UpdateMasterIndex(song, true);
                            mediaFile = null;
                            marker = null;
                        }
                        catch (FileNotFoundException)
                        {
                             // Logic to detect if file is valid
                             if (mediaFile != null)
                             {
                                var props = await mediaFile.GetBasicPropertiesAsync();
                                if (props.Size > 1024) 
                                {
                                    // REPAIR LOGIC
                                    try 
                                    {
                                        if (await cacheFolder.TryGetItemAsync($"stream_{song.Id}.json") == null)
                                        {
                                            var newJson = await cacheFolder.CreateFileAsync($"stream_{song.Id}.json", CreationCollisionOption.ReplaceExisting);
                                            using (var stream = await newJson.OpenStreamForWriteAsync())
                                            {
                                                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SubsonicItem));
                                                serializer.WriteObject(stream, song);
                                            }
                                        }
                                        // Skip art repair for brevity, it's non-critical
                                    } catch {}

                                    alreadyCached = true;
                                    context.FilePath = mediaFile.Path;
                                    context.TotalBytes = (long)props.Size;
                                    context.DownloadedBytes = context.TotalBytes;
                                    context.IsComplete = true;
                                    
                                    if (!isTransient)
                                    {
                                        try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.tmp")).DeleteAsync().AsTask().Wait(); } catch {}
                                    }

                                    // Ensure Index (In-Memory now)
                                    await UpdateMasterIndex(song, false);
                                }
                             }
                        }
                    }
                    catch (FileNotFoundException) { }

                    if (alreadyCached)
                    {
                        context.IsComplete = true; 
                        context.Signal.Release();
                        DownloadCompleted?.Invoke(this, context.SongId);
                        return;
                    }

                    // --- NEW DOWNLOAD LOGIC ---
                    
                    if (mediaFile == null || retries > 0)
                        mediaFile = await cacheFolder.CreateFileAsync($"stream_{song.Id}.mp3", CreationCollisionOption.ReplaceExisting);
                    
                    // Metadata Repair
                    if (string.IsNullOrEmpty(song.Album) || string.IsNullOrEmpty(song.Artist))
                    {
                         try
                         {
                             var full = await SubsonicService.Instance.GetSong(song.Id);
                             if (full != null)
                             {
                                 song.Album = full.Album;
                                 song.Artist = full.Artist;
                                 song.CoverArtId = full.CoverArtId;
                                 song.Duration = full.Duration;
                                 song.Title = full.Title;
                                 song.AlbumId = full.AlbumId;
                                 song.ArtistId = full.ArtistId;
                             }
                         }
                         catch { }
                    }

                    markerFile = await cacheFolder.CreateFileAsync($"stream_{song.Id}.mp3.0", CreationCollisionOption.ReplaceExisting);
                    
                    // Transient Logic
                    bool manualMode = false;
                    try 
                    {
                        var settings = ApplicationData.Current.LocalSettings.Values;
                        if (settings.ContainsKey("ManualCacheMode")) manualMode = (bool)settings["ManualCacheMode"];
                    } catch {}

                    if (isTransient && manualMode)
                    {
                        tmpMarker = await cacheFolder.CreateFileAsync($"stream_{song.Id}.tmp", CreationCollisionOption.ReplaceExisting);
                    }
                    else
                    {
                        try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.tmp")).DeleteAsync().AsTask().Wait(); } catch {}
                    }
                    
                    jsonFile = await cacheFolder.CreateFileAsync($"stream_{song.Id}.json", CreationCollisionOption.ReplaceExisting);
                    using (var stream = await jsonFile.OpenStreamForWriteAsync())
                    {
                        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SubsonicItem));
                        serializer.WriteObject(stream, song);
                    }

                    context.FilePath = mediaFile.Path;
                    context.DownloadedBytes = 0; 
                    
                    // --- PREPARE STREAMS ---
                    if (IsRamDoubleBufferingEnabled)
                    {
                        context.RamStream = new MemoryStream();
                        context.DiskWriteSemaphore = new SemaphoreSlim(1, 1);
                        diskStream = new FileStream(context.FilePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    }
                    else
                    {
                         diskStream = new FileStream(context.FilePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    }
                    
                    try
                    {
                        using (var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (!response.IsSuccessStatusCode) throw new Exception("Http Error " + response.StatusCode);
                            
                            if (response.Content.Headers.ContentLength.HasValue)
                            {
                                context.TotalBytes = response.Content.Headers.ContentLength.Value;
                                // Optimization: Pre-allocate RAM Stream to avoid resizing locks
                                if (IsRamDoubleBufferingEnabled && context.RamStream != null && context.TotalBytes < int.MaxValue)
                                {
                                    context.RamStream.Capacity = (int)context.TotalBytes;
                                }
                            }
                            else if (context.TotalBytes <= 0)
                            {
                                context.TotalBytes = 20 * 1024 * 1024; // Default fallback to 20MB
                                if (IsRamDoubleBufferingEnabled && context.RamStream != null)
                                {
                                     context.RamStream.Capacity = 20 * 1024 * 1024;
                                }
                            }

                            using (var netStream = await response.Content.ReadAsStreamAsync())
                            {
                                int read;
                                int bytesSinceFlush = 0;
                                int currentFlushThreshold = 16 * 1024; // Start small (16KB) for instant start

                                Task diskWriterTask = null;

                                if (IsRamDoubleBufferingEnabled)
                                {
                                    // Start Single Background Writer Task
                                    // Gentle Heartbeat Mode: Small 128KB chunks written slowly (256KB/s)
                                    // This prevents "spikes" and keeps disk active time low.
                                    diskWriterTask = Task.Run(async () => 
                                    {
                                        byte[] writeBuffer = new byte[128 * 1024]; // 128KB Chunk
                                        long writtenToDisk = 0;
                                        
                                        while (!context.IsComplete && !context.IsFailed)
                                        {
                                            int toWrite = 0;
                                            
                                            // 1. Read from RAM (Thread-Safe)
                                            lock (context.StreamLock) 
                                            {
                                                long available = context.RamStream.Length - writtenToDisk;
                                                if (available > 0)
                                                {
                                                    context.RamStream.Position = writtenToDisk;
                                                    toWrite = (int)Math.Min(available, writeBuffer.Length);
                                                    context.RamStream.Read(writeBuffer, 0, toWrite);
                                                }
                                            }

                                            if (toWrite > 0)
                                            {
                                                // 2. Write to Disk
                                                await diskStream.WriteAsync(writeBuffer, 0, toWrite);
                                                await diskStream.FlushAsync();
                                                writtenToDisk += toWrite;
                                                
                                                // Throttle to 256KB/s (approx 3-4x realtime audio)
                                                // 256KB = 262144 bytes per second.
                                                // Delay = (bytes / 262144.0) * 1000
                                                // 128KB chunk takes ~500ms
                                                int delay = (int)((toWrite / 262144.0) * 1000);
                                                if (delay > 0) await Task.Delay(delay);
                                            }
                                            else
                                            {
                                                await Task.Delay(200); // Check less frequently (5Hz)
                                            }
                                        }
                                        
                                        // Final Flush of remaining data
                                        while (true)
                                        {
                                            int toWrite = 0;
                                            lock (context.StreamLock) 
                                            {
                                                long available = context.RamStream.Length - writtenToDisk;
                                                if (available <= 0) break;
                                                context.RamStream.Position = writtenToDisk;
                                                toWrite = (int)Math.Min(available, writeBuffer.Length);
                                                context.RamStream.Read(writeBuffer, 0, toWrite);
                                            }
                                            
                                            if (toWrite > 0)
                                            {
                                                await diskStream.WriteAsync(writeBuffer, 0, toWrite);
                                                await diskStream.FlushAsync();
                                                writtenToDisk += toWrite;
                                                await Task.Delay(10); 
                                            }
                                            else
                                            {
                                                break; 
                                            }
                                        }
                                    });
                                }

                                while ((read = await netStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    if (IsRamDoubleBufferingEnabled)
                                    {
                                        lock (context.StreamLock)
                                        {
                                            context.RamStream.Seek(0, SeekOrigin.End);
                                            context.RamStream.Write(buffer, 0, read);
                                        }
                                        context.DownloadedBytes += read;
                                        context.NotifyDataWritten(); 
                                    }
                                    else
                                    {
                                        await diskStream.WriteAsync(buffer, 0, read);
                                        bytesSinceFlush += read;
                                        if (bytesSinceFlush >= currentFlushThreshold)
                                        {
                                            await diskStream.FlushAsync();
                                            context.DownloadedBytes += bytesSinceFlush; 
                                            context.NotifyDataWritten();
                                            bytesSinceFlush = 0;
                                            if (context.DownloadedBytes > 256 * 1024) 
                                                currentFlushThreshold = 512 * 1024;
                                        }
                                    }
                                }
                                
                                if (IsRamDoubleBufferingEnabled)
                                {
                                    context.IsComplete = true; 
                                    if (diskWriterTask != null) await diskWriterTask;
                                }
                                else if (bytesSinceFlush > 0)
                                {
                                    await diskStream.FlushAsync();
                                    context.DownloadedBytes += bytesSinceFlush;
                                }
                            }
                        }
                    }
                    finally
                    {
                        diskStream?.Dispose();
                    }
                    
                    // Cleanup & Logic (Offloaded to Background Task to prevent UI stutter at track end)
                    _ = Task.Run(async () =>
                    {
                        try 
                        {
                            await markerFile.DeleteAsync();
                        } catch {}

                        // Sidecar Art (Simplified)
                        if (!string.IsNullOrEmpty(song.CoverArtId))
                        {
                            try
                            {
                                var artUrl = SubsonicService.Instance.GetCoverArtUrl(song.CoverArtId);
                                var artFile = await cacheFolder.CreateFileAsync($"stream_{song.Id}.jpg", CreationCollisionOption.ReplaceExisting);
                                using (var artResponse = await _client.GetAsync(artUrl))
                                {
                                    if (artResponse.IsSuccessStatusCode)
                                    {
                                        using (var fs = await artFile.OpenStreamForWriteAsync()) await artResponse.Content.CopyToAsync(fs);
                                    }
                                    else await artFile.DeleteAsync();
                                }
                            }
                            catch { try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {} }
                        }
                        
                        // Update Index on Success
                        await UpdateMasterIndex(song, false);
                    });

                    // MAIN TASK RETURNS IMMEDIATELY
                    context.IsComplete = true;
                    context.NotifyDataWritten();
                    DownloadCompleted?.Invoke(this, context.SongId);
                    return; 
                }
                catch (Exception)
                {
                    retries++;
                    diskStream?.Dispose(); // Ensure disposed before cleanup
                    
                    // Cleanup
                    try 
                    {
                        if (mediaFile != null) await mediaFile.DeleteAsync();
                        if (markerFile != null) await markerFile.DeleteAsync();
                        if (jsonFile != null) await jsonFile.DeleteAsync();
                        if (tmpMarker != null) await tmpMarker.DeleteAsync();
                        if (cacheFolder != null) try { (await cacheFolder.GetFileAsync($"stream_{song.Id}.jpg")).DeleteAsync().AsTask().Wait(); } catch {}
                        
                        await UpdateMasterIndex(song, true);
                    }
                    catch { }
                    
                    context.DownloadedBytes = 0;
                    context.NotifyDataWritten(); 
                    
                    if (retries > MAX_RETRIES)
                    {
                        context.IsFailed = true;
                        context.IsComplete = true;
                        context.NotifyDataWritten(); // Wake up reader to read EOF
                        _downloads.TryRemove(song.Id, out _);
                    }
                    else
                    {
                        await Task.Delay(2000);
                    }
                }
            }
        }

    }
}
