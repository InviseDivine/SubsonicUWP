using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Controls.Primitives;

namespace SubsonicUWP
{
    public sealed partial class ArtistDetailsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Albums { get; set; } = new ObservableCollection<SubsonicItem>();
        public ObservableCollection<SubsonicItem> TopSongs { get; set; } = new ObservableCollection<SubsonicItem>();
        
        private ObservableCollection<SubsonicItem> _allAlbums = new ObservableCollection<SubsonicItem>();
        private bool _albumsExpanded = false;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;
        private string _currentArtistId;

        public ArtistDetailsPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is string artistId)
            {
                _currentArtistId = artistId;
                await LoadArtistData();
            }
            base.OnNavigatedTo(e);
        }
        
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _retryTimer.Stop();
            base.OnNavigatedFrom(e);
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
            await LoadArtistData();
        }

        private async System.Threading.Tasks.Task LoadArtistData()
        {
            if (string.IsNullOrEmpty(_currentArtistId)) return;
            try
            {
                // Load info
                var data = await SubsonicService.Instance.GetArtist(_currentArtistId);
                var artist = data.Item1;
                ArtistName.Text = artist.Title ?? "Unknown Artist";
                if (!string.IsNullOrEmpty(artist.CoverArtId))
                {
                    ArtistImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(SubsonicService.Instance.GetCoverArtUrl(artist.CoverArtId)));
                }

                _allAlbums.Clear();
                foreach(var a in data.Item2) _allAlbums.Add(a);
                UpdateAlbumView();

                TopSongs.Clear();
                var songs = await SubsonicService.Instance.GetArtistTopSongs(artist.Title);
                foreach(var s in songs) TopSongs.Add(s);
                
                IsOffline = false;
                _retryTimer.Stop();
            }
            catch
            {
                IsOffline = true;
                if (!_retryTimer.IsEnabled) _retryTimer.Start();
            }
        }

        private void UpdateAlbumView()
        {
            Albums.Clear();
            var limit = _albumsExpanded ? _allAlbums.Count : Math.Min(5, _allAlbums.Count);
            for(int i=0; i<limit; i++) Albums.Add(_allAlbums[i]);
            
            AlbumsChevron.Text = _albumsExpanded ? "\uE70E" : "\uE70D"; // Up/Down
        }

        private void ToggleAlbums_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to AlbumsPage with the full list
            Frame.Navigate(typeof(AlbumsPage), _allAlbums);
        }

        private void Album_Click(object sender, ItemClickEventArgs e)
        {
             // Navigate to Album Details instead of playing immediately
             if (e.ClickedItem is SubsonicItem album)
             {
                 Frame.Navigate(typeof(AlbumDetailsPage), album);
             }
        }

        private void Song_Click(object sender, ItemClickEventArgs e)
        {
             if (e.ClickedItem is SubsonicItem song)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     mp.PlayTrackList(TopSongs, song, ArtistName.Text + " Top Songs");
                 }
             }
        }

        private void ShuffleArtist_Click(object sender, RoutedEventArgs e)
        {
             // For now, shuffle Top Songs. Ideally we'd get all songs.
             if (TopSongs.Count > 0)
             {
                 var rnd = new Random();
                 var shuffled = TopSongs.OrderBy(x => rnd.Next()).ToList();
                 
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     mp.PlayTrackList(shuffled, shuffled[0], ArtistName.Text + " Mix");
                 }
             }
        }

        // --- Context Menu Handlers ---

        private void Album_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe) FlyoutBase.ShowAttachedFlyout(fe);
        }

        private void Song_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe) FlyoutBase.ShowAttachedFlyout(fe);
        }

        private async void AlbumPlay_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                     if (tracks.Count > 0)
                     {
                         mp.PlayTrackList(tracks, tracks[0], album.Title);
                     }
                 }
             }
        }

        private async void AlbumPlayNext_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                     if (tracks.Count > 0) mp.PlayNext(tracks);
                 }
             }
        }

        private async void AlbumAddToQueue_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                     if (tracks.Count > 0) mp.AddToQueue(tracks);
                 }
             }
        }

        private async void AlbumExport_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                 if (tracks.Count > 0)
                 {
                     if (tracks.Count > 1)
                     {
                         var dialog = new Windows.UI.Popups.MessageDialog($"Starting export for {tracks.Count} tracks...", "Export Album");
                         await dialog.ShowAsync();
                     }
                     foreach(var t in tracks)
                     {
                         if (string.IsNullOrEmpty(t.Artist)) t.Artist = album.Artist;
                         if (string.IsNullOrEmpty(t.Album)) t.Album = album.Title;
                         await DownloadManager.StartDownload(t);
                     }
                 }
             }
        }

        private async void AlbumAddToCache_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                 if (tracks.Count > 0)
                 {
                     foreach(var t in tracks)
                     {
                         if (string.IsNullOrEmpty(t.Artist)) t.Artist = album.Artist;
                         if (string.IsNullOrEmpty(t.Album)) t.Album = album.Title;
                     }
                     Services.PlaybackService.Instance.EnqueueDownloads(tracks, isTransient: false);
                 }
             }
        }

        private void SongPlayNext_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                mp?.PlayNext(item);
            }
        }

        private void SongAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                mp?.AddToQueue(item);
            }
        }

        private void SongAddToCache_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                Services.PlaybackService.Instance.EnqueueDownload(item, isTransient: false);
            }
        }

        private async void SongExport_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                await DownloadManager.StartDownload(item);
            }
        }
    }
}
