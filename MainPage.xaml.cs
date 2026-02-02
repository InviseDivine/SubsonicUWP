using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Foundation.Collections; 
using Windows.UI.Xaml.Media.Animation;
using System.Linq;
using System.IO;
using System.Threading.Tasks; // Added
using SubsonicUWP.Services; // Added
using Windows.UI.Xaml.Media.Imaging; // Added for BitmapImage
using System.Collections.Specialized; // Added for CollectionChanged

namespace SubsonicUWP
{
    public enum RepeatMode { Off, All, One }

    public sealed partial class MainPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
#pragma warning disable CS0618 // Type or member is obsolete
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));


        
        private SubsonicItem _currentSong;
        public SubsonicItem CurrentSong
        {
            get => _currentSong;
            set 
            { 
                if (_currentSong != null) _currentSong.IsPlaying = false;
                _currentSong = value; 
                if (_currentSong != null) _currentSong.IsPlaying = true;
                OnPropertyChanged("CurrentSong"); 
                UpdateFavoriteIcon();
                UpdateLocalAlbumArt(); // Trigger Art Update
                UpdateLocalAlbumArt(); // Trigger Art Update
                // REMOVED: TileManager.UpdateTile(_currentSong); 
                // Only update tile when PlayTrack triggers active playback, not on restoration.
            }
        }

        private ImageSource _localAlbumArt;
        public ImageSource LocalAlbumArt
        {
            get => _localAlbumArt;
            set
            {
                _localAlbumArt = value;
                OnPropertyChanged("LocalAlbumArt");
            }
        }

        private async void UpdateLocalAlbumArt()
        {
            if (CurrentSong == null)
            {
                LocalAlbumArt = null;
                return;
            }

            // Capture ID to prevent race conditions during retry loop
            string processingId = CurrentSong.Id;
            string url = CurrentSong.ImageUrl;

            // 1. Try Cache
            try
            {
                var folder = await Windows.Storage.ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", Windows.Storage.CreationCollisionOption.OpenIfExists);
                var file = await folder.TryGetItemAsync($"stream_{processingId}.jpg") as Windows.Storage.StorageFile;
                if (file != null)
                {
                    var bmp = new BitmapImage();
                    using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
                    {
                        await bmp.SetSourceAsync(stream);
                    }
                    if (CurrentSong?.Id == processingId) LocalAlbumArt = bmp;
                    return;
                }
            }
            catch { }

            // 2. Fallback: Download with Retry
            if (!string.IsNullOrEmpty(url))
            {
                // Reset to placeholder/null while loading
                if (CurrentSong?.Id == processingId) LocalAlbumArt = null;

                int retry = 0;
                while (retry < 3)
                {
                    try
                    {
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            var bytes = await client.GetByteArrayAsync(new Uri(url));
                            if (bytes.Length > 0)
                            {
                                // A. Cache It (Fire and forget)
                                _ = Task.Run(async () => 
                                {
                                    try 
                                    {
                                        var folder = await Windows.Storage.ApplicationData.Current.TemporaryFolder.CreateFolderAsync("Cache", Windows.Storage.CreationCollisionOption.OpenIfExists);
                                        var file = await folder.CreateFileAsync($"stream_{processingId}.jpg", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                                        await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
                                    } 
                                    catch {}
                                });

                                // B. Display It
                                if (CurrentSong?.Id == processingId)
                                {
                                    var bmp = new BitmapImage();
                                    using (var ms = new System.IO.MemoryStream(bytes))
                                    {
                                        await bmp.SetSourceAsync(ms.AsRandomAccessStream());
                                    }
                                    LocalAlbumArt = bmp;
                                }
                                return; // Success
                            }
                        }
                    }
                    catch 
                    {
                        retry++;
                        if (retry < 3) await Task.Delay(500 * retry); // Backoff 0.5s, 1s
                    }
                }
            }
            
            // Final Fallback if everything failed: null (Placeholder)
            if (CurrentSong?.Id == processingId) LocalAlbumArt = null;
        }

        public System.Collections.ObjectModel.ObservableCollection<NavItem> NavItems { get; set; }

        private double _currentPosition;
        
        public double CurrentPosition
        {
             get => _currentPosition;
             set 
             { 
                 double oldValue = _currentPosition;
                 _currentPosition = value;
                 OnPropertyChanged("CurrentPosition");
                 OnPropertyChanged("CurrentPositionFormatted");

                 if (!_isDragging && !_isUpdatingFromTimer && Math.Abs(oldValue - value) > 1)
                 {
                     if (Services.PlaybackService.Instance.Player.PlaybackSession.CanSeek)
                        Services.PlaybackService.Instance.Player.PlaybackSession.Position = TimeSpan.FromSeconds(value);
                 }
             }
        }
        public string CurrentPositionFormatted 
        {
            get
            {
                if (double.IsNaN(_currentPosition) || double.IsInfinity(_currentPosition) || _currentPosition < 0 || _currentPosition > TimeSpan.MaxValue.TotalSeconds)
                {
                    return "--:--";
                }
                return TimeSpan.FromSeconds(_currentPosition).ToString(@"mm\:ss");
            }
        }

        public void Seek(double seconds)
        {
            if (Services.PlaybackService.Instance.Player.PlaybackSession.CanSeek)
            {
                Services.PlaybackService.Instance.Player.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
                UpdateSmtcTimeline(); 
            }
            // Ensure CurrentPosition matches, though visual update surely happened via Drag
            _currentPosition = seconds;
            OnPropertyChanged("CurrentPosition");
            OnPropertyChanged("CurrentPositionFormatted");
        }

        private bool _isDragging = false;
        public bool IsDragging => _isDragging; // Expose read-only for external checks

        public void SetDragging(bool dragging)
        {
             _isDragging = dragging;
        }

        private bool _isUpdatingFromTimer = false;

        private double _duration;
        public double Duration
        {
             get => _duration;
             set 
             { 
                 _duration = value; 
                 OnPropertyChanged("Duration"); 
                 OnPropertyChanged("DurationFormatted");
             }
        }
        public string DurationFormatted 
        {
            get
            {
                if (double.IsNaN(_duration) || double.IsInfinity(_duration) || _duration < 0 || _duration > TimeSpan.MaxValue.TotalSeconds)
                {
                    return "--:--"; 
                }
                return TimeSpan.FromSeconds(_duration).ToString(@"mm\:ss");
            }
        }

        private DispatcherTimer _timer;
        private DispatcherTimer _retryTimer;

        public MainPage()
        {
            this.InitializeComponent();
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;

            // Enable Tile Queue
            try { Windows.UI.Notifications.TileUpdateManager.CreateTileUpdaterForApplication().EnableNotificationQueue(true); } catch { }

            // Initialize deduplication set
            _queueLoadedIds = new System.Collections.Generic.HashSet<string>();

            PlaybackQueue = new IncrementalLoadingCollection<SubsonicItem>(async (count) =>
            {
                if (QueueName == "Random Songs")
                {
                     var result = await SubsonicService.Instance.GetRandomSongs(50);
                     // Deduplicate
                     var newItems = new System.Collections.Generic.List<SubsonicItem>();
                     // Ensure set handles initialization
                     if (_queueLoadedIds == null) _queueLoadedIds = new System.Collections.Generic.HashSet<string>();
                     
                     foreach (var item in result)
                     {
                         if (!_queueLoadedIds.Contains(item.Id))
                         {
                             _queueLoadedIds.Add(item.Id);
                             newItems.Add(item);
                         }
                     }
                     return newItems;
                }
                else if (QueueName == "Recently Added")
                {
                     _recentAlbumsOffset += 6;
                     return await SubsonicService.Instance.GetRecentSongs(_recentAlbumsOffset, 6);
                }
                return new System.Collections.Generic.List<SubsonicItem>();
            });
            
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Subscribe to Preload Event
            Services.PlaybackService.Instance.DownloadCompleted += OnDownloadCompleted;
            
            // Initialize RAM Double Buffering Setting
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey("RamDoubleBuffering"))
            {
                Services.PlaybackService.Instance.IsRamDoubleBufferingEnabled = (bool)settings["RamDoubleBuffering"];
            }

            InitSMTC();

            // Subscribe to Service Events
            Services.PlaybackService.Instance.AddToQueueRequested += (s, item) => 
            {
               _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => AddToQueue(item));
            };
            Services.PlaybackService.Instance.PlayNextRequested += (s, item) =>
            {
               _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => 
               {
                   if (item == null) return;
                   if (PlaybackQueue.Count == 0) 
                   {
                       AddToQueue(item);
                       return;
                   }
                   
                   int targetIndex = CurrentQueueIndex + 1;
                   if (targetIndex > PlaybackQueue.Count) targetIndex = PlaybackQueue.Count;
                   
                   PlaybackQueue.Insert(targetIndex, item);
                   // Retrieve deduplication set check if needed?
                   if (_queueLoadedIds != null) _queueLoadedIds.Add(item.Id);
                   
                   // Retrigger Preload since the "Next" song just changed
                   AttemptPreloadNext(CurrentSong?.Id);
                   
                   // Toast or visual feedback?
               });
            };
            Services.PlaybackService.Instance.PlayTracksRequested += (s, args) =>
            {
               _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => 
               {
                   if (args.Item1 == null || args.Item1.Count == 0) return;
                   var startItem = (args.Item2 >= 0 && args.Item2 < args.Item1.Count) ? args.Item1[args.Item2] : args.Item1.First();
                   PlayTrackList(args.Item1, startItem, "Now Playing");
               });
            };

            // this.Loaded += (s, e) => {
            //     GlobalMediaElement.MediaOpened += GlobalMediaElement_MediaOpened;
            // };

            // Defaults
            // Defaults
            // if (VolumeSlider != null) VolumeSlider.Value = 0.5; // Removed to respect saved state

            NavItems = new System.Collections.ObjectModel.ObservableCollection<NavItem>
            {
                new NavItem { Label = "Home", Symbol = "\uE80F", Tag = "Home", DestPage = typeof(HomePage) },
                new NavItem { Label = "Favorites", Symbol = "\uE734", Tag = "Favorites", DestPage = typeof(FavoritesPage) },
                new NavItem { Label = "Playlists", Symbol = "\uE142", Tag = "Playlists", DestPage = typeof(PlaylistsPage) },
                new NavItem { Label = "Queues", Symbol = "\uE81C", Tag = "Sessions", DestPage = typeof(SessionsPage) },
                new NavItem { Label = "Artists", Symbol = "\uE77B", Tag = "Artists", DestPage = typeof(ArtistsPage) },
                new NavItem { Label = "Albums", Symbol = "\uE93C", Tag = "Albums", DestPage = typeof(AlbumsPage) },
                new NavItem { Label = "Offline / Cache", Symbol = "\uE896", Tag = "Cache", DestPage = typeof(CacheManagerPage) },
                new NavItem { Label = "Settings", Symbol = "\uE713", Tag = "Settings", DestPage = typeof(SettingsPage) },
            };
            this.DataContext = this;
            
            // Removed NavList.SelectedIndex = 0; // Moved to Loaded to avoid ArgumentException
            ContentFrame.Navigate(typeof(HomePage));
            ContentFrame.Navigate(typeof(HomePage));
            this.Loaded += MainPage_Loaded;
            
            // Handle Back Button
            Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += MainPage_BackRequested;
            Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
            ContentFrame.Navigated += ContentFrame_Navigated;
        }

        private void CoreWindow_PointerPressed(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.PointerEventArgs args)
        {
             // Handle Mouse Back Button (XButton1)
             if (args.CurrentPoint.Properties.IsXButton1Pressed)
             {
                 if (ContentFrame.CanGoBack)
                 {
                     ContentFrame.GoBack();
                     args.Handled = true;
                 }
             }
        }

        private void MainPage_BackRequested(object sender, Windows.UI.Core.BackRequestedEventArgs e)
        {
            if (ContentFrame.CanGoBack)
            {
                e.Handled = true;
                ContentFrame.GoBack();
            }
        }

        private void ContentFrame_Navigated(object sender, Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
             // Update UI Back Button visibility if we had one in title bar, 
             // but SystemNavigationManager handles the PC/Phone back button automatically.
             // We can also opt to show the back button in the titlebar for Desktop:
             var navManager = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
             if (ContentFrame.CanGoBack)
             {
                 navManager.AppViewBackButtonVisibility = Windows.UI.Core.AppViewBackButtonVisibility.Visible;
             }
             else
             {
                 navManager.AppViewBackButtonVisibility = Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;
             }

            // Sync NavList selection if possible
            if (e.SourcePageType == typeof(HomePage)) NavList.SelectedIndex = 0;
            else if (e.SourcePageType == typeof(FavoritesPage)) NavList.SelectedIndex = 1;
            else if (e.SourcePageType == typeof(PlaylistsPage)) NavList.SelectedIndex = 2;
            else if (e.SourcePageType == typeof(SessionsPage)) NavList.SelectedIndex = 3;
            else if (e.SourcePageType == typeof(ArtistsPage)) NavList.SelectedIndex = 4;
            else if (e.SourcePageType == typeof(AlbumsPage)) NavList.SelectedIndex = 5;

            // Hide Player Bar on Phone Now Playing Screen
            if (e.SourcePageType == typeof(NowPlayingPhonePage))
            {
                 PlayerBarGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                 PlayerBarGrid.Visibility = Visibility.Visible;
            }
        }


        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            await RestoreState();
        }
        
        private bool _isRestoring = false;
        private async System.Threading.Tasks.Task RestoreState()
        {
            _isRestoring = true;
            try 
            {
                var session = await SessionManager.LoadCurrentState();
                if (session != null && session.Tracks != null && session.Tracks.Count > 0)
                {
                    PlaybackQueue.Clear();
                    foreach (var t in session.Tracks) PlaybackQueue.Add(t);
                    
                    // Restore Shuffle/Repeat
                    IsShuffle = session.IsShuffle;
                    CurrentRepeatMode = (RepeatMode)session.RepeatMode;
                    UpdateShuffleIcon();
                    UpdateRepeatIcon();

                    if (session.CurrentIndex >= 0 && session.CurrentIndex < PlaybackQueue.Count)
                    {
                        CurrentQueueIndex = session.CurrentIndex;
                        CurrentSong = PlaybackQueue[CurrentQueueIndex];
                        
                        // Robust Load (Cache-aware)
                        await LoadSong(CurrentSong, false);
                        
                        // Robust Load (Cache-aware)
                        await LoadSong(CurrentSong, false);
                        
                        var player = Services.PlaybackService.Instance.Player;
                        if (player.PlaybackSession.CanSeek) player.PlaybackSession.Position = session.Position;
                        // Paused by default
                        // Paused by default
                        // Paused by default
                    }
                
                QueueName = session.Name;
                _currentQueueIndex = session.CurrentIndex; // Use backing field
                _currentSessionId = session.Id;
                OnPropertyChanged("CurrentQueueIndex");
                
                if (_currentQueueIndex >= 0 && _currentQueueIndex < PlaybackQueue.Count)
                {
                    // Load but don't play
                    var song = PlaybackQueue[_currentQueueIndex];
                    CurrentSong = song;
                    var uri = new Uri(song.StreamUrl);
                    
                    Services.PlaybackService.Instance.Player.AutoPlay = false; // Ensure we don't auto-start on restore
                    if (session.Position.TotalSeconds > 0)
                    {
                        var p = Services.PlaybackService.Instance.Player;
                        if (p.PlaybackSession.CanSeek) p.PlaybackSession.Position = session.Position;
                    }

                    // Force SMTC Active
                    if (_smtc != null)
                    {
                        _smtc.IsEnabled = true;
                        _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                        UpdateSmtcMetadata(CurrentSong);
                        _smtc.DisplayUpdater.Update();
                    }
                    
                    // If we just played, icon is Pause. If we didn't play (just loaded), icon is Play.
                    if (session.Position.TotalSeconds > 0) 
                    {
                        // Ensure it's not set to Pause icon
                        PlayPauseIcon.Symbol = Symbol.Play; // Play Icon (because paused)
                    }

                    // Repair Metadata if missing (Album Name Bug)
                    if (string.IsNullOrEmpty(CurrentSong.Album) || string.IsNullOrEmpty(CurrentSong.ArtistId))
                    {
                        try
                        {
                            var full = await SubsonicService.Instance.GetSong(CurrentSong.Id);
                            if (full != null)
                            {
                                // Clone to trigger PropertyChanged on reassignment
                                var fixedSong = CurrentSong.Clone();
                                if (string.IsNullOrEmpty(fixedSong.Album)) fixedSong.Album = full.Album;
                                if (string.IsNullOrEmpty(fixedSong.AlbumId)) fixedSong.AlbumId = full.AlbumId;
                                if (string.IsNullOrEmpty(fixedSong.ArtistId)) fixedSong.ArtistId = full.ArtistId;
                                
                                CurrentSong = fixedSong; // This triggers MainPage.PropertyChanged("CurrentSong")
                            }
                        }
                        catch {}
                    }
                    UpdateSmtcMetadata(CurrentSong);
                }
                
                // Infinite Random Restore Check
                if (QueueName == "Random Songs" && (PlaybackQueue.Count == 0 || _currentQueueIndex >= PlaybackQueue.Count - 1))
                {
                     await PlaybackQueue.LoadMoreItemsAsync(50);
                     // If we appended, start playing? No, respect paused state but ensure content is ready.
                     if (CurrentSong == null && PlaybackQueue.Count > 0)
                     {
                         CurrentQueueIndex = 0;
                     }
                }
            }
            }
            finally
            {
                _isRestoring = false;
            }

            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.ContainsKey("Volume"))
            {
                object vol = Windows.Storage.ApplicationData.Current.LocalSettings.Values["Volume"];
                Services.PlaybackService.Instance.Player.Volume = (double)vol;
                if (VolumeSlider != null) VolumeSlider.Value = (double)vol;
                OnPropertyChanged("VolumeDisplay");
            }
        }

        private bool _isBuffering;
        public bool IsBuffering
        {
            get { return _isBuffering; }
            set { _isBuffering = value; OnPropertyChanged("IsBuffering"); }
        }



        private void NavList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is NavItem item)
            {
                if (item.DestPage != null)
                {
                     // Do NOT clear BackStack if we want history
                     // But typically top-level nav clears history.
                     // The user wants "Back" to work.
                     // If I go Home -> Random -> Back, I expect Home.
                     // If I go Home -> Favorites -> Back, do I expect Home?
                     // Standard UWP Nav Pane: usually clears stack.
                     // BUT user said "previous screen".
                     // Let's TRY keeping the stack for now, or at least NOT clearing it blindly.
                     // Actually, standard behavior is: Top level items clear stack. Sub-pages push.
                     // User said: "Back button... get me to the previous screen".
                     // If they click "Random Songs" (which is likely a sub-page of something or a top level?), 
                     // let's assume they want browser-style history.
                     ContentFrame.Navigate(item.DestPage);
                     // ContentFrame.BackStack.Clear(); // Commented out to allow history between tabs
                    
                    // Close pane on mobile selection
                    if (RootSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
                    {
                        RootSplitView.IsPaneOpen = false;
                    }
                }
            }
        }
        
        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;
        }

        private void MobileRestoreButton_Click(object sender, RoutedEventArgs e)
        {
             ContentFrame.Navigate(typeof(SessionsPage));
        }

    

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
             if (!string.IsNullOrEmpty(args.QueryText))
             {
                 ContentFrame.Navigate(typeof(SearchPage), args.QueryText);
             }
        }

        private async void PlayerArtist_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
             // Mobile check: If narrow, let it bubble to PlayerBar (opens Now Playing)
             if (Window.Current.Bounds.Width < 720) 
             {
                 return; 
             }

             e.Handled = true; // Prevent bubbling to PlayerBar listener
             
             if (CurrentSong == null) return;

             string artistId = CurrentSong.ArtistId;

             if (string.IsNullOrEmpty(artistId))
             {
                 // Attempt repair
                 var fullSong = await SubsonicService.Instance.GetSong(CurrentSong.Id);
                 if (fullSong != null && !string.IsNullOrEmpty(fullSong.ArtistId))
                 {
                     CurrentSong.ArtistId = fullSong.ArtistId;
                     CurrentSong.AlbumId = fullSong.AlbumId; // Update album too while we're at it
                     artistId = fullSong.ArtistId;
                 }
             }

             if (!string.IsNullOrEmpty(artistId))
             {
                 ContentFrame.Navigate(typeof(ArtistDetailsPage), artistId);
             }
             else
             {
                 // Feedback for stale data
                 var dialog = new Windows.UI.Popups.MessageDialog("Artist info unavailable. Please play a new song to refresh data.");
                 await dialog.ShowAsync();
             }
        }

        private async void GoToAlbum_Click(object sender, RoutedEventArgs e)
        {
             if (CurrentSong == null) return;
             
             string albumId = CurrentSong.AlbumId;

             if (string.IsNullOrEmpty(albumId))
             {
                 // Attempt repair
                 var fullSong = await SubsonicService.Instance.GetSong(CurrentSong.Id);
                 if (fullSong != null && !string.IsNullOrEmpty(fullSong.AlbumId))
                 {
                     CurrentSong.ArtistId = fullSong.ArtistId; // Update artist too
                     CurrentSong.AlbumId = fullSong.AlbumId;
                     albumId = fullSong.AlbumId;
                 }
             }

             if (!string.IsNullOrEmpty(albumId))
             {
                 ContentFrame.Navigate(typeof(AlbumDetailsPage), albumId);
             }
             else
             {
                 var dialog = new Windows.UI.Popups.MessageDialog("Album info unavailable. Please play a new song to refresh data.");
                 await dialog.ShowAsync();
             }
        }


        private void PlayerBar_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
             if (RootSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
             {
                 ContentFrame.Navigate(typeof(NowPlayingPhonePage), CurrentSong, new SlideNavigationTransitionInfo());
             }
             else
             {
                 ContentFrame.Navigate(typeof(NowPlayingPage), CurrentSong);
             }
        }

        private void PlayerBar_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
             if (e.Velocities.Linear.Y < -0.5) // Swipe Up
             {
                 if (RootSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
                 {
                     ContentFrame.Navigate(typeof(NowPlayingPhonePage), CurrentSong, new SlideNavigationTransitionInfo());
                 }
                 else
                 {
                     ContentFrame.Navigate(typeof(NowPlayingPage), CurrentSong);
                 }
             }
        }

        private void Queue_Click(object sender, RoutedEventArgs e)
        {
             ContentFrame.Navigate(typeof(QueuePage));
        }



        // Helper to update icon when track changes
        private void OnTrackChanged()
        {
             // Existing logic...
             // Existing logic...
             UpdateFavoriteIcon();
             TileManager.UpdateTile(CurrentSong);
        }

        private void Control_Click(object sender, RoutedEventArgs e)
        {
             if (sender == ShuffleButton) ToggleShuffle();
             else if (sender == RepeatButton) ToggleRepeat();
        }

        // Playback Queue Logic
        private IncrementalLoadingCollection<SubsonicItem> _playbackQueue;
        public IncrementalLoadingCollection<SubsonicItem> PlaybackQueue 
        { 
            get => _playbackQueue;
            set
            {
                if (_playbackQueue != value)
                {
                    if (_playbackQueue != null) _playbackQueue.CollectionChanged -= OnQueueChanged;
                    _playbackQueue = value;
                    if (_playbackQueue != null) _playbackQueue.CollectionChanged += OnQueueChanged;
                }
            }
        }
        
        private void OnQueueChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
             // If queue changes (reorder, add next, etc), check if we need to preload the new "Next" song.
             // Optimize: Only if change affects the immediate next slot?
             // Since AttemptPreloadNext is robust (checks CurrentSong), just calling it is safe.
             // We pass CurrentSong?.Id to satisfy the "finishedSongId" check (simulating "Just finished/checked current song, what's next?")
             AttemptPreloadNext(CurrentSong?.Id);
        }
        
        private string _queueName = "Now Playing";
        public string QueueName
        {
            get => _queueName;
            set 
            { 
                 if (_queueName != value)
                 {
                     _queueName = value; 
                     OnPropertyChanged("QueueName");
                     SaveState();
                 }
            }
        }

        private int _currentQueueIndex = -1;
        public int CurrentQueueIndex
        {
            get => _currentQueueIndex;
            set 
            { 
                if (_currentQueueIndex != value)
                {
                    _currentQueueIndex = value; 
                    OnPropertyChanged("CurrentQueueIndex");
                    // Removed PlayTrack call here to prevent auto-play on reorder/index shift.
                    // Playback should only start on explicit actions (Click, Next, Prev, etc.)
                    SaveState();
                }
            }
        }

        private int _recentAlbumsOffset = 0; // Recent Songs offset


        
        // Append methods removed as they are handled by IncrementalLoadingCollection lambda

        public async void PlayTrackList(System.Collections.Generic.IEnumerable<SubsonicItem> tracks, SubsonicItem startItem, string queueName = "Now Playing", Guid? sessionId = null)
        {
            // Archive current queue if valuable
            if (PlaybackQueue.Count > 0)
            {
                // Archive current session
                await SessionManager.ArchiveSession(PlaybackQueue, QueueName, _currentSessionId, CurrentQueueIndex, Services.PlaybackService.Instance.Player.PlaybackSession.Position, IsShuffle, (int)CurrentRepeatMode);
            }

            if (string.IsNullOrEmpty(queueName)) queueName = "Now Playing";
            
            // Should we reuse ID?
            if (sessionId.HasValue)
            {
                _currentSessionId = sessionId.Value;
            }
            else if (QueueName != queueName || _currentSessionId == Guid.Empty)
            {
                 // New context, new ID
                 _currentSessionId = Guid.NewGuid(); 
            }
            
            QueueName = queueName;
            
            // Reset Infinite Scroll State
            PlaybackQueue.Clear();
            PlaybackQueue.Reset(); // Re-enable loading
            
            int index = 0;
            // Use backing field to avoid triggering PlayTrack on reset
            _currentQueueIndex = -1;
            OnPropertyChanged("CurrentQueueIndex");
            
            // Reset offsets
            _recentAlbumsOffset = 0;
            if (QueueName == "Recently Added") _recentAlbumsOffset = 6; 
            
            foreach (var t in tracks) 
            {
                PlaybackQueue.Add(t);
                if (t == startItem) CurrentQueueIndex = index; 
                index++;
            }

            if (CurrentQueueIndex == -1 && PlaybackQueue.Count > 0) CurrentQueueIndex = 0; 
            
            // Explicitly play start item since setter no longer triggers PlayTrack (to support reordering)
            if (CurrentQueueIndex >= 0 && CurrentQueueIndex < PlaybackQueue.Count)
            {
               PlayTrack(PlaybackQueue[CurrentQueueIndex]);
            }
            
            // Rebuild Deduplication Set
            if (_queueLoadedIds == null) _queueLoadedIds = new System.Collections.Generic.HashSet<string>();
            _queueLoadedIds.Clear();
            foreach (var t in PlaybackQueue) _queueLoadedIds.Add(t.Id);
            
            SaveState();
        }

        public void RestoreSessionSettings(SavedSession session)
        {
             if (session == null) return;
             IsShuffle = session.IsShuffle;
             CurrentRepeatMode = (RepeatMode)session.RepeatMode;
             UpdateShuffleIcon();
             UpdateRepeatIcon();
        }

        private Guid _currentSessionId = Guid.NewGuid();
        private System.Collections.Generic.HashSet<string> _queueLoadedIds;

        private async void SaveState()
        {
            if (_isRestoring) return;
            try
            {
                // Auto-save current queue state
                SessionManager.SaveCurrentState(PlaybackQueue, QueueName, CurrentQueueIndex, _currentSessionId, Services.PlaybackService.Instance.Player.PlaybackSession.Position, IsShuffle, (int)CurrentRepeatMode);
            }
            catch { }
        }




        public void ClearQueue()
        {
            Services.PlaybackService.Instance.Player.Source = null; // Force Stop
            PlaybackQueue.Clear();
            CurrentQueueIndex = -1;
            CurrentSong = null;
            QueueName = "Now Playing"; // Reset name
            if (_queueLoadedIds != null) _queueLoadedIds.Clear();
            
            PlayPauseIcon.Symbol = Symbol.Play;
            
            // Trigger Idle Tile with random albums (Refresh cache first)
            var _ = System.Threading.Tasks.Task.Run(async () => 
            {
                try
                {
                    await TileManager.PrepareIdleCache();
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        TileManager.UpdateFromCache();
                    });
                }
                catch { }
            });

            // Update SMTC to cleared state
             if (_smtc != null)
             {
                 _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                 _smtc.DisplayUpdater.ClearAll();
                 _smtc.DisplayUpdater.Update();
                 _smtc.IsEnabled = false;
             }
             
            SaveState();
            // Navigate to Home
            ContentFrame.Navigate(typeof(HomePage));
        }

        public void RemoveFromQueue(SubsonicItem item)
        {
            if (item == null) return;
            int index = PlaybackQueue.IndexOf(item);
            if (index == -1) return;

            // If removing current song
            if (index == CurrentQueueIndex)
            {
                PlaybackQueue.RemoveAt(index);
                if (PlaybackQueue.Count == 0)
                {
                    ClearQueue();
                }
                else if (index < PlaybackQueue.Count)
                {
                    // Play next (index stays same)
                    // But we must force play because index didn't change so PropertyChanged won't fire?
                    // Setter checks `if (_currentQueueIndex != value)`.
                    // So just calling `PlayTrack` manually is safest.
                    if (CurrentQueueIndex >= 0 && CurrentQueueIndex < PlaybackQueue.Count)
                    {
                       PlayTrack(PlaybackQueue[CurrentQueueIndex]);
                    }
                }
                else
                {
                    // Was last song
                    CurrentQueueIndex = -1;
                    Services.PlaybackService.Instance.Player.Pause();
                }
            }
            else
            {
                PlaybackQueue.RemoveAt(index);
                if (index < CurrentQueueIndex)
                {
                    // Decrement index without triggering play
                    _currentQueueIndex--;
                    OnPropertyChanged("CurrentQueueIndex");
                }
            }
            SaveState();
        }


        


        private void UpdateVolumeIcon(double volume)
        {
             if (volume == 0) VolumeIcon.Text = "\uE74F"; // Mute
             else if (volume < 0.33) VolumeIcon.Text = "\uE993"; // Low
             else if (volume < 0.66) VolumeIcon.Text = "\uE994"; // Med
             else VolumeIcon.Text = "\uE767"; // High
        }

        private double _preMuteVolume = 0.5;
        private void VolumeIcon_Tapped(object sender, TappedRoutedEventArgs e)
        {
             if (Services.PlaybackService.Instance.Player.Volume > 0)
             {
                 _preMuteVolume = Services.PlaybackService.Instance.Player.Volume;
                 VolumeSlider.Value = 0; // Triggers ValueChanged
             }
             else
             {
                 VolumeSlider.Value = _preMuteVolume > 0 ? _preMuteVolume : 0.5;
             }
        }

        private void AddToPlaylist_Click_Player(object sender, RoutedEventArgs e)
        {
             if (CurrentSong != null) ShowPlaylistPicker(CurrentSong);
        }

        private async void Download_Click_Player(object sender, RoutedEventArgs e)
        {
             if (CurrentSong != null) await DownloadManager.StartDownload(CurrentSong);
        }

        public void AddToQueue(SubsonicItem item)
        {
            if (item == null) return;
            PlaybackQueue.Add(item);
            if (PlaybackQueue.Count == 1)
            {
                CurrentQueueIndex = 0;
                // Use the SMTC-aware PlayTrack (defined lower down)
                PlayTrack(item); 
            }
            SaveState();
        }

        public void PlayNext(SubsonicItem item)
        {
            if (item == null) return;
            if (PlaybackQueue.Count == 0 || PlaybackQueue.IndexOf(item) == -1 && PlaybackQueue.Count == 0)
            {
                AddToQueue(item);
                return;
            }

            int oldIndex = PlaybackQueue.IndexOf(item);
            int targetIndex = CurrentQueueIndex + 1;
            if (targetIndex > PlaybackQueue.Count) targetIndex = PlaybackQueue.Count;

            if (oldIndex != -1)
            {
                // If it is the current song, Duplicate (Insert Copy) instead of Move
                if (item == CurrentSong)
                {
                    PlaybackQueue.Insert(targetIndex, item.Clone());
                    SaveState();
                    return;
                }

                // Move via Remove+Insert to trigger "Add" animation (Pop-in) matches Duplicate behavior
                if (oldIndex == targetIndex) return;

                PlaybackQueue.RemoveAt(oldIndex);

                // Recalculate target to be "After Current Song" to ensure correctness after index shift
                // and to trigger the Add animation that the user requested.
                if (CurrentSong != null)
                {
                    int currentIdx = PlaybackQueue.IndexOf(CurrentSong);
                    if (currentIdx != -1)
                    {
                        // Update CurrentQueueIndex if it shifted
                        if (CurrentQueueIndex != currentIdx)
                        {
                            _currentQueueIndex = currentIdx;
                            OnPropertyChanged("CurrentQueueIndex");
                        }
                        targetIndex = currentIdx + 1;
                    }
                }
                
                // Clamp
                if (targetIndex > PlaybackQueue.Count) targetIndex = PlaybackQueue.Count;
                
                PlaybackQueue.Insert(targetIndex, item);
            }
            else
            {
                // Insert
                PlaybackQueue.Insert(targetIndex, item);
            }
            
            // Repair Index logic...
            if (CurrentSong != null)
            {
                 int newCurrent = PlaybackQueue.IndexOf(CurrentSong);
                 if (newCurrent != -1 && newCurrent != CurrentQueueIndex)
                 {
                     _currentQueueIndex = newCurrent; // Update backing field to avoid re-triggering setter logic
                     OnPropertyChanged("CurrentQueueIndex");
                 }
            }
            
            SaveState();
        }

        public void AddToQueue(System.Collections.Generic.IEnumerable<SubsonicItem> items)
        {
            if (items == null) return;
            foreach (var item in items) PlaybackQueue.Add(item);
            if (PlaybackQueue.Count == items.Count()) // Was empty
            {
                CurrentQueueIndex = 0;
                PlayTrack(PlaybackQueue[0]);
            }
            SaveState();
        }

        public void PlayNext(System.Collections.Generic.IEnumerable<SubsonicItem> items)
        {
            if (items == null || !items.Any()) return;
            
            int targetIndex = CurrentQueueIndex + 1;
            if (PlaybackQueue.Count == 0) targetIndex = 0; 
            
            // Clamp
            if (targetIndex > PlaybackQueue.Count) targetIndex = PlaybackQueue.Count;

            foreach (var item in items)
            {
                PlaybackQueue.Insert(targetIndex, item);
                targetIndex++;
            }
            
            if (PlaybackQueue.Count == items.Count()) // Was empty
            {
                 CurrentQueueIndex = 0;
                 PlayTrack(PlaybackQueue[0]);
            }
            SaveState();
        }

        public async void ShowPlaylistPicker(SubsonicItem songToAdd)
        {
            if (songToAdd == null) return;

            var playlists = await SubsonicService.Instance.GetPlaylists();
            var listView = new ListView 
            { 
                ItemsSource = playlists,
                DisplayMemberPath = "Title",
                Height = 300,
                SelectionMode = ListViewSelectionMode.Single
            };

            var dialog = new ContentDialog
            {
                Title = "Select Playlist",
                Content = listView,
                PrimaryButtonText = "Add",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (listView.SelectedItem is SubsonicItem playlist)
                {
                    bool success = await SubsonicService.Instance.AddToPlaylist(playlist.Id, songToAdd.Id);
                    if (!success)
                    {
                        var errDialog = new Windows.UI.Popups.MessageDialog("Failed to add to playlist.");
                        await errDialog.ShowAsync();
                    }
                }
            }
        }

        // Shuffle & Repeat
        public bool IsShuffle { get; set; } = false;
        public RepeatMode CurrentRepeatMode { get; set; } = RepeatMode.Off;

        private System.Collections.Generic.List<SubsonicItem> _originalQueueSnapshot;

        public async void ToggleShuffle()
        {
            IsShuffle = !IsShuffle;
            
            if (IsShuffle)
            {
                // ENABLE SHUFFLE: Create Shuffled Subqueue
                if (PlaybackQueue != null)
                {
                    // 1. Snapshot original
                    _originalQueueSnapshot = new System.Collections.Generic.List<SubsonicItem>(PlaybackQueue);
                    
                    // 2. Create new shuffled list
                    var shuffledList = new System.Collections.Generic.List<SubsonicItem>();
                    
                    if (CurrentSong != null)
                    {
                        shuffledList.Add(CurrentSong);
                    }
                    
                    if (QueueName == "Random Songs" || QueueName == "Recently Added")
                    {
                         // Special Mode: Current + 50 Random
                         var randomSongs = await SubsonicService.Instance.GetRandomSongs(50);
                         foreach(var s in randomSongs)
                         {
                             // Avoid duplicate if current song is accidentally in random list OR if duplicate in batch
                             if (s.Id != CurrentSong?.Id && !shuffledList.Any(existing => existing.Id == s.Id))
                             {
                                 shuffledList.Add(s);
                             }
                         }
                    }
                    else
                    {
                        // Normal Mode: Current + Shuffle Rest
                        var remainder = new System.Collections.Generic.List<SubsonicItem>();
                        foreach(var item in _originalQueueSnapshot)
                        {
                            if (item.Id != CurrentSong?.Id) remainder.Add(item);
                        }
                        
                        // Shuffle remainder
                        var rnd = new Random();
                        int n = remainder.Count;
                        while (n > 1) 
                        {
                            n--;
                            int k = rnd.Next(n + 1);
                            var value = remainder[k];
                            remainder[k] = remainder[n];
                            remainder[n] = value;
                        }
                        
                        shuffledList.AddRange(remainder);
                    }
                    
                    // 3. Apply to PlaybackQueue
                    PlaybackQueue.Clear();
                    foreach(var s in shuffledList) PlaybackQueue.Add(s);
                    
                    // 4. Update Index (Current song is always 0)
                    CurrentQueueIndex = 0;
                    _currentQueueIndex = 0; // Force update backing field without triggering setter logic if any
                    OnPropertyChanged("CurrentQueueIndex");
                }
            }
            else
            {
                // DISABLE SHUFFLE: Restore Original
                if (_originalQueueSnapshot != null && _originalQueueSnapshot.Count > 0)
                {
                    // 1. Restore
                    PlaybackQueue.Clear();
                    foreach(var s in _originalQueueSnapshot) PlaybackQueue.Add(s);
                    
                    // 2. Find current song in original
                    if (CurrentSong != null)
                    {
                        int originalIndex = -1;
                        for(int i=0; i<PlaybackQueue.Count; i++)
                        {
                            if (PlaybackQueue[i].Id == CurrentSong.Id)
                            {
                                originalIndex = i;
                                break;
                            }
                        }
                        
                        if (originalIndex != -1)
                        {
                            CurrentQueueIndex = originalIndex;
                        }
                        else
                        {
                             // Song not in original (e.g. was loaded dynamically in random mode)
                             // Just append it? Or reset to 0?
                             // User said "switch back to original master queue and delete temp". 
                             // If current song isn't in master, we probably stop or just play from index 0?
                             // Let's assume we keep playing current song if possible, or just go to 0.
                             // Actually, if we are playing a Random Song that wasn't in original, we can't "keep context".
                             // But let's verify if `QueueName` logic implies `_originalQueueState` is relevant.
                        }
                    }
                    
                    _originalQueueSnapshot = null;
                }
            }

            UpdateShuffleIcon();
        }

        public void ToggleRepeat()
        {
            if (CurrentRepeatMode == RepeatMode.Off) CurrentRepeatMode = RepeatMode.All;
            else if (CurrentRepeatMode == RepeatMode.All) CurrentRepeatMode = RepeatMode.One;
            else CurrentRepeatMode = RepeatMode.Off;
            
            UpdateRepeatIcon();
        }

        private void UpdateShuffleIcon()
        {
             // Safely access elements. Content is now a Grid.
             // We can access x:Name directly if generated, or traverse.
             // Since x:Name="ShuffleIcon" is in MainPage.xaml, it should be accessible.
             
             if (IsShuffle) 
             {
                 ShuffleButton.Foreground = new SolidColorBrush(Windows.UI.Colors.OrangeRed); 
                 if (ShuffleStrike != null) ShuffleStrike.Visibility = Visibility.Collapsed;
             }
             else 
             {
                 ShuffleButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136)); 
                 if (ShuffleStrike != null) ShuffleStrike.Visibility = Visibility.Visible;
             }
        }

        private void UpdateRepeatIcon()
        {
             var brush = new SolidColorBrush(Windows.UI.Colors.OrangeRed);
             var gray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
             
             // Access the TextBlock inside the Button. 
             // RepeatButton still has TextBlock as content? 
             // Let's check MainPage.xaml XAML previously viewed.
             // Line 163: <Button x:Name="RepeatButton"...><TextBlock .../></Button>
             // So Content IS TextBlock for RepeatButton.
             
             if (RepeatButton?.Content is TextBlock tb)
             {
                 if (CurrentRepeatMode == RepeatMode.Off)
                 {
                     RepeatButton.Foreground = gray;
                     // Arrow -> use Segoe UI
                     tb.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                     tb.Text = "\u2192"; // →
                     tb.FontSize = 24; // Adjust size for arrow
                 }
                 else if (CurrentRepeatMode == RepeatMode.All)
                 {
                     RepeatButton.Foreground = brush;
                     tb.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
                     tb.Text = "\uE895";
                     tb.FontSize = 18;
                 }
                 else // One
                 {
                     RepeatButton.Foreground = brush;
                     tb.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
                     tb.Text = "\uE8ED";
                     tb.FontSize = 18;
                 }
             }
        }
        
        // Helper to update icon when track changes
        // Playback Queue Logic
        private async void GlobalMediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
             if (CurrentRepeatMode == RepeatMode.One)
             {
                 var player = Services.PlaybackService.Instance.Player;
                 if (player.PlaybackSession.CanSeek) player.PlaybackSession.Position = TimeSpan.Zero;
                 player.Play();
                 return;
             }

             // Check if we need to load more (Shuffle Infinite Mode)
             if (IsShuffle && (QueueName == "Random Songs" || QueueName == "Recently Added"))
             {
                 if (CurrentQueueIndex >= PlaybackQueue.Count - 2)
                 {
                      var moreSongs = await SubsonicService.Instance.GetRandomSongs(50);
                      foreach(var s in moreSongs)
                      {
                          // Duplicate check
                          if (!PlaybackQueue.Any(existing => existing.Id == s.Id))
                          {
                              PlaybackQueue.Add(s);
                          }
                      }
                 }
             }
             else
             {
                 // Normal LoadMore logic for IncrementalCollection
                 bool nearEnd = CurrentQueueIndex >= PlaybackQueue.Count - 2; 
                 if (nearEnd && PlaybackQueue.HasMoreItems)
                 {
                     LoadMore();
                 }
             }

             // Continue playback
             int nextIndex = GetNextIndex();
             if (nextIndex != -1)
             {
                 CurrentQueueIndex = nextIndex;
                 PlayTrack(PlaybackQueue[CurrentQueueIndex]);
             }
        }

        private int GetNextIndex()
        {
            if (PlaybackQueue.Count == 0) return -1;
            
            // With Shuffled Queue, next index is always sequential
             if (CurrentQueueIndex < PlaybackQueue.Count - 1)
             {
                 return CurrentQueueIndex + 1;
             }
             else
             {
                  // End of queue
                  if (CurrentRepeatMode == RepeatMode.All) return 0;
                  return -1;
             }
        }

        private double _volume = 1.0;
        public double Volume
        {
            get { return _volume; }
            set
            {
                if (_volume != value)
                {
                    _volume = value;
                    OnPropertyChanged("Volume");
                    
                    // Update Media Element
                    Services.PlaybackService.Instance.Player.Volume = value;
                    
                    // Persist to Settings
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values["Volume"] = value;
                    
                    // Update Display
                    OnPropertyChanged("VolumeDisplay");
                    UpdateVolumeIcon(value);
                }
            }
        }
        
        public string VolumeDisplay
        {
            get { return (Volume * 100).ToString("0"); }
        }

        
        private async void LoadMore()
        {
            await PlaybackQueue.LoadMoreItemsAsync(20);
        }
        
        private void Timer_Tick(object sender, object e)
        {
             // Prevent fighting with user seeking
             if (_isDragging) return;
             try {
                 var session = Services.PlaybackService.Instance.Player.PlaybackSession;
                 double d = session.NaturalDuration.TotalSeconds;
                 if (!double.IsNaN(d) && !double.IsInfinity(d) && d < TimeSpan.MaxValue.TotalSeconds)
                     Duration = d;
                 
                 double p = session.Position.TotalSeconds;
                 if (!double.IsNaN(p) && !double.IsInfinity(p) && p < TimeSpan.MaxValue.TotalSeconds)
                 {
                     _isUpdatingFromTimer = true;
                     CurrentPosition = p;
                     _isUpdatingFromTimer = false;
                 }

                 // Update SMTC Timeline
                 // SMTC Update Removed from Timer to prevent Stutter

             } catch {}
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
             var player = Services.PlaybackService.Instance.Player;
             if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
             {
                 player.Pause();
             }
             else
             {
                 player.Play();
             }
             UpdatePlayPauseIcon();
        }
        
        private void UpdatePlayPauseIcon()
        {
             var session = Services.PlaybackService.Instance.Player.PlaybackSession;
             if (session.PlaybackState == MediaPlaybackState.Playing || session.PlaybackState == MediaPlaybackState.Buffering) 
             {
                 PlayPauseIcon.Symbol = Symbol.Pause;
             if (_smtc != null) 
             {
                 _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                 _smtc.IsPlayEnabled = true;
                 _smtc.IsPauseEnabled = true;
                 _smtc.IsNextEnabled = true;
                 _smtc.IsPreviousEnabled = true;
             }
         }
         else 
         {
             PlayPauseIcon.Symbol = Symbol.Play;
             if (_smtc != null) 
             {
                 _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                 _smtc.IsPlayEnabled = true;
                 _smtc.IsPauseEnabled = true;
                 _smtc.IsNextEnabled = true;
                 _smtc.IsPreviousEnabled = true;
             }
         }
        }
        
        private void RetryTimer_Tick(object sender, object e)
        {
            if (_expectingPlay && CurrentSong != null)
            {
                // Retry playback via robust loader
                _ = LoadSong(CurrentSong, true);
            }
            else
            {
                _retryTimer.Stop();
            }
        }

        private bool _expectingPlay = false;

        private void GlobalMediaElement_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
             // This is now redundant as we bound to Player events, but might be called by legacy paths?
             // Leaving empty or directing to UpdateIcon logic just in case.
             UpdatePlayPauseIcon();
        }


        public async void PlayTrack(SubsonicItem song)
        {
            if (song == null) return;

            // Explicitly reset position
            CurrentPosition = 0; 
            
            var player = Services.PlaybackService.Instance.Player;
            player.Pause();
            // CRITICAL FIX: Clear source immediately to prevent playing old audio if new load fails
            player.Source = null; 
            
            if (player.PlaybackSession.CanSeek) player.PlaybackSession.Position = TimeSpan.Zero;
            
            CurrentSong = song;

            // Trigger Live Tile Update (Active Playback)
            TileManager.UpdateTile(song); 
            
            IsBuffering = true; 
            _expectingPlay = true;
            _retryTimer.Stop(); 
            
            await LoadSong(song, true);
        }

        private async System.Threading.Tasks.Task LoadSong(SubsonicItem song, bool autoPlay)
        {
            try
            {
                if (_smtc != null) _smtc.IsEnabled = true;

                // 1. Start Download (Transient for Playback)
                var context = await Services.PlaybackService.Instance.StartDownload(song, isTransient: true);
                
                // Explicitly Touch File (LRU Update)
                // We only do this here (Playback Start), not during Preload.
                await Services.PlaybackService.Instance.TouchCachedFile(song.Id);
                
                // Determine Threshold
                bool aggressiveCaching = false;
                try
                {
                    var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                    if (settings.ContainsKey("AggressiveBuffering") && (bool)settings["AggressiveBuffering"])
                    {
                        aggressiveCaching = true;
                    }
                }
                catch {}
                
                // 2. Wait for Sufficient Data (Prevent Lockup)
                // Wait for either 256KB (fast start) or completion
                // 2. Wait for Sufficient Data (Prevent Lockup)
                // Wait for either 256KB (fast start) or completion
                const long SUFFICIENT_BUFFER = 256 * 1024; 
                int timeoutMs = 15000; // 15s absolute timeout
                
                while (!context.IsComplete && !context.IsFailed)
                {
                     // If aggressive, wait for 100%
                     if (aggressiveCaching)
                     {
                         if (context.Progress >= 1.0) break;
                     }
                     else
                     {
                         // Standard: Wait for sufficient buffer to prevent starvation
                         if (context.DownloadedBytes >= SUFFICIENT_BUFFER) break;
                     }

                    if (CurrentSong != song) return; // User switched track
                    
                    await Task.Delay(100); // Check every 100ms
                    timeoutMs -= 100;
                    if (timeoutMs <= 0) throw new TimeoutException("Buffering timed out");
                }
                
                if (CurrentSong != song) return;
                
                if (context.IsFailed)
                {
                   throw new Exception("Download failed");
                }
                
                // If download was already complete (cached), trigger preload immediately
                if (context.IsComplete)
                {
                    AttemptPreloadNext(song.Id);
                }

                // 3. Open Read Stream
                // Optimization priority: RAM > Disk (Cached) > Disk (Buffering)
                if (Services.PlaybackService.Instance.IsRamDoubleBufferingEnabled && context.RamStream != null)
                {
                     // RAM Mode: Read from MemoryStream (Always prioritize if available to avoid Disk Reads)
                     var bufferStream = new Services.BufferingStream(context.RamStream, context);
                     Services.PlaybackService.Instance.Player.AutoPlay = autoPlay;
                     Services.PlaybackService.Instance.Player.Source = Windows.Media.Core.MediaSource.CreateFromStream(bufferStream, "audio/mpeg");
                }
                else if (context.IsComplete)
                {
                    if (Services.PlaybackService.Instance.IsRamDoubleBufferingEnabled)
                    {
                         // RAM Mode (Cached): Pre-load file into RAM to ensure 0 disk reads during playback
                         try 
                         {
                             var fileRef = await Windows.Storage.StorageFile.GetFileFromPathAsync(context.FilePath);
                             var props = await fileRef.GetBasicPropertiesAsync();
                             if (props.Size < int.MaxValue)
                             {
                                 var memStream = new MemoryStream((int)props.Size);
                                 using (var fs = await fileRef.OpenStreamForReadAsync()) 
                                 {
                                     await fs.CopyToAsync(memStream); 
                                 }
                                 memStream.Position = 0;
                                 memStream.Position = 0;
                                 context.RamStream = memStream; 
                                 
                                 // CRITICAL: Update context so BufferingStream knows the full size!
                                 context.TotalBytes = (long)props.Size;
                                 context.DownloadedBytes = (long)props.Size;
                                 context.IsComplete = true; // Ensure it knows it's complete 
                                 
                                 // Use BufferingStream to wrap our MemoryStream (handles RandomAccessStream adaptation)
                                 var bufferStream = new Services.BufferingStream(context.RamStream, context);
                                 Services.PlaybackService.Instance.Player.AutoPlay = autoPlay;
                                 Services.PlaybackService.Instance.Player.Source = Windows.Media.Core.MediaSource.CreateFromStream(bufferStream, "audio/mpeg");
                                 
                                 UpdateSmtcMetadata(song);
                                 return;
                             }
                         }
                         catch 
                         { 
                            // Fallback to disk if RAM load fails (OOM, etc)
                         }
                    }

                    // Disk Mode (Cached): Direct File Access
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(context.FilePath);
                    Services.PlaybackService.Instance.Player.AutoPlay = autoPlay;
                    Services.PlaybackService.Instance.Player.Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
                }
                else
                {
                     // Disk Mode (Buffering): FileStream
                     var fs = System.IO.File.Open(context.FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                     var bufferStream = new Services.BufferingStream(fs, context);
                     
                     Services.PlaybackService.Instance.Player.AutoPlay = autoPlay;
                     Services.PlaybackService.Instance.Player.Source = Windows.Media.Core.MediaSource.CreateFromStream(bufferStream, "audio/mpeg");
                } 
                
                UpdateSmtcMetadata(song);
                UpdatePlayPauseIcon();
            }
            catch 
            {
                if (autoPlay) 
                {
                    IsBuffering = true;
                    _retryTimer.Start();
                }
            }
        }
        
        private void OnDownloadCompleted(object sender, string songId)
        {
             AttemptPreloadNext(songId);
        }

        private async void AttemptPreloadNext(string finishedSongId)
        {
             await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
             {
                 if (PlaybackQueue == null || PlaybackQueue.Count == 0) return;
                 
                 // If "Aggressive Caching" is ON, we might want to preload MORE than 1 song?
                 // But for now, "Preload Next" is a baseline feature for smooth playback, regardless of setting.
                 // The user explicitly requested this to work even if "Aggressive Caching" is disabled.
                 
                 // Find Next Song
                 // Only preload if the finished song is the Current Song (or we are just starting it)
                 if (CurrentSong != null && CurrentSong.Id == finishedSongId)
                 {
                      int nextIndex = CurrentQueueIndex + 1;
                      if (nextIndex < PlaybackQueue.Count)
                      {
                          var nextSong = PlaybackQueue[nextIndex];
                          // Start Background Download
                          await Services.PlaybackService.Instance.StartDownload(nextSong, isTransient: true);
                          
                          // Determine Previous Song
                          string prevId = null;
                          int prevIndex = CurrentQueueIndex - 1;
                          if (prevIndex >= 0 && prevIndex < PlaybackQueue.Count)
                          {
                              prevId = PlaybackQueue[prevIndex].Id;
                          }
                          
                          // Clean Cache (Async) - Keep Prev, Current, Next
                          var keep = new System.Collections.Generic.List<string>();
                          if (CurrentSong != null) keep.Add(CurrentSong.Id);
                          if (nextSong != null) keep.Add(nextSong.Id);
                          if (prevId != null) keep.Add(prevId);
                          
                          Services.PlaybackService.Instance.CleanCache(keep);
                      }
                 }
             });
        }
        private void UpdateSmtcMetadata(SubsonicItem song)
        {
             if (_smtc == null || song == null) return;
             var display = _smtc.DisplayUpdater;
             display.Type = MediaPlaybackType.Music;
             display.MusicProperties.Title = song.Title ?? "";
             display.MusicProperties.Artist = song.Artist ?? "";
             display.MusicProperties.AlbumTitle = song.Album ?? "";
             if (!string.IsNullOrEmpty(song.ImageUrl))
             {
                 _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                 {
                     try 
                     { 
                         // 1. Try Offline Cache Sidecar first
                         try
                         {
                             var cacheFolder = await Windows.Storage.ApplicationData.Current.TemporaryFolder.GetFolderAsync("Cache");
                             var cachedArt = await cacheFolder.TryGetItemAsync($"stream_{song.Id}.jpg") as Windows.Storage.StorageFile;
                             if (cachedArt != null)
                             {
                                 display.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(cachedArt);
                                 display.Update();
                                 return; 
                             }
                         }
                         catch {}

                         // 2. Try Local Tile Cache (Best for Lock Screen)
                         var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                         
                         // For now, let's try to just download it to a temp file for SMTC usage
                         var file = await Windows.Storage.ApplicationData.Current.TemporaryFolder.CreateFileAsync("smtc_art.jpg", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                         using (var client = new System.Net.Http.HttpClient())
                         {
                             var buffer = await client.GetByteArrayAsync(new Uri(song.ImageUrl));
                             await Windows.Storage.FileIO.WriteBytesAsync(file, buffer);
                         }
                         
                         display.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file);
                         display.Update();
                     } 
                     catch 
                     {
                         // Fallback to URL (might fail but worth a shot)
                         try { display.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(new Uri(song.ImageUrl)); display.Update(); } catch {}
                     }
                 });
             }
             else
             {
                 display.Update();
             }
        }

        private void UpdateSmtcTimeline()
        {
            if (_smtc == null) return;
            try
            {
                var session = Services.PlaybackService.Instance.Player.PlaybackSession;
                var timeline = new SystemMediaTransportControlsTimelineProperties();
                timeline.StartTime = TimeSpan.Zero;
                timeline.MinSeekTime = TimeSpan.Zero;
                timeline.Position = session.Position;
                
                if (session.NaturalDuration.TotalSeconds > 0)
                    timeline.MaxSeekTime = session.NaturalDuration;
                else if (CurrentSong != null && CurrentSong.Duration > 0)
                    timeline.MaxSeekTime = TimeSpan.FromSeconds(CurrentSong.Duration);
                else
                    timeline.MaxSeekTime = TimeSpan.Zero;
                    
                timeline.EndTime = timeline.MaxSeekTime;
                
                _smtc.UpdateTimelineProperties(timeline);
            }
            catch { }
        }
        
        private void Prev_Click(object sender, RoutedEventArgs e)
        {
             if (Services.PlaybackService.Instance.Player.PlaybackSession.Position.TotalSeconds > 5)
             {
                 Services.PlaybackService.Instance.Player.PlaybackSession.Position = TimeSpan.Zero;
             }
             else if (CurrentQueueIndex > 0)
             {
                 CurrentQueueIndex--;
                 PlayTrack(PlaybackQueue[CurrentQueueIndex]); 
             }
        }
        
        public void Prev_Click_Public() => Prev_Click(null, null);

        private void Next_Click(object sender, RoutedEventArgs e)
        {
             int next = GetNextIndex();
             if (next != -1)
             {
                 CurrentQueueIndex = next;
                 PlayTrack(PlaybackQueue[CurrentQueueIndex]); 
             }
        }

        public void Next_Click_Public() => Next_Click(null, null);

        private SystemMediaTransportControls _smtc;

        private void InitSMTC()
        {
             // Subscribe to Player Events
             var player = Services.PlaybackService.Instance.Player;
             player.MediaEnded += Player_MediaEnded;
             player.PlaybackSession.PlaybackStateChanged += Player_PlaybackStateChanged;
             player.MediaFailed += Player_MediaFailed;
             
             _smtc = SystemMediaTransportControls.GetForCurrentView();
             _smtc.ButtonPressed -= Smtc_ButtonPressed;
             _smtc.ButtonPressed += Smtc_ButtonPressed;
             
             _smtc.IsPlayEnabled = true;
             _smtc.IsPauseEnabled = true;
             _smtc.IsNextEnabled = true;
             _smtc.IsPreviousEnabled = true;

            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.ContainsKey("Volume"))
            {
                 var vol = Windows.Storage.ApplicationData.Current.LocalSettings.Values["Volume"];
                 _volume = (double)vol; 
                 // Set Player Volume
                 player.Volume = _volume;
                 UpdateVolumeIcon(_volume);
            }

     }

        private async void Player_MediaEnded(Windows.Media.Playback.MediaPlayer sender, object args)
        {
             await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => GlobalMediaElement_MediaEnded(null, null));
        }

        private async void Player_PlaybackStateChanged(Windows.Media.Playback.MediaPlaybackSession sender, object args)
        {
             await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
             {
                  UpdatePlayPauseIcon();
                  UpdateSmtcTimeline();
                  
                  // Map PlaybackState to Buffering Logic
                  if (sender.PlaybackState == MediaPlaybackState.Buffering || sender.PlaybackState == MediaPlaybackState.Opening)
                  {
                      IsBuffering = true;
                  }
                  else if (sender.PlaybackState == MediaPlaybackState.Playing)
                  {
                      IsBuffering = false;
                      _expectingPlay = false; 
                      _retryTimer.Stop();
                  }
                  else if (sender.PlaybackState == MediaPlaybackState.Paused)
                  {
                      IsBuffering = false;
                      _expectingPlay = false; 
                      _retryTimer.Stop();
                  }
                  // Handle Error/None?
             });
        }

        private async void Player_MediaFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
        {
             await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => GlobalMediaElement_MediaFailed(null, null));
        }

        private void GlobalMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (_expectingPlay)
            {
                // Keep buffering if we are expecting play (offline/error)
                IsBuffering = true;
                if (!_retryTimer.IsEnabled) _retryTimer.Start();
            }
            else
            {
                IsBuffering = false;
                _retryTimer.Stop();
            }
        }

        private async void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                var player = Services.PlaybackService.Instance.Player;
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        player.Play();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        player.Pause();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        Next_Click(null, null);
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        Prev_Click(null, null);
                        break;
                }
            });
        }




        private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleFavorite();
        }

        private void MobileSearchButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(typeof(SearchPage));
        }

        public async System.Threading.Tasks.Task ToggleFavorite()
        {
            if (CurrentSong == null) return;
            if (CurrentSong.IsStarred)
            {
                bool success = await SubsonicService.Instance.UnstarItem(CurrentSong.Id);
                if (success) CurrentSong.IsStarred = false;
            }
            else
            {
                bool success = await SubsonicService.Instance.StarItem(CurrentSong.Id);
                if (success) CurrentSong.IsStarred = true;
            }
            // Trigger property changed to update UI (Converter)
            OnPropertyChanged("CurrentSong");
            // Also need to update the button icon manually if not bound?
            // In MainPage XAML, FavoriteIcon is distinct.
            UpdateFavoriteIcon();
        }

        public void UpdateFavoriteIcon()
        {
            if (CurrentSong == null) return;
            if (FavoriteIcon != null)
            {
                FavoriteIcon.Symbol = CurrentSong.IsStarred ? Symbol.SolidStar : Symbol.OutlineStar;
                // Use default foreground for filled star, instead of Orange
                // Assuming "ForegroundBrush" resource exists or use default
                if (CurrentSong.IsStarred)
                {
                     if (Application.Current.Resources.TryGetValue("ForegroundBrush", out object brush))
                         FavoriteIcon.Foreground = brush as SolidColorBrush;
                     else
                         FavoriteIcon.Foreground = new SolidColorBrush(Windows.UI.Colors.White);
                }
                else
                {
                     FavoriteIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255,136,136,136));
                }
            }
        }
        public void NavigateTo(Type pageType)
        {
             ContentFrame.Navigate(pageType);
        }

    }
}
#pragma warning restore CS0618 // Type or member is obsolete
