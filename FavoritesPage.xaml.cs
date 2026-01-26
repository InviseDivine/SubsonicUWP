using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class FavoritesPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<SubsonicItem> Favorites { get; set; }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;

        public FavoritesPage()
        {
            this.InitializeComponent();
            Favorites = new ObservableCollection<SubsonicItem>();
            this.DataContext = this;
            this.Loaded += FavoritesPage_Loaded;
            
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private async void RetryTimer_Tick(object sender, object e)
        {
            await LoadFavorites();
        }

        private async void FavoritesPage_Loaded(object sender, RoutedEventArgs e)
        {
             await LoadFavorites();
        }

        private async System.Threading.Tasks.Task LoadFavorites()
        {
            try
            {
                Favorites.Clear();
                var songs = await SubsonicService.Instance.GetStarred();
                foreach (var s in songs) Favorites.Add(s);
                IsOffline = false;
                _retryTimer.Stop();
            }
            catch
            {
                IsOffline = true;
                if (!_retryTimer.IsEnabled) _retryTimer.Start();
            }
        }

        private void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
             if (e.ClickedItem is SubsonicItem song)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 mp?.PlayTrackList(Favorites, song, "Favorites");
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

        private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 mp?.ShowPlaylistPicker(item);
             }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 await DownloadManager.StartDownload(item);
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
                      // Remove from list if unstarred and we are in FavoritesPage
                      if (!item.IsStarred && Favorites.Contains(item))
                      {
                          Favorites.Remove(item);
                      }
                  }
             }
        }
        private void PlayAll_Click(object sender, RoutedEventArgs e)
        {
             if (Favorites.Count > 0)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 mp?.PlayTrackList(Favorites, Favorites[0], "Favorites");
             }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
             if (Favorites.Count > 0)
             {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 // Create a shuffled list or rely on MainPage shuffle? 
                 // For now, let's just shuffle locally and play
                 var list = new System.Collections.Generic.List<SubsonicItem>(Favorites);
                 var rng = new Random();
                 int n = list.Count;
                 while (n > 1) {
                     n--;
                     int k = rng.Next(n + 1);
                     var value = list[k];
                     list[k] = list[n];
                     list[n] = value;
                 }
                 var shuffled = new ObservableCollection<SubsonicItem>(list);
                 mp?.PlayTrackList(shuffled, shuffled[0], "Favorites (Shuffled)");
             }
        }
    }
}
