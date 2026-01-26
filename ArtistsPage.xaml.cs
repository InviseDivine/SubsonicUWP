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

        private async void PlayArtist_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                  // Stop propagation isn't strictly needed if button handles it, but good practice
                  e.OriginalSource.ToString(); 
                  
                  try
                  {
                      var songs = await SubsonicService.Instance.GetArtistTopSongs(item.Title);
                      var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                      if (mp != null && songs.Count > 0)
                      {
                          mp.PlayTrackList(songs, songs[0], item.Title);
                      }
                  }
                  catch {}
             }
        }
    }
}
