using System;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public sealed partial class AlbumsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Albums { get; set; }
        private int _offset = 0;
        private DispatcherTimer _retryTimer;

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        public AlbumsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;
            // Default to empty; specific type set in OnNavigatedTo
            Albums = new ObservableCollection<SubsonicItem>();
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
             // Try to reload
             _offset = 0;
             InitializeLoadingCollection();
        }

        private void InitializeLoadingCollection()
        {
             Albums = new IncrementalLoadingCollection<SubsonicItem>(async (count) =>
             {
                 try
                 {
                     var result = await SubsonicService.Instance.GetAlbumList("newest", _offset, 20);
                     if (result != null && result.Count > 0)
                     {
                         _offset += 20;
                     }
                     // Successful load
                     if (IsOffline) 
                     {
                         IsOffline = false; 
                         _retryTimer.Stop();
                     }
                     return result;
                 }
                 catch
                 {
                     IsOffline = true;
                     if (!_retryTimer.IsEnabled) _retryTimer.Start();
                     return new ObservableCollection<SubsonicItem>(); // Return empty to stop infinite spinning of the list itself
                 }
             });
             OnPropertyChanged("Albums");
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            if (e.Parameter is System.Collections.Generic.IEnumerable<SubsonicItem> albumList)
            {
                 // Static list (Artist Discography)
                 Albums = new ObservableCollection<SubsonicItem>(albumList);
                 OnPropertyChanged("Albums");
            }
            else
            {
                // Default behavior: Recently Added (Infinite)
                // Only load if empty or explicitly refreshed (handling via Param?)
                // If we are cached and have items, do not reset.
                if (Albums == null || Albums.Count == 0 || !(Albums is IncrementalLoadingCollection<SubsonicItem>))
                {
                    _offset = 0;
                    InitializeLoadingCollection();
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private void AlbumsPage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             // Moved logic to OnNavigatedTo to support params
        }

        private void Album_Click(object sender, Windows.UI.Xaml.Controls.ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem album)
            {
                Frame.Navigate(typeof(AlbumDetailsPage), album);
            }
        }

        private void Grid_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is Windows.UI.Xaml.FrameworkElement fe)
            {
                Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(fe);
            }
        }

        private async void AlbumPlay_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             if ((sender as Windows.UI.Xaml.FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.Controls.Frame)?.Content as MainPage;
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

        private async void AlbumPlayNext_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             if ((sender as Windows.UI.Xaml.FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.Controls.Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                     if (tracks.Count > 0) mp.PlayNext(tracks);
                 }
             }
        }

        private async void AlbumAddToQueue_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             if ((sender as Windows.UI.Xaml.FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.Controls.Frame)?.Content as MainPage;
                 if (mp != null)
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                     if (tracks.Count > 0) mp.AddToQueue(tracks);
                 }
             }
        }

        private async void AlbumDownload_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             if ((sender as Windows.UI.Xaml.FrameworkElement)?.DataContext is SubsonicItem album)
             {
                 var tracks = await SubsonicService.Instance.GetAlbum(album.Id);
                 if (tracks.Count > 0)
                 {
                     var dialog = new Windows.UI.Popups.MessageDialog($"Starting download for {tracks.Count} tracks...", "Download Album");
                     await dialog.ShowAsync();
                     foreach(var t in tracks)
                     {
                         if (string.IsNullOrEmpty(t.Artist)) t.Artist = album.Artist;
                         if (string.IsNullOrEmpty(t.Album)) t.Album = album.Title;
                         await DownloadManager.StartDownload(t);
                     }
                 }
             }
        }
    }
}
