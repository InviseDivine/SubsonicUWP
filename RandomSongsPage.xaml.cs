using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public sealed partial class RandomSongsPage : Page, System.ComponentModel.INotifyPropertyChanged
    {
        public IncrementalLoadingCollection<SubsonicItem> Songs { get; set; }
        
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set { _isOffline = value; OnPropertyChanged("IsOffline"); }
        }

        private DispatcherTimer _retryTimer;

        private System.Collections.Generic.HashSet<string> _loadedIds = new System.Collections.Generic.HashSet<string>();

        public RandomSongsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;
            InitializeCollection();
            this.DataContext = this;
            
            _retryTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(5) };
            _retryTimer.Tick += RetryTimer_Tick;
        }

        private void InitializeCollection()
        {
            Songs = new IncrementalLoadingCollection<SubsonicItem>(async (count) =>
            {
                try
                {
                    // Fetch more than needed to account for duplicates
                    var result = await SubsonicService.Instance.GetRandomSongs(50);
                    
                    if (IsOffline)
                    {
                        IsOffline = false;
                        _retryTimer.Stop();
                    }

                    // Deduplicate
                    var newItems = new System.Collections.Generic.List<SubsonicItem>();
                    if (result != null)
                    {
                        foreach (var item in result)
                        {
                            if (!_loadedIds.Contains(item.Id))
                            {
                                _loadedIds.Add(item.Id);
                                newItems.Add(item);
                            }
                        }
                    }
                    
                    // If we filtered out everything (unlikely but possible), return empty list or retry?
                    // Retrying recursively is dangerous. Better to return what we have.
                    // IncrementalLoadingCollection stops if count is 0.
                    // But for Random Songs, 0 means "all duplicates", not "end of list".
                    // Ideally we should loop until we get *something*, but let's start simple.
                    
                    return new ObservableCollection<SubsonicItem>(newItems);
                }
                catch
                {
                    IsOffline = true;
                    if (!_retryTimer.IsEnabled) _retryTimer.Start();
                    return null;
                }
            });
            OnPropertyChanged("Songs");
        }

        private void RetryTimer_Tick(object sender, object e)
        {
             if (Songs.Count == 0)
             {
                 InitializeCollection();
             }
             else
             {
                 IsOffline = false;
                 _retryTimer.Stop();
             }
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is ObservableCollection<SubsonicItem> passedSongs && passedSongs != null)
            {
                // Reset if passing new params
                _loadedIds.Clear();
                Songs.Clear(); // Should we clear or append? Usually param means "Show these".
                
                foreach (var song in passedSongs)
                {
                    if (!_loadedIds.Contains(song.Id))
                    {
                        _loadedIds.Add(song.Id);
                        Songs.Add(song);
                    }
                }
            }
        }

        private void RandomSongsPage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
             // No explicit load needed with IncrementalLoadingCollection usually,
             // or handled by initial binding.
        }

        private void GenerateMockData()
        {
             // Deprecated by real API
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

        private void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem song)
            {
                var frame = Window.Current.Content as Frame;
                var mainPage = frame.Content as MainPage;
                // Pass "Random Songs" as QueueName to trigger infinite queue logic in MainPage
                // And pass explicit list to avoid re-fetching
                mainPage?.PlayTrackList(Songs, song, "Random Songs");
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
                  }
             }
        }
    }
}
