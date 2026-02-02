using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public sealed partial class QueuePage : Page
    {
        public QueuePage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;
        }

        protected override void OnNavigatedTo(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null)
             {
                 this.DataContext = mp;
                 // Scroll to current song with Leading alignment (Top)
                 if (mp.CurrentQueueIndex >= 0 && mp.CurrentQueueIndex < mp.PlaybackQueue.Count)
                 {
                     var item = mp.PlaybackQueue[mp.CurrentQueueIndex];
                     // Execute on UI Update to ensure list is ready
                     _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                     {
                        try
                        {
                            await Task.Delay(200); // Give ListView time to bind and measure
                            if (QueueList.Items.Count > 0 && mp.PlaybackQueue.Contains(item))
                            {
                                QueueList.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                            }
                        }
                        catch { }
                     });
                 }
                 mp.PropertyChanged += MainPage_PropertyChanged;
             }
             base.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(Windows.UI.Xaml.Navigation.NavigationEventArgs e)
        {
             base.OnNavigatedFrom(e);
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null)
             {
                 mp.PropertyChanged -= MainPage_PropertyChanged;
             }
        }

        private void MainPage_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
             if (e.PropertyName == "CurrentQueueIndex")
             {
                 ScrollToCurrent();
             }
        }

        private void ScrollToCurrent()
        {
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null && mp.CurrentQueueIndex >= 0 && mp.CurrentQueueIndex < mp.PlaybackQueue.Count)
             {
                 var item = mp.PlaybackQueue[mp.CurrentQueueIndex];
                 _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                 {
                    try
                    {
                        // await Task.Delay(100); // Small delay not strictly needed if list is populated, but safer
                        if (QueueList.Items.Count > 0 && mp.PlaybackQueue.Contains(item))
                        {
                            QueueList.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                        }
                    }
                    catch { }
                 });
             }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
            mp?.ClearQueue();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                mp?.RemoveFromQueue(item);
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

        private async void SaveSession_Click(object sender, RoutedEventArgs e)
        {
             var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
             if (mp != null && mp.PlaybackQueue.Count > 0)
             {
                 bool success = await SubsonicService.Instance.CreatePlaylist(mp.QueueName, System.Linq.Enumerable.Select(mp.PlaybackQueue, t => t.Id));
                 
                 var dialog = new ContentDialog 
                 { 
                     Title = success ? "Playlist Created" : "Error", 
                     Content = success ? $"Saved as server playlist '{mp.QueueName}'" : "Failed to create playlist.", 
                     PrimaryButtonText = "OK" 
                 };
                 await dialog.ShowAsync();
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
                     if (baseItem is MenuFlyoutItem mItem && (mItem.Text == "Favorite" || mItem.Text == "Unfavorite"))
                     {
                         mItem.Text = item.IsStarred ? "Unfavorite" : "Favorite";
                     }
                 }
                 Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(fe);
             }
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


        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            NameBox.Focus(FocusState.Programmatic);
            NameBox.SelectAll();
        }

        private void NameBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                this.Focus(FocusState.Programmatic); // Triggers LostFocus
            }
        }
        
        private void NameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Binding handles update
        }

        private void Item_ManipulationDelta(object sender, Windows.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            var contentGrid = sender as Grid;
            if (contentGrid != null && contentGrid.RenderTransform is CompositeTransform transform)
            {
                transform.TranslateX += e.Delta.Translation.X;

                // Restrict functionality for Current Song
                if (contentGrid.DataContext is SubsonicItem item)
                {
                    var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                    if (mp != null && mp.CurrentSong == item)
                    {
                        // Block Swipe Right (Remove)
                        if (transform.TranslateX > 0) transform.TranslateX = 0;
                    }
                }

                // Find Backdrop
                var parent = VisualTreeHelper.GetParent(contentGrid) as Grid;
                if (parent != null)
                {
                     var backdrop = parent.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "BackdropGrid");
                     if (backdrop != null)
                     {
                         var removeBack = backdrop.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "RemoveBackdrop");
                         var playNextBack = backdrop.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "PlayNextBackdrop");

                         if (transform.TranslateX > 0)
                         {
                             // Swiping Right -> Remove (Show Left Backbone)
                             if (removeBack != null) removeBack.Visibility = Visibility.Visible;
                             if (playNextBack != null) playNextBack.Visibility = Visibility.Collapsed;
                             backdrop.Opacity = Math.Min(1.0, Math.Abs(transform.TranslateX) / 100.0);
                         }
                         else
                         {
                             // Swiping Left -> Play Next (Show Right Backbone)
                             if (removeBack != null) removeBack.Visibility = Visibility.Collapsed;
                             if (playNextBack != null) playNextBack.Visibility = Visibility.Visible;
                             backdrop.Opacity = Math.Min(1.0, Math.Abs(transform.TranslateX) / 100.0);
                         }
                     }
                }
            }
        }

        private void Item_ManipulationCompleted(object sender, Windows.UI.Xaml.Input.ManipulationCompletedRoutedEventArgs e)
        {
            var contentGrid = sender as Grid;
            if (contentGrid != null && contentGrid.RenderTransform is CompositeTransform transform)
            {
                var item = contentGrid.DataContext as SubsonicItem;
                var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;

                if (Math.Abs(transform.TranslateX) > 100 && item != null && mp != null)
                {
                    // Action Triggered
                    if (transform.TranslateX > 0)
                    {
                        // Remove - Swipe Right
                        AnimateSlideOut(transform, 500, () => 
                        {
                            mp.RemoveFromQueue(item);
                            ResetBackdrop(contentGrid);
                        });
                    }
                    else
                    {
                        // Play Next - Swipe Left
                        // User prefers "Snap Back" style (Spring loaded) instead of Slide Out for this action
                        AnimateSnapBack(transform, () => 
                        {
                            mp.PlayNext(item);
                            ResetBackdrop(contentGrid);
                        });
                    }
                }
                else
                {
                    // Snap back
                    AnimateSnapBack(transform, () => ResetBackdrop(contentGrid));
                }
            }
        }

        private void ResetBackdrop(Grid contentGrid)
        {
            var parent = VisualTreeHelper.GetParent(contentGrid) as Grid;
            if (parent != null)
            {
                 var backdrop = parent.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "BackdropGrid");
                 if (backdrop != null)
                 {
                     var removeBack = backdrop.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "RemoveBackdrop");
                     var playNextBack = backdrop.Children.OfType<Grid>().FirstOrDefault(c => c.Name == "PlayNextBackdrop");
                     if (removeBack != null) removeBack.Visibility = Visibility.Collapsed;
                     if (playNextBack != null) playNextBack.Visibility = Visibility.Collapsed;
                 }
            }
        }

        private void AnimateSnapBack(CompositeTransform transform, Action onCompleted = null)
        {
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var da = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new Windows.UI.Xaml.Media.Animation.ExponentialEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, transform);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "TranslateX");
            sb.Children.Add(da);
            sb.Completed += (s, e) => onCompleted?.Invoke();
            sb.Begin();
        }

        private void AnimateSlideOut(CompositeTransform transform, double toX, Action onCompleted)
        {
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var da = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = toX,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new Windows.UI.Xaml.Media.Animation.ExponentialEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseIn }
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, transform);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "TranslateX");
            sb.Children.Add(da);
            
            sb.Completed += (s, e) =>
            {
                 onCompleted?.Invoke();
                 // Reset transform for container reuse
                 transform.TranslateX = 0;
            };
            sb.Begin();
        }
    }
}
