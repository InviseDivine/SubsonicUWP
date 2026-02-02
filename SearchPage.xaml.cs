using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;
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

        private async void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                
                // Check Artist
                if (Results != null && Results.Artists != null && Results.Artists.Contains(item))
                {
                     var songs = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                     if (songs.Count > 0) mp?.AddToQueue(songs);
                }
                // Check Album
                else if (Results != null && Results.Albums != null && Results.Albums.Contains(item))
                {
                    var tracks = await SubsonicService.Instance.GetAlbum(item.Id);
                    if (tracks.Count > 0) mp?.AddToQueue(tracks);
                }
                // Song
                else
                {
                    mp?.AddToQueue(item);
                }
            }
        }

        private async void PlayNext_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Window.Current.Content as Frame)?.Content as MainPage;
                
                // Check Artist
                if (Results != null && Results.Artists != null && Results.Artists.Contains(item))
                {
                     var songs = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                     if (songs.Count > 0) mp?.PlayNext(songs);
                }
                // Check Album
                else if (Results != null && Results.Albums != null && Results.Albums.Contains(item))
                {
                    var tracks = await SubsonicService.Instance.GetAlbum(item.Id);
                    if (tracks.Count > 0) mp?.PlayNext(tracks);
                }
                // Song
                else
                {
                    mp?.PlayNext(item);
                }
            }
        }

        private async void AddToCache_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 System.Collections.Generic.List<SubsonicItem> songsToCache = null;

                 // Check Artist
                 if (Results != null && Results.Artists != null && Results.Artists.Contains(item))
                 {
                     songsToCache = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                 }
                 // Check Album
                 else if (Results != null && Results.Albums != null && Results.Albums.Contains(item))
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(item.Id);
                     songsToCache = new System.Collections.Generic.List<SubsonicItem>(tracks);
                     foreach(var t in songsToCache)
                     {
                         if (string.IsNullOrEmpty(t.Artist)) t.Artist = item.Artist;
                         if (string.IsNullOrEmpty(t.Album)) t.Album = item.Title;
                     }
                 }
                 // Song
                 else
                 {
                     Services.PlaybackService.Instance.EnqueueDownload(item, isTransient: false);
                     return;
                 }

                 if (songsToCache != null && songsToCache.Count > 0)
                 {
                      if (songsToCache.Count > 25)
                      {
                          var dialog = new Windows.UI.Popups.MessageDialog($"Are you sure that you want to cache {songsToCache.Count} songs?", "Confirm Cache");
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes"));
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("No"));
                          var result = await dialog.ShowAsync();
                          if (result.Label != "Yes") return;
                      }
                      Services.PlaybackService.Instance.EnqueueDownloads(songsToCache, isTransient: false);
                 }
             }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 System.Collections.Generic.List<SubsonicItem> songsToExport = null;

                 // Check Artist
                 if (Results != null && Results.Artists != null && Results.Artists.Contains(item))
                 {
                     songsToExport = await SubsonicService.Instance.GetAllArtistSongs(item.Id);
                 }
                 // Check Album
                 else if (Results != null && Results.Albums != null && Results.Albums.Contains(item))
                 {
                     var tracks = await SubsonicService.Instance.GetAlbum(item.Id);
                     songsToExport = new System.Collections.Generic.List<SubsonicItem>(tracks);
                     foreach(var t in songsToExport)
                     {
                         if (string.IsNullOrEmpty(t.Artist)) t.Artist = item.Artist;
                         if (string.IsNullOrEmpty(t.Album)) t.Album = item.Title;
                     }
                 }
                 // Song
                 else
                 {
                     await DownloadManager.StartDownload(item);
                     return;
                 }

                 if (songsToExport != null && songsToExport.Count > 0)
                 {
                      if (songsToExport.Count > 25)
                      {
                          var dialog = new Windows.UI.Popups.MessageDialog($"Are you sure that you want to export {songsToExport.Count} songs?", "Confirm Export");
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes"));
                          dialog.Commands.Add(new Windows.UI.Popups.UICommand("No"));
                          var result = await dialog.ShowAsync();
                          if (result.Label != "Yes") return;
                      }
                      else if (songsToExport.Count > 1)
                      {
                          var dialog = new Windows.UI.Popups.MessageDialog($"Starting export for {songsToExport.Count} songs...", "Export");
                          await dialog.ShowAsync();
                      }
                      
                      foreach(var t in songsToExport)
                      {
                          await DownloadManager.StartDownload(t);
                      }
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
