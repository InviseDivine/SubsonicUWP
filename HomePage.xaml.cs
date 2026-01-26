using System;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public sealed partial class HomePage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Favorites { get; set; }
        public ObservableCollection<SubsonicItem> RecentItems { get; set; }
        public ObservableCollection<SubsonicItem> RandomSongs { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;

        public HomePage()
        {
            this.InitializeComponent();
            Favorites = new ObservableCollection<SubsonicItem>();
            RecentItems = new ObservableCollection<SubsonicItem>();
            RandomSongs = new ObservableCollection<SubsonicItem>();
            
            // Enable Caching to preserve Random Songs on Back
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;
            
            this.Loaded += HomePage_Loaded;
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
            await LoadHomeContent();
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // Load in parallel without blocking UI thread
            var _ = LoadHomeContent();
        }

        private async System.Threading.Tasks.Task LoadHomeContent()
        {
            var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
            bool anyError = false;

            // Random Songs
            if (RandomSongs.Count == 0)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(async () => 
                {
                    try 
                    {
                        var songs = await SubsonicService.Instance.GetRandomSongs(20);
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => 
                        {
                             // If service returns empty list during offline (swallowed in service? No, I fixed it to throw), check for empty
                             // But wait, I removed try/catch in service, so it THROWS now.
                             foreach (var s in songs) RandomSongs.Add(s);
                        });
                    }
                    catch { anyError = true; }
                }));
            }

            // Favorites
            if (Favorites.Count == 0)
            {
                 tasks.Add(System.Threading.Tasks.Task.Run(async () =>
                 {
                     try
                     {
                         var songs = await SubsonicService.Instance.GetStarred();
                         var limited = System.Linq.Enumerable.Take(songs, 20);
                         await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                         {
                             foreach (var s in limited) Favorites.Add(s);
                         });
                     }
                     catch { anyError = true; }
                 }));
            }

            // Recent Songs
            if (RecentItems.Count == 0)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(async () =>
                {
                     try
                     {
                         var songs = await SubsonicService.Instance.GetRecentSongs(0, 20);
                         var limited = System.Linq.Enumerable.Take(songs, 20);
                         
                         await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                         {
                             foreach (var s in limited) RecentItems.Add(s);
                         });
                     }
                     catch { anyError = true; }
                }));
            }

            if (tasks.Count > 0)
            {
                await System.Threading.Tasks.Task.WhenAll(tasks);
                
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (anyError && Favorites.Count == 0 && RecentItems.Count == 0 && RandomSongs.Count == 0)
                    {
                        IsOffline = true;
                        if (!_retryTimer.IsEnabled) _retryTimer.Start();
                    }
                    else
                    {
                        IsOffline = false;
                        _retryTimer.Stop();
                    }
                });
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            // Clear existing data to force reload
            Favorites.Clear();
            RecentItems.Clear();
            RandomSongs.Clear();
            
            await LoadHomeContent();
        }

        private void GenerateMockData()
        {
            // Deprecated
        }

        private void Favorites_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(FavoritesPage));
        }

        private void Recent_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(RecentSongsPage));
        }

        private void Random_Click(object sender, RoutedEventArgs e)
        {
             Frame.Navigate(typeof(RandomSongsPage), RandomSongs);
        }

        private void Song_ItemClick(object sender, ItemClickEventArgs e)
        {
             if (e.ClickedItem is SubsonicItem song)
             {
                var frame = Window.Current.Content as Frame;
                var mainPage = frame.Content as MainPage;
                
                System.Collections.Generic.IEnumerable<SubsonicItem> list = null;
                string name = "Now Playing";

                if (RandomSongs.Contains(song)) { list = RandomSongs; name = "Random Songs"; }
                else if (RecentItems.Contains(song)) { list = RecentItems; name = "Recently Added"; }
                else if (Favorites.Contains(song)) { list = Favorites; name = "Favorites"; }

                if (list != null)
                    mainPage?.PlayTrackList(list, song, name);
                else
                    mainPage?.PlayTrack(song);
             }
        }

        private void MediaItemGrid_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
             ShowContextMenu(sender);
             e.Handled = true;
        }

        private void MediaItemGrid_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                ShowContextMenu(sender);
                e.Handled = true;
            }
        }

        private void ShowContextMenu(object sender)
        {
             var fe = sender as FrameworkElement;
             var flyout = Windows.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(fe) as MenuFlyout;
             var item = fe?.DataContext as SubsonicItem;

             if (flyout != null && item != null)
             {
                 foreach (var baseItem in flyout.Items)
                 {
                     if (baseItem is MenuFlyoutItem mItem && (mItem.Text == "Favorite" || mItem.Text == "Unfavorite"))
                     {
                         mItem.Text = item.IsStarred ? "Unfavorite" : "Favorite";
                     }
                 }
                 Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(fe);
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

        private async void Favorite_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                  bool success = false;
                  if (item.IsStarred)
                      success = await SubsonicService.Instance.UnstarItem(item.Id);
                  else
                      success = await SubsonicService.Instance.StarItem(item.Id);

                  if (success)
                  {
                      item.IsStarred = !item.IsStarred;
                  }
             }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 await DownloadManager.StartDownload(item);
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

    }


}
