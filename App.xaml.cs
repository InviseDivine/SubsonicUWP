using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SubsonicUWP
{
    sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
                ThemeHelper.Initialize();
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // Load Settings
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.ContainsKey("ServerUrl")) SubsonicService.Instance.ServerUrl = localSettings.Values["ServerUrl"] as string;
                    if (localSettings.Values.ContainsKey("Username")) SubsonicService.Instance.Username = localSettings.Values["Username"] as string;
                    if (localSettings.Values.ContainsKey("Password")) SubsonicService.Instance.Password = localSettings.Values["Password"] as string;

                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();
                
                // Pre-cache idle tile content in background
                Task.Run(() => TileManager.CleanupOldImages());
                Task.Run(() => TileManager.PrepareIdleCache());
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            
            try
            {
                // Ensure cache is fresh (or finish pending downloads)
                await TileManager.PrepareIdleCache();

                // Verify play state - if not playing, update tile with random albums
                var player = Windows.Media.Playback.BackgroundMediaPlayer.Current;
                if (player.CurrentState != Windows.Media.Playback.MediaPlayerState.Playing && 
                    player.CurrentState != Windows.Media.Playback.MediaPlayerState.Buffering)
                {
                     // Update immediately from pre-cached content
                     TileManager.UpdateFromCache();
                }
            }
            catch { }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
