using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
            LoadPinnedAlbums();
        }

        private bool _isLoading = true;

        private void LoadSettings()
        {
            _isLoading = true;
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey("ServerUrl")) SubsonicService.Instance.ServerUrl = localSettings.Values["ServerUrl"] as string;
                if (localSettings.Values.ContainsKey("Username")) SubsonicService.Instance.Username = localSettings.Values["Username"] as string;
                if (localSettings.Values.ContainsKey("Password")) SubsonicService.Instance.Password = localSettings.Values["Password"] as string;
    
                ServerUrlBox.Text = SubsonicService.Instance.ServerUrl ?? "";
                UsernameBox.Text = SubsonicService.Instance.Username ?? "";
                PasswordBox.Password = SubsonicService.Instance.Password ?? "";
    
                // Load Theme
                string currentTheme = ThemeHelper.CurrentTheme;
                foreach (ComboBoxItem item in ThemeValidator.Items)
                {
                    if (item.Tag.ToString() == currentTheme)
                    {
                        ThemeValidator.SelectedItem = item;
                        break;
                    }
                }
                
                // Load Mix Random Setting
                if (localSettings.Values.ContainsKey("MixRandomAlbums"))
                {
                    MixRandomSwitch.IsOn = (bool)localSettings.Values["MixRandomAlbums"];
                }

                // Load System Buffering (Default: True)
                if (localSettings.Values.ContainsKey("SystemBuffering"))
                    SystemBufferingSwitch.IsOn = (bool)localSettings.Values["SystemBuffering"];
                else
                    SystemBufferingSwitch.IsOn = false;

                // Load Force Caching (Default: False)
                if (localSettings.Values.ContainsKey("ForceCaching"))
                    ForceCachingSwitch.IsOn = (bool)localSettings.Values["ForceCaching"];

                // Load Aggressive Caching (Default: False)
                if (localSettings.Values.ContainsKey("AggressiveCaching"))
                    AggressiveCachingSwitch.IsOn = (bool)localSettings.Values["AggressiveCaching"];

                // Load RAM Double Buffering (Default: False)
                if (localSettings.Values.ContainsKey("RamDoubleBuffering"))
                    RamDoubleBufferingSwitch.IsOn = (bool)localSettings.Values["RamDoubleBuffering"];



                // Trigger logic to set IsEnabled states
                UpdateSwitchStates();
    
                // Load Bitrate Setting
                // Default to 0 (Unlimited) as requested
                int currentBitrate = 0;
                if (localSettings.Values.ContainsKey("MaxBitrate"))
                {
                    currentBitrate = (int)localSettings.Values["MaxBitrate"];
                }
    
                foreach (ComboBoxItem item in BitrateBox.Items)
                {
                    if (int.Parse(item.Tag.ToString()) == currentBitrate)
                    {
                        BitrateBox.SelectedItem = item;
                        break;
                    }
                }
                
                // Load Cache Size Setting
                int currentCache = 0; // Default: No Cache
                if (localSettings.Values.ContainsKey("MaxCacheSizeMB"))
                {
                    currentCache = (int)localSettings.Values["MaxCacheSizeMB"];
                }
    
                foreach (ComboBoxItem item in CacheSizeBox.Items)
                {
                    if (int.Parse(item.Tag.ToString()) == currentCache)
                    {
                        CacheSizeBox.SelectedItem = item;
                        _previousCacheItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private ComboBoxItem _previousCacheItem;

        private async void CacheSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (_isLoading) return;

             if (CacheSizeBox.SelectedItem is ComboBoxItem item)
             {
                 int size = int.Parse(item.Tag.ToString());
                 
                 // User set to 0 (No Cache)
                 if (size == 0)
                 {
                     int count = await SubsonicUWP.Services.PlaybackService.Instance.GetCacheFileCount();
                     if (count > 2)
                     {
                         var dialog = new Windows.UI.Popups.MessageDialog("Do you want to delete all cached songs too?", "Clear Cache");
                         dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes"));
                         dialog.Commands.Add(new Windows.UI.Popups.UICommand("No"));
                         
                         var result = await dialog.ShowAsync();
                         var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                         
                         if (result.Label == "No")
                         {
                             // No -> Keep Cache Intact (Manual Mode)
                             // Set limit to 0 but enable ManualMode so CleanCache skips
                             localSettings.Values["MaxCacheSizeMB"] = size;
                             localSettings.Values["ManualCacheMode"] = true;
                             _previousCacheItem = item;
                             return;
                         }
                         else
                         {
                             // Yes -> Clean EVERYTHING (Auto & Manual) but KEEP ACTIVE SONG
                             localSettings.Values["MaxCacheSizeMB"] = size;
                             localSettings.Values["ManualCacheMode"] = true; // Manual Mode going forward

                             // Active Songs Protection Strategy (User Request)
                             // 1. Get Active/Prev/Next Song IDs
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
                             
                             // 2. Ensure Active Song is marked .tmp (Transient)
                             // Actually, EnsureTransient assumes ID. We should ensure transient for ALL kept songs?
                             // Or just Current? Transient means "don't persist in manual mode".
                             // Buffer/Next should also be transient if we are "Clearing Cache" (to Manual Mode 0 MB).
                             foreach (var kid in keep)
                             {
                                 await SubsonicUWP.Services.PlaybackService.Instance.EnsureTransient(kid);
                             }
                             
                             // 3. Delete ALL files EXCEPT kept IDs
                             await SubsonicUWP.Services.PlaybackService.Instance.ClearAllCache(keep);
                         }
                     }
                     else
                     {
                         // Count <= 2, just set it
                         var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                         localSettings.Values["MaxCacheSizeMB"] = size;
                         // If size 0, Manual Mode
                         if (size == 0) localSettings.Values["ManualCacheMode"] = true;
                         else localSettings.Values["ManualCacheMode"] = false;
                     }
                 }
                 else
                 {
                     // Size > 0 or initial load
                     var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                     localSettings.Values["MaxCacheSizeMB"] = size;
                     localSettings.Values["ManualCacheMode"] = false;
                     
                     // Promote transient files to permanent (User request: delete .tmp sidecars when enabling cache)
                     await SubsonicUWP.Services.PlaybackService.Instance.RemoveAllTransientMarkers();
                 }
                 
                 SubsonicService.Instance.InvalidateSettings();
                 _previousCacheItem = item;
             }
        }

        private void BitrateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (_isLoading) return;
             
             if (BitrateBox.SelectedItem is ComboBoxItem item)
             {
                 int bitrate = int.Parse(item.Tag.ToString());
                 var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                 localSettings.Values["MaxBitrate"] = bitrate;
                 SubsonicService.Instance.InvalidateSettings();
             }
        }

        private void MixRandomSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["MixRandomAlbums"] = MixRandomSwitch.IsOn;
        }

        private void UpdateSwitchStates()
        {
             if (SystemBufferingSwitch == null || ForceCachingSwitch == null || AggressiveCachingSwitch == null) return;

             bool sys = SystemBufferingSwitch.IsOn;
             ForceCachingSwitch.IsEnabled = sys;
             AggressiveCachingSwitch.IsEnabled = !sys;
             
             EnsureManualCacheModeState();
        }

        private void EnsureManualCacheModeState()
        {
             // If System Buffering is ON and Force Caching is OFF, we want to treat cache as "Temporary Garbage" (Manual Mode)
             // This ensures .tmp files are cleaned up and we don't accidentally fill the cache with streaming fragments if the user didn't want them kept.
             var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
             
             if (SystemBufferingSwitch.IsOn && !ForceCachingSwitch.IsOn)
             {
                 localSettings.Values["ManualCacheMode"] = true;
             }
             else
             {
                 // Restore based on Cache Size Selection
                 if (CacheSizeBox.SelectedItem is ComboBoxItem item)
                 {
                     int size = int.Parse(item.Tag.ToString());
                     if (size == 0) localSettings.Values["ManualCacheMode"] = true;
                     else localSettings.Values["ManualCacheMode"] = false;
                 }
             }
        }

        private void SystemBufferingSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["SystemBuffering"] = SystemBufferingSwitch.IsOn;
            UpdateSwitchStates();
        }

        private void ForceCachingSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["ForceCaching"] = ForceCachingSwitch.IsOn;
            UpdateSwitchStates(); 
        }

        private void AggressiveCachingSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["AggressiveCaching"] = AggressiveCachingSwitch.IsOn;
        }

        private void RamDoubleBufferingSwitch_Toggled(object sender, RoutedEventArgs e)
        {
             if (_isLoading) return;
             var val = RamDoubleBufferingSwitch.IsOn;
             Windows.Storage.ApplicationData.Current.LocalSettings.Values["RamDoubleBuffering"] = val;
             Services.PlaybackService.Instance.IsRamDoubleBufferingEnabled = val;
        }

        private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (ThemeValidator.SelectedItem is ComboBoxItem item)
             {
                 string theme = item.Tag.ToString();
                 if (theme != ThemeHelper.CurrentTheme)
                 {
                     ThemeHelper.ApplyTheme(theme);
                 }
             }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            StatusBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Yellow);
            StatusBlock.Text = "Testing connection...";

            SubsonicService.Instance.ServerUrl = ServerUrlBox.Text;
            SubsonicService.Instance.Username = UsernameBox.Text;
            SubsonicService.Instance.Password = PasswordBox.Password;

            bool success = await SubsonicService.Instance.PingServer();

            if (success)
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["ServerUrl"] = SubsonicService.Instance.ServerUrl;
                localSettings.Values["Username"] = SubsonicService.Instance.Username;
                localSettings.Values["Password"] = SubsonicService.Instance.Password;

                StatusBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Green);
                StatusBlock.Text = "Connection successful! Settings saved.";
            }
            else
            {
                StatusBlock.Text = "Connection failed. Please check your details.";
            }
        }

        private async void RemovePin_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                await PinnedAlbumManager.UnpinAlbum(item.Id);
                await LoadPinnedAlbums();
            }
        }

        private async System.Threading.Tasks.Task LoadPinnedAlbums()
        {
            var list = await PinnedAlbumManager.GetPinnedAlbums();
            PinnedAlbumsList.ItemsSource = list.ToList();
        }
    }
}
