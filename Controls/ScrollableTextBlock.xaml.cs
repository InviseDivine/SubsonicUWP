using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace SubsonicUWP.Controls
{
    public sealed partial class ScrollableTextBlock : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(ScrollableTextBlock), new PropertyMetadata("", OnTextChanged));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }
        
        // Expose styling properties to make usage easier
        public new static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register("FontSize", typeof(double), typeof(ScrollableTextBlock), new PropertyMetadata(14.0, OnAppearanceChanged));
        public new double FontSize 
        { 
            get => (double)GetValue(FontSizeProperty); 
            set => SetValue(FontSizeProperty, value); 
        }

        public new static readonly DependencyProperty FontWeightProperty =
            DependencyProperty.Register("FontWeight", typeof(Windows.UI.Text.FontWeight), typeof(ScrollableTextBlock), new PropertyMetadata(Windows.UI.Text.FontWeights.Normal, OnAppearanceChanged));
        public new Windows.UI.Text.FontWeight FontWeight 
        { 
             get => (Windows.UI.Text.FontWeight)GetValue(FontWeightProperty); 
             set => SetValue(FontWeightProperty, value); 
        }
        
        public new static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(ScrollableTextBlock), new PropertyMetadata(new SolidColorBrush(Windows.UI.Colors.Black), OnAppearanceChanged));
        public new Brush Foreground
        {
             get => (Brush)GetValue(ForegroundProperty);
             set => SetValue(ForegroundProperty, value);
        }

        private Storyboard _scrollInfo;

        public ScrollableTextBlock()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => CheckMarquee();
            this.Unloaded += (s, e) => StopMarquee();
            
            this.PointerEntered += (s, e) =>
            {
                if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse || 
                    e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
                {
                    _scrollInfo?.Pause();
                }
            };

            this.PointerExited += (s, e) =>
            {
                if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse || 
                    e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
                {
                    _scrollInfo?.Resume();
                }
            };
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
             if (d is ScrollableTextBlock stb) 
             {
                 string raw = e.NewValue as string ?? "";
                 
                 // 1. Setup Placeholder (Props open the Grid)
                 if (stb.Placeholder != null) stb.Placeholder.Text = raw;

                 // 2. Setup Real Text with Anchor (For accurate measuring/rendering)
                 stb.TextElement.Inlines.Clear();
                 stb.TextElement.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = raw });
                 
                 // Anchor: Spaces + Dot.
                 var anchor = new Windows.UI.Xaml.Documents.Run { Text = "     .", Foreground = new SolidColorBrush(Windows.UI.Colors.Transparent) };
                 stb.TextElement.Inlines.Add(anchor);
                 
                 stb.CheckMarquee();
             }
        }
        
        public new static readonly DependencyProperty FontFamilyProperty = 
            DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(ScrollableTextBlock), new PropertyMetadata(new FontFamily("Segoe UI"), OnAppearanceChanged));
        public new FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
             if (d is ScrollableTextBlock stb) 
             {
                 // Sync Real
                 stb.TextElement.FontSize = stb.FontSize;
                 stb.TextElement.FontWeight = stb.FontWeight;
                 stb.TextElement.Foreground = stb.Foreground;
                 stb.TextElement.FontFamily = stb.FontFamily;
                 
                 // Sync Placeholder
                 if (stb.Placeholder != null)
                 {
                     stb.Placeholder.FontSize = stb.FontSize;
                     stb.Placeholder.FontWeight = stb.FontWeight;
                     stb.Placeholder.Foreground = stb.Foreground; // Color doesn't matter (0 opacity) but keeps it consistent
                     stb.Placeholder.FontFamily = stb.FontFamily;
                 }
                 
                 stb.CheckMarquee();
             }
        }

        private void ContainerGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
             // Clip to bounds
             var rect = new Windows.UI.Xaml.Media.RectangleGeometry();
             rect.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
             ContainerGrid.Clip = rect;
             
             CheckMarquee();
        }

        private void CheckMarquee()
        {
             StopMarquee();

             _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
             {
                 if (Placeholder == null) return;

                 // 1. Reset to Pure Text (No Anchor)
                 TextElement.Inlines.Clear();
                 TextElement.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = Text });

                 // Sync properties
                 TextElement.FontSize = FontSize;
                 Placeholder.FontSize = FontSize;
                 
                 TextElement.FontWeight = FontWeight;
                 TextElement.Foreground = Foreground;
                 TextElement.FontFamily = FontFamily;
                 TextElement.TextTrimming = TextTrimming.None;
                 
                 TextElement.Margin = new Thickness(0);
                 TextElement.Width = double.NaN;
                 
                 this.UpdateLayout();

                 // 2. Measure Pure Text
                 TextElement.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                 double textWidth = TextElement.DesiredSize.Width; 
                 double textHeight = TextElement.DesiredSize.Height;
                 
                 double containerWidth = ContainerGrid.ActualWidth;
                 double containerHeight = ContainerGrid.ActualHeight;
                 
                 // Vertically Center
                 if (containerHeight > 0)
                 {
                     Canvas.SetTop(TextElement, -textHeight / 2);
                 }
                 
                 // 3. Logic: Only scroll if PURE text is wider than container
                 if (containerWidth > 0 && textWidth > containerWidth)
                 {
                     // Add Anchor for padding/rendering safety during scroll
                     var anchor = new Windows.UI.Xaml.Documents.Run { Text = "     .", Foreground = new SolidColorBrush(Windows.UI.Colors.Transparent) };
                     TextElement.Inlines.Add(anchor);
                     
                     // Helper: Re-measure to get full scroll width
                     this.UpdateLayout();
                     TextElement.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                     double fullWidth = TextElement.DesiredSize.Width;

                     // Force Width
                     TextElement.Width = fullWidth;
                     StartMarquee(fullWidth, containerWidth);
                 }
                 else
                 {
                     // Fits! No anchor needed.
                     TextTransform.X = 0;
                     Canvas.SetLeft(TextElement, 0); 
                 }
             });
        }

        private void StartMarquee(double textWidth, double containerWidth)
        {
             if (_scrollInfo != null) _scrollInfo.Stop();

             _scrollInfo = new Storyboard();
             _scrollInfo.RepeatBehavior = RepeatBehavior.Forever;

             // Scroll from 0 to -(textWidth - containerWidth)
             double targetX = containerWidth - textWidth; 
             double distance = Math.Abs(targetX);
             double timeToScroll = distance / 30.0; 
             if (timeToScroll < 1) timeToScroll = 1;
             
             var anim = new DoubleAnimationUsingKeyFrames();
             
             anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)) });
             anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)) });
             anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = targetX, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + timeToScroll)) });
             anim.KeyFrames.Add(new LinearDoubleKeyFrame { Value = targetX, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + timeToScroll + 2)) });
             
             Storyboard.SetTarget(anim, TextTransform);
             Storyboard.SetTargetProperty(anim, "X");
             
             _scrollInfo.Children.Add(anim);
             _scrollInfo.Begin();
        }

        private void StopMarquee()
        {
            if (_scrollInfo != null)
            {
                _scrollInfo.Stop();
                _scrollInfo = null;
            }
            TextTransform.X = 0;
        }
    }
}
