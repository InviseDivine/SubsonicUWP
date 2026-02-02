using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Linq;

namespace SubsonicUWP
{
    public sealed partial class AlbumDetailsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Tracks { get; set; } = new ObservableCollection<SubsonicItem>();
        private SubsonicItem _album;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;
        private object _navParam;

        public AlbumDetailsPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _navParam = e.Parameter;
            await LoadAlbumData();
            base.OnNavigatedTo(e);
        }
        
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _retryTimer.Stop();
            base.OnNavigatedFrom(e);
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
            await LoadAlbumData();
        }

        private async System.Threading.Tasks.Task LoadAlbumData()
        {
            if (_navParam == null) return;
            try
            {
                if (_navParam is SubsonicItem album)
                {
                    _album = album;
                    AlbumTitle.Text = album.Title;
                    AlbumArtist.Text = album.Artist;
                    if (!string.IsNullOrEmpty(album.ImageUrl)) AlbumCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(album.ImageUrl));
                    
                    Tracks.Clear();
                    var list = await SubsonicService.Instance.GetAlbum(album.Id);
                    foreach(var t in list) Tracks.Add(t);
                }
                else if (_navParam is string albumId)
                {
                    Tracks.Clear();
                    var list = await SubsonicService.Instance.GetAlbum(albumId);
                    foreach(var t in list) Tracks.Add(t);
                    
                    if (Tracks.Count > 0)
                    {
                        var first = Tracks[0];
                        _album = new SubsonicItem 
                        { 
                            Id = albumId, 
                            Title = first.Album, 
                            Artist = first.Artist, 
                            CoverArtId = first.CoverArtId 
                        };
                        AlbumTitle.Text = _album.Title;
                        AlbumArtist.Text = _album.Artist;
                        if (!string.IsNullOrEmpty(_album.ImageUrl)) AlbumCover.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_album.ImageUrl));
                    }
                }
                
                IsOffline = false;
                _retryTimer.Stop();
            }
            catch
            {
                IsOffline = true;
                if (!_retryTimer.IsEnabled) _retryTimer.Start();
            }
            UpdatePinState();
        }

        private void PlayAlbum_Click(object sender, RoutedEventArgs e)
        {
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null && Tracks.Count > 0)
             {
                 mp.PlayTrackList(Tracks, Tracks[0], _album?.Title ?? "Album");
             }
        }

        private void TrackList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem item)
            {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     mp.PlayTrackList(Tracks, item, _album?.Title ?? "Album");
                 }
            }
        }

        private async void ExportAlbum_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks != null && Tracks.Any())
            {
                if (Tracks.Count > 1)
                {
                    var dialog = new Windows.UI.Popups.MessageDialog($"Starting export for {Tracks.Count} tracks...", "Export Album");
                    await dialog.ShowAsync();
                }
                
                // Should we optimize artist/album info if missing before enqueueing?
                foreach (var t in Tracks)
                {
                    if (string.IsNullOrEmpty(t.Artist)) t.Artist = _album.Artist;
                    if (string.IsNullOrEmpty(t.Album)) t.Album = _album.Title;
                    await DownloadManager.StartDownload(t);
                }
            }
        }

        private void AddToCacheAlbum_Click(object sender, RoutedEventArgs e)
        {
            if (Tracks != null && Tracks.Any())
            {
                foreach (var t in Tracks)
                {
                    if (string.IsNullOrEmpty(t.Artist)) t.Artist = _album.Artist;
                    if (string.IsNullOrEmpty(t.Album)) t.Album = _album.Title;
                }
                Services.PlaybackService.Instance.EnqueueDownloads(Tracks, isTransient: false);
            }
        }

        private void TrackPlay_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.PlayTrackList(Tracks, item, _album?.Title ?? "Album");
            }
        }

        private void TrackPlayNext_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.PlayNext(item);
            }
        }

        private void TrackAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.AddToQueue(item);
            }
        }

        private async void TrackExport_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                if (string.IsNullOrEmpty(item.Artist)) item.Artist = _album?.Artist;
                await DownloadManager.StartDownload(item);
            }
        }

        private void TrackAddToCache_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                if (string.IsNullOrEmpty(item.Artist)) item.Artist = _album?.Artist;
                Services.PlaybackService.Instance.EnqueueDownload(item, isTransient: false);
            }
        }

        private async void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_album == null) return;
            
            bool isPinned = await PinnedAlbumManager.IsPinned(_album.Id);
            if (isPinned)
            {
                await PinnedAlbumManager.UnpinAlbum(_album.Id);
            }
            else
            {
                // Ensure we have cover art info
                if (string.IsNullOrEmpty(_album.ImageUrl) && Tracks.Count > 0)
                {
                     // Try to construct it if missing (legacy logic often built it from ID)
                     // But _album usually has it from nav. 
                }
                
                // Check if we are exceeding the Live Tile limit (5)
                var currentPinned = await PinnedAlbumManager.GetPinnedAlbums();
                if (currentPinned.Count >= 5)
                {
                     var dialog = new Windows.UI.Popups.MessageDialog(
                        "You have pinned more than 5 albums. Windows Live Tiles can only rotate 5 items at a time.\n\n" +
                        "Your pinned albums will be randomly selected to fill the 5 slots each time the app runs.",
                        "Live Tile Limit");
                     await dialog.ShowAsync();
                }
                
                await PinnedAlbumManager.PinAlbum(_album);
            }
            UpdatePinState();
        }

        private async void UpdatePinState()
        {
            if (_album == null) return;
            bool isPinned = await PinnedAlbumManager.IsPinned(_album.Id);
            
            // Update Icon
            PinButton.Content = new SymbolIcon(isPinned ? Symbol.UnPin : Symbol.Pin);
            
            // Update ToolTip
            ToolTipService.SetToolTip(PinButton, isPinned ? "Unpin from Tile" : "Pin to Tile");
        }
    }
}
