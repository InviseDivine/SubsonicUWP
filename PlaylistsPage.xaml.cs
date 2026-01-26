using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class PlaylistsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Playlists { get; set; }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;

        public PlaylistsPage()
        {
            this.InitializeComponent();
            Playlists = new ObservableCollection<SubsonicItem>();
            this.DataContext = this;
            this.Loaded += PlaylistsPage_Loaded;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
             await LoadPlaylists();
        }

        private async void PlaylistsPage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (Playlists.Count == 0)
            {
                await LoadPlaylists();
            }
        }

        private async System.Threading.Tasks.Task LoadPlaylists()
        {
            try
            {
                var list = await SubsonicService.Instance.GetPlaylists();
                if (list != null)
                {
                    // Clear only if valid list returned to avoid flashing
                    Playlists.Clear();
                    foreach (var item in list) Playlists.Add(item);
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

        private void Playlist_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                  Frame.Navigate(typeof(PlaylistDetailsPage), item);
             }
        }

         private async void PlayPlaylist_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
         {
              if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
              {
                   var tracks = await SubsonicService.Instance.GetPlaylist(item.Id);
                   var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                   if (mp != null && tracks.Count > 0)
                   {
                       mp.PlayTrackList(tracks, tracks[0], item.Title);
                   }
              }
         }

         private async void DeletePlaylist_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
         {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                  // Confirm?
                  var dialog = new Windows.UI.Popups.MessageDialog($"Are you sure you want to delete playlist '{item.Title}'?");
                  dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes") { Id = 0 });
                  dialog.Commands.Add(new Windows.UI.Popups.UICommand("No") { Id = 1 });
                  dialog.DefaultCommandIndex = 0;
                  dialog.CancelCommandIndex = 1;
                  var result = await dialog.ShowAsync();
                  
                  if ((int)result.Id == 0)
                  {
                      bool success = await SubsonicService.Instance.DeletePlaylist(item.Id);
                      if (success)
                      {
                          Playlists.Remove(item);
                      }
                  }
             }
         }

         private void Button_SwallowTap(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
         {
             e.Handled = true;
         }
    }
}
