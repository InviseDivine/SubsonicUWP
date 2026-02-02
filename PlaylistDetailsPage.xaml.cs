using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SubsonicUWP
{
    public sealed partial class PlaylistDetailsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Tracks { get; set; } = new ObservableCollection<SubsonicItem>();
        private SavedSession _currentSession;
        private string _playlistId; // Subsonic Playlist ID

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }
        
        private DispatcherTimer _retryTimer;
        private NavigationEventArgs _navArgs;

        public PlaylistDetailsPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _navArgs = e;
            await LoadData();
            base.OnNavigatedTo(e);
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
            await LoadData();
        }

        private async System.Threading.Tasks.Task LoadData()
        {
            if (_navArgs == null) return;
            var e = _navArgs;

            try
            {
                _playlistId = null;
                if (e.Parameter is SavedSession session)
                {
                    _currentSession = session;
                    NameBox.Text = session.Name;
                    NameBox.IsReadOnly = false;
                    RenameButton.Visibility = Visibility.Visible;
                    
                    Tracks.Clear();
                    // Saved sessions are local, so they shouldn't fail offline unless they try to load remote images immediately?
                    // But usually tracks are just objects.
                    foreach (var t in session.Tracks) Tracks.Add(t);
                    
                    SaveOrderButton.Visibility = Visibility.Collapsed;
                    SaveAsPlaylistButton.Visibility = Visibility.Visible;
                    IsOffline = false;
                }
                else if (e.Parameter is SubsonicItem playlistItem)
                {
                     _playlistId = playlistItem.Id;
                     NameBox.Text = playlistItem.Title;
                     NameBox.IsReadOnly = false; 
                     RenameButton.Visibility = Visibility.Visible;
                     
                     _currentSession = new SavedSession { Name = playlistItem.Title, Id = Guid.NewGuid() }; 
                     
                     Tracks.Clear();
                     var playlistTracks = await SubsonicService.Instance.GetPlaylist(playlistItem.Id);
                     foreach(var t in playlistTracks) Tracks.Add(t);
                     
                     SaveOrderButton.Visibility = Visibility.Visible;
                     SaveAsPlaylistButton.Visibility = Visibility.Collapsed;
                     
                     IsOffline = false;
                     _retryTimer.Stop();
                }
            }
            catch
            {
                if (!string.IsNullOrEmpty(_playlistId))
                {
                     IsOffline = true;
                     if (!_retryTimer.IsEnabled) _retryTimer.Start();
                }
            }
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            NameBox.Focus(FocusState.Programmatic);
            NameBox.SelectAll();
        }

        private async void NameBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // Commit rename
                this.Focus(FocusState.Programmatic); // Triggers LostFocus
            }
        }

        private async void NameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string newName = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                 // Revert
                 if (_currentSession != null) NameBox.Text = _currentSession.Name;
                 return;
            }

            if (!string.IsNullOrEmpty(_playlistId))
            {
                // Server Playlist Rename
                if (newName != _currentSession?.Name)
                {
                    bool success = await SubsonicService.Instance.RenamePlaylist(_playlistId, newName);
                    if (success)
                    {
                        if (_currentSession != null) _currentSession.Name = newName;
                    }
                    else
                    {
                        // Revert on failure
                        if (_currentSession != null) NameBox.Text = _currentSession.Name;
                    }
                }
            }
            else if (_currentSession != null)
            {
                // Saved Session Rename
                if (newName != _currentSession.Name)
                {
                    await SessionManager.RenameSession(_currentSession, newName);
                    _currentSession.Name = newName;
                }
            }
        }

        private async void SaveOrder_Click(object sender, RoutedEventArgs e)
        {
             if (!string.IsNullOrEmpty(_playlistId))
             {
                 // Collect IDs
                 var ids = System.Linq.Enumerable.Select(Tracks, t => t.Id);
                 bool success = await SubsonicService.Instance.UpdatePlaylist(_playlistId, ids);
                 
                 var dialog = new Windows.UI.Popups.MessageDialog(success ? "Playlist order saved." : "Failed to save order.");
                 await dialog.ShowAsync();
             }
        }

        private void PlayAll_Click(object sender, RoutedEventArgs e)
        {
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null && Tracks.Count > 0)
             {
                 mp.PlayTrackList(Tracks, Tracks[0], _currentSession?.Name ?? "Playlist", _currentSession?.Id);
             }
        }

        private void TrackList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem item)
            {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     mp.PlayTrackList(Tracks, item, _currentSession?.Name ?? "Playlist", _currentSession?.Id);
                 }
            }
        }


        private async void SaveAsPlaylist_Click(object sender, RoutedEventArgs e)
        {
             if (Tracks.Count > 0)
             {
                 var nameBox = new TextBox { PlaceholderText = "New Playlist Name", Text = (NameBox.Text ?? "New Playlist") + " (Copy)" };
                 var dialog = new ContentDialog
                 {
                     Title = "Save as Playlist",
                     Content = nameBox,
                     PrimaryButtonText = "Save",
                     SecondaryButtonText = "Cancel"
                 };
                 
                 var result = await dialog.ShowAsync();
                 if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
                 {
                     var ids = System.Linq.Enumerable.Select(Tracks, t => t.Id);
                     bool success = await SubsonicService.Instance.CreatePlaylist(nameBox.Text, ids);
                     
                     var msg = new Windows.UI.Popups.MessageDialog(success ? $"Created playlist '{nameBox.Text}'." : "Failed to create playlist.");
                     await msg.ShowAsync();
                 }
             }
        }
        private void Grid_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                e.Handled = true;
                if (fe.ContextFlyout != null)
                {
                    fe.ContextFlyout.ShowAt(fe, new Windows.UI.Xaml.Controls.Primitives.FlyoutShowOptions 
                    { 
                        Position = e.GetPosition(fe) 
                    });
                }
            }
        }

        private void PlayNext_Click(object sender, RoutedEventArgs e)
        {
             if (sender is FrameworkElement fe && fe.Tag is string id)
             {
                 var song = System.Linq.Enumerable.FirstOrDefault(Tracks, x => x.Id == id);
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
                 var song = System.Linq.Enumerable.FirstOrDefault(Tracks, x => x.Id == id);
                 if (song != null)
                 {
                     Services.PlaybackService.Instance.AddToQueue(song);
                 }
             }
        }
        
        private void AddToCache_Click(object sender, RoutedEventArgs e)
        {
             if (sender is FrameworkElement fe && fe.Tag is string id)
             {
                 var song = System.Linq.Enumerable.FirstOrDefault(Tracks, x => x.Id == id);
                 if (song != null)
                 {
                     Services.PlaybackService.Instance.EnqueueDownload(song, isTransient: false);
                 }
             }
        }

        private void AddToCacheAll_Click(object sender, RoutedEventArgs e)
        {
             if (Tracks.Count > 0)
             {
                 Services.PlaybackService.Instance.EnqueueDownloads(Tracks, isTransient: false);
             }
        }

        private async void ExportAll_Click(object sender, RoutedEventArgs e)
        {
             if (Tracks.Count > 0)
             {
                 if (Tracks.Count > 1)
                 {
                     var dialog = new Windows.UI.Popups.MessageDialog($"Starting export for {Tracks.Count} tracks...", "Export Playlist");
                     await dialog.ShowAsync();
                 }
                 
                 foreach (var t in Tracks)
                 {
                     await DownloadManager.StartDownload(t);
                 }
             }
        }

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string id)
            {
               var item = System.Linq.Enumerable.FirstOrDefault(Tracks, x => x.Id == id);
               if (item != null) 
               {
                   Tracks.Remove(item);
                   
                   if (!string.IsNullOrEmpty(_playlistId))
                   {
                        var ids = System.Linq.Enumerable.Select(Tracks, t => t.Id);
                        await SubsonicService.Instance.UpdatePlaylist(_playlistId, ids);
                   }
                   else if (_currentSession != null)
                   {
                       _currentSession.Tracks = System.Linq.Enumerable.ToList(Tracks);
                       await SessionManager.UpdateSession(_currentSession);
                   }
               }
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                await DownloadManager.StartDownload(item);
            }
        }

    }
}
