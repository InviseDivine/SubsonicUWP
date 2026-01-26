using System;
using Windows.UI.Xaml.Controls;

namespace SubsonicUWP
{
    public class NavItem
    {
        public string Label { get; set; }
        public string Symbol { get; set; } // MDLs 2 Asset char
        public string Tag { get; set; }
        public Type DestPage { get; set; }
    }
}
