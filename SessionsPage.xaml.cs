using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class SessionsPage : Page
    {
        public ObservableCollection<SavedSession> Sessions { get; set; }

        public SessionsPage()
        {
            this.InitializeComponent();
            Sessions = new ObservableCollection<SavedSession>();
            this.DataContext = this;
            this.Loaded += SessionsPage_Loaded;
        }

        private async void SessionsPage_Loaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var list = await SessionManager.LoadSessions();
            Sessions.Clear();
            foreach (var s in list) Sessions.Add(s);
        }

        private void Session_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
             // Resume button handled separately via Click
             if ((sender as FrameworkElement)?.DataContext is SavedSession session)
             {
                 Frame.Navigate(typeof(PlaylistDetailsPage), session);
             }
        }

         private void Resume_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SavedSession session)
            {
                 var mp = (Windows.UI.Xaml.Window.Current.Content as Frame)?.Content as MainPage;
                 if (mp != null && session.Tracks.Count > 0)
                 {
                     // Pass ID to prevent duplication
                     // Calculate start item
                     SubsonicItem startItem = session.Tracks[0];
                     if (session.CurrentIndex >= 0 && session.CurrentIndex < session.Tracks.Count)
                     {
                         startItem = session.Tracks[session.CurrentIndex];
                     }
                     
                     mp.PlayTrackList(session.Tracks, startItem, session.Name, session.Id);
                     mp.RestoreSessionSettings(session); // Restore Shuffle/Repeat
                     
                     // Restore position (PlayTrackList will trigger PlayTrack async, so we might race default PlayTrack? 
                     // PlayTrackList uses CurrentQueueIndex setter which calls PlayTrack. 
                     // We can manually overwrite position after? Or update PlayTrackList. 
                     // Let's rely on StartItem for now to fix the "Start at beginning" annoyance.)
                 }
            }
        }



        private async void Delete_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SavedSession session)
            {
                 await SessionManager.DeleteSession(session);
                 Sessions.Remove(session);
            }
        }
        private void Button_SwallowTap(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
