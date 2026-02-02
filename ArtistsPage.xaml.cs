using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public sealed partial class ArtistsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Artists { get; set; }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;

        public ArtistsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;
            Artists = new ObservableCollection<SubsonicItem>();
            this.Loaded += ArtistsPage_Loaded;
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
            await LoadArtists();
        }

        private async void ArtistsPage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             if (Artists.Count == 0)
             {
                 await LoadArtists();
             }
        }

        private async System.Threading.Tasks.Task LoadArtists()
        {
            try
            {
                var result = await SubsonicService.Instance.GetArtists();
                if (result != null)
                {
                    Artists.Clear();
                    foreach (var item in result) Artists.Add(item);
                    IsOffline = false;
                    _retryTimer.Stop();
                }
            }
            catch
            {
                IsOffline = true;
                if (!_retryTimer.IsEnabled) _retryTimer.Start();
            }
        }

        private void Artist_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                  Frame.Navigate(typeof(ArtistDetailsPage), item.Id);
             }
        }

        private void Artist_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
             if (sender is FrameworkElement fe) Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(fe);
             e.Handled = true;
        }

        private void Artist_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
               if (sender is FrameworkElement fe) Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(fe);
               e.Handled = true;
            }
        }

        private async void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 var songs = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                 var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null && songs.Count > 0) mp.AddToQueue(songs);
             }
        }
        
        private async void PlayNext_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 var songs = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                 var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null && songs.Count > 0) mp.PlayNext(songs);
             }
        }

        private async void AddToCache_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 var songs = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                 if (songs.Count > 0)
                 {
                      if (songs.Count > 25)
                      {
                          var dialog = new Windows.UI.Popups.MessageDialog($"Are you sure that you want to cache {songs.Count} songs?", "Confirm Cache");
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes"));
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("No"));
                          var result = await dialog.ShowAsync();
                          if (result.Label != "Yes") return;
                      }

                      Services.PlaybackService.Instance.EnqueueDownloads(songs, isTransient: false);
                 }
             }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 var songs = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                 if (songs.Count > 0)
                 {
                      if (songs.Count > 25)
                      {
                          var dialog = new Windows.UI.Popups.MessageDialog($"Are you sure that you want to export {songs.Count} songs?", "Confirm Export");
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes"));
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("No"));
                          var result = await dialog.ShowAsync();
                          if (result.Label != "Yes") return;
                      }
                      else if (songs.Count > 1) 
                      {
                          // Show old dialog for small batch > 1 (optional, but consistent with other pages)
                          var dialog = new Windows.UI.Popups.MessageDialog($"Starting export for {songs.Count} songs...", "Export Artist");
                          await dialog.ShowAsync();
                      }
                      
                      foreach(var t in songs)
                      {
                          await DownloadManager.StartDownload(t);
                      }
                 }
             }
        }
    }
}
