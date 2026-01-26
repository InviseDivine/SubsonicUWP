using System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    public static class ThemeHelper
    {
        public static string CurrentTheme { get; private set; } = "System";

        public static void Initialize()
        {
            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.ContainsKey("ThemePreference"))
            {
                string storedTheme = Windows.Storage.ApplicationData.Current.LocalSettings.Values["ThemePreference"] as string;
                ApplyTheme(storedTheme);
            }
            else
            {
                ApplyTheme("System");
            }
        }

        public static void ApplyTheme(string themeString)
        {
            CurrentTheme = themeString;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["ThemePreference"] = themeString;

            if (Window.Current.Content is FrameworkElement root)
            {
                switch (themeString)
                {
                    case "Light":
                        root.RequestedTheme = ElementTheme.Light;
                        RestoreDarkResources(); // Ensure Dark isn't AMOLED
                        break;
                    case "Dark":
                        root.RequestedTheme = ElementTheme.Dark;
                        RestoreDarkResources();
                        break;
                    case "AMOLED":
                        root.RequestedTheme = ElementTheme.Dark;
                        ApplyAmoledResources();
                        break;
                    case "System":
                    default:
                        root.RequestedTheme = ElementTheme.Default;
                        // For system, we theoretically need to know if system is Dark to apply AMOLED, 
                        // but System usually implies standard Dark. 
                        // If user wants AMOLED, they usually stick to "AMOLED" mode.
                        // So for System we just restore standard Dark.
                        RestoreDarkResources(); 
                        break;
                }
            }
            
            UpdateTitleBar();
        }

        private static void ApplyAmoledResources()
        {
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Dark", out object darkDictObj) && darkDictObj is ResourceDictionary darkDict)
            {
                // Helper to update brush color if it exists
                void SetBrushColor(string key, Color color)
                {
                    if (darkDict.TryGetValue(key, out object brushObj) && brushObj is SolidColorBrush brush)
                    {
                        brush.Color = color;
                    }
                }

                SetBrushColor("WindowBackgroundBrush", Colors.Black);
                SetBrushColor("ControlBackgroundBrush", Colors.Black);
                SetBrushColor("NavRailBackgroundBrush", Colors.Black);
                SetBrushColor("PlayerBarBackgroundBrush", Colors.Black);
            }
        }

        private static void RestoreDarkResources()
        {
             // Restore #121212 and #1F1F1F
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Dark", out object darkDictObj) && darkDictObj is ResourceDictionary darkDict)
            {
                void SetBrushColor(string key, Color color)
                {
                    if (darkDict.TryGetValue(key, out object brushObj) && brushObj is SolidColorBrush brush)
                    {
                        brush.Color = color;
                    }
                }

                SetBrushColor("WindowBackgroundBrush", Color.FromArgb(255, 18, 18, 18));
                SetBrushColor("ControlBackgroundBrush", Color.FromArgb(255, 31, 31, 31));
                SetBrushColor("NavRailBackgroundBrush", Color.FromArgb(255, 18, 18, 18));
                SetBrushColor("PlayerBarBackgroundBrush", Color.FromArgb(255, 23, 23, 23));
            }
        }

        public static void UpdateTitleBar()
        {
            var view = ApplicationView.GetForCurrentView();
            var titleBar = view.TitleBar;
            
            // Adjust titlebar colors if custom titlebar is used, but for now just standard
            // This might need more work if we have a custom drag region.
            // For now, let's assume system handles it based on RequestedTheme, 
            // except AMOLED might need manual tweak if titlebar is visible.
        }
    }
}
