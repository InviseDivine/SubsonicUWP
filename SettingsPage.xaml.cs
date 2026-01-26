using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
            LoadPinnedAlbums();
        }

        private void LoadSettings()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey("ServerUrl")) SubsonicService.Instance.ServerUrl = localSettings.Values["ServerUrl"] as string;
            if (localSettings.Values.ContainsKey("Username")) SubsonicService.Instance.Username = localSettings.Values["Username"] as string;
            if (localSettings.Values.ContainsKey("Password")) SubsonicService.Instance.Password = localSettings.Values["Password"] as string;

            ServerUrlBox.Text = SubsonicService.Instance.ServerUrl ?? "";
            UsernameBox.Text = SubsonicService.Instance.Username ?? "";
            PasswordBox.Password = SubsonicService.Instance.Password ?? "";

            // Load Theme
            string currentTheme = ThemeHelper.CurrentTheme;
            foreach (ComboBoxItem item in ThemeValidator.Items)
            {
                if (item.Tag.ToString() == currentTheme)
                {
                    ThemeValidator.SelectedItem = item;
                    break;
                }
            }
            
            // Load Mix Random Setting
            if (localSettings.Values.ContainsKey("MixRandomAlbums"))
            {
                MixRandomSwitch.IsOn = (bool)localSettings.Values["MixRandomAlbums"];
            }
        }

        private void MixRandomSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["MixRandomAlbums"] = MixRandomSwitch.IsOn;
        }

        private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             if (ThemeValidator.SelectedItem is ComboBoxItem item)
             {
                 string theme = item.Tag.ToString();
                 if (theme != ThemeHelper.CurrentTheme)
                 {
                     ThemeHelper.ApplyTheme(theme);
                 }
             }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            StatusBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Yellow);
            StatusBlock.Text = "Testing connection...";

            SubsonicService.Instance.ServerUrl = ServerUrlBox.Text;
            SubsonicService.Instance.Username = UsernameBox.Text;
            SubsonicService.Instance.Password = PasswordBox.Password;

            bool success = await SubsonicService.Instance.PingServer();

            if (success)
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["ServerUrl"] = SubsonicService.Instance.ServerUrl;
                localSettings.Values["Username"] = SubsonicService.Instance.Username;
                localSettings.Values["Password"] = SubsonicService.Instance.Password;

                StatusBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Green);
                StatusBlock.Text = "Connection successful! Settings saved.";
            }
            else
            {
                StatusBlock.Text = "Connection failed. Please check your details.";
            }
        }

        private async void RemovePin_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubsonicItem item)
            {
                await PinnedAlbumManager.UnpinAlbum(item.Id);
                await LoadPinnedAlbums();
            }
        }

        private async System.Threading.Tasks.Task LoadPinnedAlbums()
        {
            var list = await PinnedAlbumManager.GetPinnedAlbums();
            PinnedAlbumsList.ItemsSource = list.ToList();
        }
    }
}
