using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SubsonicUWP
{
    public sealed partial class NowPlayingPhonePage : Page
    {
        public NowPlayingPhonePage()
        {
            this.InitializeComponent();
        }

        private void UpdateRepeatIcon(MainPage mp)
        {
             // Need to access Button from XAML? We can't easily binding color without converters.
             // Let's just use the Click handler to trigger update on MainPage, implies MP handles state.
             // But we need to update THIS page's icons.
             // Let's just bind text/color? No, explicit update.
             // Simplest: Button Click -> mp.Toggle... -> Update Local Icons.
             
             // Implementation:
             // We'll rely on property change notification or manual sync.
             // For now, let's just make the buttons work.
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            var mp = this.DataContext as MainPage;
            mp?.ToggleShuffle();
            UpdatePhoneIcons(mp);
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            var mp = this.DataContext as MainPage;
            mp?.ToggleRepeat();
            UpdatePhoneIcons(mp);
        }

        private void UpdatePhoneIcons(MainPage mp)
        {
             if (mp == null) return;
             
             if (ShuffleButton != null)
             {
                 ShuffleButton.Foreground = mp.IsShuffle ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.OrangeRed) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
                 if (ShuffleStrike != null)
                 {
                     ShuffleStrike.Visibility = mp.IsShuffle ? Visibility.Collapsed : Visibility.Visible;
                 }
             }
             
             if (RepeatButton != null && RepeatButton.Content is TextBlock tb)
             {
                 var brush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.OrangeRed);
                 var gray = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
    
                 if (mp.CurrentRepeatMode == RepeatMode.Off)
                 {
                     RepeatButton.Foreground = gray;
                     tb.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                     tb.Text = "\u2192";
                     tb.FontSize = 24;
                 }
                 else if (mp.CurrentRepeatMode == RepeatMode.All)
                 {
                     RepeatButton.Foreground = brush;
                     tb.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
                     tb.Text = "\uE895";
                     tb.FontSize = 18;
                 }
                 else // One
                 {
                     RepeatButton.Foreground = brush;
                     tb.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
                     tb.Text = "\uE8ED";
                     tb.FontSize = 18;
                 }
             }
        }
        
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
             var mp = (Window.Current.Content as Frame)?.Content as MainPage;
             base.OnNavigatedTo(e); 
             
             if (mp != null)
             {
                 this.DataContext = mp;
                 UpdatePlayIcon(mp.GlobalMediaElement.CurrentState == Windows.UI.Xaml.Media.MediaElementState.Playing);
                 mp.GlobalMediaElement.CurrentStateChanged += GlobalMediaElement_CurrentStateChanged;
                 UpdatePhoneIcons(mp); 
                 UpdateFavoriteIcon(mp);
             }
             
             TimeSlider.AddHandler(UIElement.PointerPressedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerPressed), true);
             TimeSlider.AddHandler(UIElement.PointerReleasedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerReleased), true);
        }
        
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp != null)
             {
                 mp.GlobalMediaElement.CurrentStateChanged -= GlobalMediaElement_CurrentStateChanged;
             }
             
             TimeSlider.RemoveHandler(UIElement.PointerPressedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerPressed));
             TimeSlider.RemoveHandler(UIElement.PointerReleasedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerReleased));
             
             base.OnNavigatedFrom(e);
        }

        private void GlobalMediaElement_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp != null)
             {
                 UpdatePlayIcon(mp.GlobalMediaElement.CurrentState == Windows.UI.Xaml.Media.MediaElementState.Playing);
             }
        }

        private void UpdatePlayIcon(bool isPlaying)
        {
             if (PlayPauseIcon == null) return;
             PlayPauseIcon.Text = isPlaying ? "\uE769" : "\uE768"; 
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp != null)
             {
                 if (mp.GlobalMediaElement.CurrentState == Windows.UI.Xaml.Media.MediaElementState.Playing)
                     mp.GlobalMediaElement.Pause();
                 else
                     mp.GlobalMediaElement.Play();
             }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             mp?.Prev_Click_Public();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             mp?.Next_Click_Public();
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            var mp = this.DataContext as MainPage;
            mp?.ToggleFavorite();
            UpdateFavoriteIcon(mp);
        }

        private void Queue_Click(object sender, RoutedEventArgs e)
        {
             // Navigate global frame to Queue Page (or Sessions, or similar)
             // Usually this is navigating the Main Frame.
             // If NowPlaying is a full page, navigating away replaces it.
             var mp = (Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null)
             {
                 // User asked for "Current Queue".
                 // QueuePage displays the current PlaybackQueue.
                 mp.NavigateTo(typeof(QueuePage));
             }
        }
        
        private void More_Click(object sender, RoutedEventArgs e)
        {
             // Flyout is attached in XAML, so it opens automatically.
        }

        private void TimeSlider_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var mp = this.DataContext as MainPage;
            mp?.SetDragging(true);
        }

        private void TimeSlider_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var mp = this.DataContext as MainPage;
            mp?.SetDragging(false);
            if (sender is Slider slider && mp != null) mp.Seek(slider.Value); 
        }

        private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp?.CurrentSong != null)
             {
                 mp.ShowPlaylistPicker(mp.CurrentSong);
             }
        }

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp?.CurrentSong != null)
             {
                 await DownloadManager.StartDownload(mp.CurrentSong);
             }
        }

        private void UpdateFavoriteIcon(MainPage mp)
        {
             if (mp == null || mp.CurrentSong == null || FavoriteIcon == null) return;
             // We need to know if it's starred.
             // mp.CurrentSong.Starred -> but checks if property exists.
             // Assuming Binding handles it in UI usually? 
             // In MainPage it was: Text="{Binding CurrentSong.Starred, Converter ...}"
             // Here we are in code behind.
             // Let's try to grab Starred state.
             // Ideally we bind Text/Icon in XAML.
             // But if we want to do it manually:
             FavoriteIcon.Text = mp.CurrentSong.IsStarred ? "\uE735" : "\uE734"; // Solid Star : Outline Star
             FavoriteIcon.Text = mp.CurrentSong.IsStarred ? "\uE735" : "\uE734"; // Solid Star : Outline Star
             if (mp.CurrentSong.IsStarred)
             {
                  if (Application.Current.Resources.TryGetValue("ForegroundBrush", out object brush))
                      FavoriteIcon.Foreground = brush as Windows.UI.Xaml.Media.SolidColorBrush;
                  else
                      FavoriteIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
             }
             else
             {
                  FavoriteIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,136,136,136));
             }
        }

        private async void GoToAlbum_Click(object sender, RoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp?.CurrentSong == null) return;

             string albumId = mp.CurrentSong.AlbumId;

             if (string.IsNullOrEmpty(albumId))
             {
                  // Repair
                  var fullSong = await SubsonicService.Instance.GetSong(mp.CurrentSong.Id);
                  if (fullSong != null && !string.IsNullOrEmpty(fullSong.AlbumId))
                  {
                      mp.CurrentSong.ArtistId = fullSong.ArtistId;
                      mp.CurrentSong.AlbumId = fullSong.AlbumId;
                      albumId = fullSong.AlbumId;
                  }
             }

             if (!string.IsNullOrEmpty(albumId))
             {
                 Frame.Navigate(typeof(AlbumDetailsPage), albumId);
             }
             else
             {
                 var dialog = new Windows.UI.Popups.MessageDialog("Album info unavailable. Please play a new song to refresh data.");
                 await dialog.ShowAsync();
             }
        }

        private async void Artist_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
             var mp = this.DataContext as MainPage;
             if (mp?.CurrentSong == null) return;

             string artistId = mp.CurrentSong.ArtistId;

             if (string.IsNullOrEmpty(artistId))
             {
                  var fullSong = await SubsonicService.Instance.GetSong(mp.CurrentSong.Id);
                  if (fullSong != null && !string.IsNullOrEmpty(fullSong.ArtistId))
                  {
                      mp.CurrentSong.ArtistId = fullSong.ArtistId;
                      mp.CurrentSong.AlbumId = fullSong.AlbumId;
                      artistId = fullSong.ArtistId;
                  }
             }

             if (!string.IsNullOrEmpty(artistId))
             {
                 Frame.Navigate(typeof(ArtistDetailsPage), artistId);
             }
             else
             {
                 var dialog = new Windows.UI.Popups.MessageDialog("Artist info unavailable. Please play a new song to refresh data.");
                 await dialog.ShowAsync();
             }
        }
    }
}
