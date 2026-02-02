using System;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public sealed partial class RecentSongsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public IncrementalLoadingCollection<SubsonicItem> Songs { get; set; }
        private int _albumOffset = 0;
        
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;

        public RecentSongsPage()
        {
            this.InitializeComponent();
            InitializeCollection();
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private void InitializeCollection()
        {
            _albumOffset = 0;
            Songs = new IncrementalLoadingCollection<SubsonicItem>(async (count) =>
            {
                try
                {
                    // Fetch 5 albums at a time
                    var newSongs = await SubsonicService.Instance.GetRecentSongs(_albumOffset, 5);
                    _albumOffset += 5;
                    
                    if (IsOffline)
                    {
                        IsOffline = false;
                        _retryTimer.Stop();
                    }
                    return newSongs;
                }
                catch
                {
                    IsOffline = true;
                    if (!_retryTimer.IsEnabled) _retryTimer.Start();
                    return null; // Signal error, keep HasMoreItems=true
                }
            });
            OnPropertyChanged("Songs");
        }

        private void RetryTimer_Tick(object sender, object e)
        {
             if (Songs.Count == 0)
             {
                 // Restart initial load
                 InitializeCollection();
             }
             else
             {
                 // If we have items but failed paging, just hide the error overlay (or keep it?) 
                 // If we keep it, we block the view.
                 // Ideally we verify connectivity.
                 // Let's assume we just hide it and let user retry scrolling.
                 IsOffline = false;
                 _retryTimer.Stop();
             }
        }

        private void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem song)
            {
                var frame = Window.Current.Content as Frame;
                var mainPage = frame.Content as MainPage;
                // Pass "Recently Added" to trigger infinite scroll for Recent
                mainPage?.PlayTrackList(Songs, song, "Recently Added");
            }
        }

        private void MediaItemGrid_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
             Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
             e.Handled = true;
        }

        private void MediaItemGrid_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
                e.Handled = true;
            }
        }
        
        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.AddToQueue(item);
            }
        }

        private void PlayNext_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.PlayNext(item);
            }
        }

        private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 mp?.ShowPlaylistPicker(item);
             }
        }

        private void AddToCache_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                  Services.PlaybackService.Instance.EnqueueDownload(item, isTransient: false);
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
