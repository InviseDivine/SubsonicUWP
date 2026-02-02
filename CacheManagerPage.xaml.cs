using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class CacheManagerPage : Page
    {
        private ObservableCollection<SubsonicItem> _items = new ObservableCollection<SubsonicItem>();

        public CacheManagerPage()
        {
            this.InitializeComponent();
            CachedList.ItemsSource = _items;
        }

        protected override async void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            try
            {
                await LoadData();
            }
            catch 
            {
                // Last resort crash prevention for async void
                LoadingRing.IsActive = false;
            }
        }

        private async Task LoadData()
        {
            LoadingRing.IsActive = true;
            // Clear immediately to show work is starting
            _items.Clear();
            
            try
            {
                // Run HEAVY deserialization on BG thread
                var list = await Task.Run(async () =>
                {
                    try 
                    {
                        var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                        if (await folder.TryGetItemAsync("local_index.json") is StorageFile indexFile)
                        {
                            using (var stream = await indexFile.OpenStreamForReadAsync())
                            {
                                 if (stream.Length > 0)
                                 {
                                     var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(System.Collections.Generic.List<SubsonicItem>));
                                     return (System.Collections.Generic.List<SubsonicItem>)serializer.ReadObject(stream);
                                 }
                            }
                        }
                    }
                    catch { }
                    return new System.Collections.Generic.List<SubsonicItem>();
                });

                // Batch Update UI (Avoid firing CollectionChanged 1000s of times)
                // Replacing ItemsSource is cheaper than Adding 1000 items
                _items = new ObservableCollection<SubsonicItem>(list);
                CachedList.ItemsSource = _items;
            }
            catch { }
            
            await UpdateStats();
            LoadingRing.IsActive = false;
        }
        
        private async Task UpdateStats()
        {
            try
            {
                // Run HEAVY file iteration on BG thread
                var stats = await Task.Run(async () =>
                {
                    long tBytes = 0;
                    long fBytes = 0;
                    try 
                    {
                        var folder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", CreationCollisionOption.OpenIfExists);
                        var files = await folder.GetFilesAsync();
                        foreach (var f in files)
                        {
                            var p = await f.GetBasicPropertiesAsync();
                            tBytes += (long)p.Size;
                        }
                        
                        // Free Space Logic
                        try
                        {
                            var props = await folder.Properties.RetrievePropertiesAsync(new[] { "System.FreeSpace" });
                            if (props != null && props.ContainsKey("System.FreeSpace"))
                            {
                                fBytes = (long)(ulong)props["System.FreeSpace"];
                            }
                        }
                        catch { }

                        if (fBytes == 0)
                        {
                            try
                            {
                                var local = ApplicationData.Current.LocalFolder;
                                var props = await local.Properties.RetrievePropertiesAsync(new[] { "System.FreeSpace" });
                                if (props != null && props.ContainsKey("System.FreeSpace"))
                                {
                                    fBytes = (long)(ulong)props["System.FreeSpace"];
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    return new { Total = tBytes, Free = fBytes };
                });

                long totalBytes = stats.Total;
                long freeBytes = stats.Free;

                // Rest of logic is cheap math, run on UI thread
                long limitBytes = 1024 * 1024 * 1024; // Default fallback
                var settings = ApplicationData.Current.LocalSettings.Values;
                if (settings.ContainsKey("MaxCacheSizeMB"))
                {
                     int mb = (int)settings["MaxCacheSizeMB"];
                     if (mb > 0 && mb < 900000) 
                     {
                         limitBytes = (long)mb * 1024 * 1024;
                     }
                     else 
                     {
                         if (freeBytes > 0)
                            limitBytes = totalBytes + freeBytes;
                         else
                            limitBytes = Math.Max(totalBytes * 2, 10L * 1024 * 1024 * 1024);
                     }
                }
                else
                {
                     if (freeBytes > 0)
                        limitBytes = totalBytes + freeBytes;
                     else
                        limitBytes = Math.Max(totalBytes * 2, 10L * 1024 * 1024 * 1024);
                }

                double percentage = 0;
                if (limitBytes > 0) percentage = (double)totalBytes / limitBytes;
                if (percentage > 1) percentage = 1;
                
                UsedColumn.Width = new GridLength(percentage, GridUnitType.Star);
                FreeColumn.Width = new GridLength(1 - percentage, GridUnitType.Star);
                
                UsedText.Text = $"Used: {FormatSize(totalBytes)}";
                FreeText.Text = $"Limit: {FormatSize(limitBytes)}";
            }
            catch {}
        }
        
        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void Grid_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SubsonicItem item)
            {
                e.Handled = true;
                if (this.Resources.TryGetValue("SongMenu", out object res) && res is MenuFlyout flyout)
                {
                    // Update Tag of items to hold the ID (since Click handlers expect it)
                    // Or set DataContext of Flyout itself?
                    // Click handlers look at `sender.Tag`. 
                    // When Flyout is shown, its items inherit DataContext from the placement target IF correctly set.
                    // But standard MenuFlyoutItem doesn't automatically inherit context in code-behind show unless we set it.
                    
                    foreach(var child in flyout.Items)
                    {
                        if (child is MenuFlyoutItem mfi) mfi.DataContext = item;
                    }
                    
                    flyout.ShowAt(fe, new Windows.UI.Xaml.Controls.Primitives.FlyoutShowOptions 
                    { 
                        Position = e.GetPosition(fe) 
                    });
                }
            }
        }
        
        private void CachedList_ItemClick(object sender, ItemClickEventArgs e)
        {
            // Play clicked item
            // Logic: Play single song or play all starting from this index? 
            // Standard behavior: Play context.
            if (e.ClickedItem is SubsonicItem song)
            {
                // Simple play: Clear queue, add this song, play.
                // Or better: Play all cached songs starting from this one?
                // Let's go with Play All Cached starting here.
                var index = _items.IndexOf(song);
                if (index < 0) return;
                
                var tracks = _items.ToList(); // Copy
                Services.PlaybackService.Instance.PlayTracks(tracks, index);
            }
        }

        private void PlayNext_Click(object sender, RoutedEventArgs e)
        {
             if (sender is FrameworkElement fe && fe.Tag is string id)
             {
                 var song = _items.FirstOrDefault(x => x.Id == id);
                 if (song != null)
                 {
                     Services.PlaybackService.Instance.PlayNext(song);
                 }
             }
        }

        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
             if (sender is FrameworkElement fe && fe.Tag is string id)
             {
                 var song = _items.FirstOrDefault(x => x.Id == id);
                 if (song != null)
                 {
                     Services.PlaybackService.Instance.AddToQueue(song);
                 }
             }
        }

        private void AddToCache_Click(object sender, RoutedEventArgs e)
        {
             // Placeholder
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Control btn && btn.Tag is string id)
            {
               var item = _items.FirstOrDefault(x => x.Id == id);
               if (item != null) _items.Remove(item);
               
               await Services.PlaybackService.Instance.RemoveFromCache(id);
               await UpdateStats();
            }
        }
        
        private async void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Windows.UI.Popups.MessageDialog("Are you sure you want to delete ALL cached songs? This cannot be undone.", "Clear All Cache");
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes"));
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("No"));
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;

                var result = await dialog.ShowAsync();
                if (result.Label == "Yes")
                {
                     // Active Songs Protection Strategy
                     var keep = new System.Collections.Generic.List<string>();
                     var frame = Window.Current.Content as Frame;
                     if (frame?.Content is MainPage mp && mp.CurrentSong != null)
                     {
                         keep.Add(mp.CurrentSong.Id);
                         if (mp.PlaybackQueue != null)
                         {
                             int nextIndex = mp.CurrentQueueIndex + 1;
                             if (nextIndex < mp.PlaybackQueue.Count) keep.Add(mp.PlaybackQueue[nextIndex].Id);
                             
                             int prevIndex = mp.CurrentQueueIndex - 1;
                             if (prevIndex >= 0 && prevIndex < mp.PlaybackQueue.Count) keep.Add(mp.PlaybackQueue[prevIndex].Id);
                         }
                     }

                     // Ensure Transient
                     foreach (var kid in keep)
                     {
                         await Services.PlaybackService.Instance.EnsureTransient(kid);
                     }

                     // Explicitly Clear All EXCEPT kept IDs
                     await Services.PlaybackService.Instance.ClearAllCache(keep);
                     
                     // UI Cleanup
                     // If we kept the current song, it is now Transient. 
                     // Transient files are NOT shown in the Cache Manager list (usually, as they are not "Cached" permanently).
                     // However, we populated the list from local_index.json which tracks them?
                     // MasterIndex tracks everything.
                     // But if we converted it to Transient, it might still be in MasterIndex.
                     // It is debatable if Transient files should be visible in Cache Manager.
                     // For now, let's just reload the data which reads the index.
                     
                     await LoadData();
                }
            }
            catch { }
        }
    }
}
