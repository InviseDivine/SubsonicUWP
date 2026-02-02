using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class NowPlayingPage : Page
    {
        public NowPlayingPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null)
             {
                 this.DataContext = mp;
             }
             base.OnNavigatedTo(e);
             
             TimeSlider.AddHandler(UIElement.PointerPressedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerPressed), true);
             TimeSlider.AddHandler(UIElement.PointerReleasedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerReleased), true);
        }

        protected override void OnNavigatedFrom(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            TimeSlider.RemoveHandler(UIElement.PointerPressedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerPressed));
            TimeSlider.RemoveHandler(UIElement.PointerReleasedEvent, new Windows.UI.Xaml.Input.PointerEventHandler(TimeSlider_PointerReleased));
            base.OnNavigatedFrom(e);
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
            // Force sync on release
            if (sender is Slider slider) mp?.Seek(slider.Value); 
        }

        private void QueueList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                if (mp != null)
                {
                    int newIndex = mp.PlaybackQueue.IndexOf(item);
                    if (newIndex != -1)
                    {
                         mp.CurrentQueueIndex = newIndex;
                         mp.PlayTrack(item);
                    }
                }
            }
        }

        // Context Menu Handlers
        private void QueueItemGrid_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
             ShowContextMenu(sender);
             e.Handled = true;
        }

        private void QueueItemGrid_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
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
                     if (baseItem is MenuFlyoutItem mItem)
                     {
                         if (mItem.Text == "Favorite" || mItem.Text == "Unfavorite")
                             mItem.Text = item.IsStarred ? "Unfavorite" : "Favorite";
                         
                         if (mItem.Text == "Cache Permanently")
                         {
                             mItem.Visibility = SubsonicService.Instance.ManualCacheMode ? Visibility.Visible : Visibility.Collapsed;
                         }
                     }
                 }
                 Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(fe);
             }
        }

        private void PlayNow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                if (mp != null)
                {
                    int index = mp.PlaybackQueue.IndexOf(item);
                    if (index >= 0)
                    {
                        if (mp.CurrentQueueIndex == index) mp.PlayTrack(item);
                        else mp.CurrentQueueIndex = index;
                    }
                }
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

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.RemoveFromQueue(item);
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

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 await DownloadManager.StartDownload(item);
             }
        }

        private async void CachePermanently_Click(object sender, RoutedEventArgs e)
        {
             if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
             {
                 await Services.PlaybackService.Instance.PromoteTransient(item.Id);
             }
        }
    }
}
