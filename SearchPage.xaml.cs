using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace SubsonicUWP
{
    public sealed partial class SearchPage : Page
    {
        public SearchResult Results { get; set; } = new SearchResult();
        
        public Visibility SongsVisibility => Results != null && Results.Songs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AlbumsVisibility => Results != null && Results.Albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ArtistsVisibility => Results != null && Results.Artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public SearchPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
            {
                PageSearchBox.Text = query;
                PerformSearch(query);
            }
        }

        private void PageSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(sender.Text))
            {
                PerformSearch(sender.Text);
            }
        }

        private async void PerformSearch(string query)
        {
            TitleBlock.Text = $"Search: {query}";
            TitleBlock.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Visible;
            
            try
            {
                Results = await SubsonicService.Instance.Search(query);
                this.DataContext = null;
                this.DataContext = this; 
            }
            catch { }
            
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        
        private void Song_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem song)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                mp?.PlayTrackList(Results.Songs, song, "Search Results");
            }
        }

        private void Album_ItemClick(object sender, ItemClickEventArgs e)
        {
             if (e.ClickedItem is SubsonicItem album)
             {
                 Frame.Navigate(typeof(AlbumDetailsPage), album);
             }
        }

        private void Artist_ItemClick(object sender, ItemClickEventArgs e)
        {
             if (e.ClickedItem is SubsonicItem artist)
             {
                 Frame.Navigate(typeof(ArtistDetailsPage), artist.Id);
             }
        }

        // Context Menu Handlers (Copied from FavoritesPage/HomePage)
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
             var flyout = FlyoutBase.GetAttachedFlyout(fe) as MenuFlyout;
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
                 FlyoutBase.ShowAttachedFlyout(fe);
             }
        }

        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                mp?.AddToQueue(item);
            }
        }

        private void PlayNext_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                mp?.PlayNext(item);
            }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 // Check if it's an Album
                 if (Results != null && Results.Albums != null && Results.Albums.Contains(item))
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(item.Id);
                     if (tracks.Count > 0)
                     {
                         var dialog = new Windows.UI.Popups.MessageDialog($"Starting download for {tracks.Count} tracks...", "Download Album");
                         await dialog.ShowAsync();
                         foreach(var t in tracks)
                         {
                             if (string.IsNullOrEmpty(t.Artist)) t.Artist = item.Artist;
                             if (string.IsNullOrEmpty(t.Album)) t.Album = item.Title;
                             await DownloadManager.StartDownload(t);
                         }
                     }
                 }
                 else
                 {
                     // Single Song (or Artist, but Artist download usually not supported nicely this way)
                     await DownloadManager.StartDownload(item);
                 }
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
                      // Refresh binding/context menu will update on next open
                  }
             }
        }
    }
}
