using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class ExtendedSplashPage : Page
    {
        private LaunchActivatedEventArgs _launchArgs;

        public ExtendedSplashPage(LaunchActivatedEventArgs e)
        {
            this.InitializeComponent();
            _launchArgs = e;
            this.Loaded += ExtendedSplashPage_Loaded;
        }

        public ExtendedSplashPage()
        {
            this.InitializeComponent();
            this.Loaded += ExtendedSplashPage_Loaded;
        }

        private async void ExtendedSplashPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Simulate loading delay or initialize real data here
            await Task.Delay(2000); // 2 seconds of animation
            
            // Navigate to Main Page
            Frame.Navigate(typeof(MainPage), _launchArgs?.Arguments);
            
            // Clear backstack so user can't go back to splash
            Frame.BackStack.Clear();
        }
    }
}
