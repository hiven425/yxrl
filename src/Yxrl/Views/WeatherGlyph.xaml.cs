using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yxrl.Views;

internal partial class WeatherGlyph : UserControl
{
    private Storyboard? motion;
    private bool animationsEnabled;
    private bool appliedDark;
    private bool hasRendered;
    internal WeatherScene Scene { get; private set; }

    public WeatherGlyph()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => Apply(Scene, Theme.IsDark);
        Unloaded += (_, _) => StopAnimation();
    }

    internal void SetScene(WeatherScene next) => Apply(next, Theme.IsDark);

    internal void RefreshTheme() => Apply(Scene, Theme.IsDark, forceBackdrop: true);

    private void Apply(WeatherScene next, bool dark, bool forceBackdrop = false)
    {
        bool animate = IsVisible && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        if (hasRendered && !forceBackdrop && next == Scene && dark == appliedDark && animate == animationsEnabled)
            return;

        hasRendered = true;
        appliedDark = dark;
        StopAnimation();
        Scene = next;
        animationsEnabled = animate;

        ApplyGlassPlate(next, dark);

        bool partly = next is WeatherScene.PartlyCloudy or WeatherScene.CloudyNight;
        bool wet = next is WeatherScene.Rain or WeatherScene.Thunder or WeatherScene.Snow;
        bool dawn = next == WeatherScene.Sunrise;
        bool twilight = dawn || next == WeatherScene.Sunset;

        Show(Solar, next is WeatherScene.Sun or WeatherScene.PartlyCloudy);
        Show(Lunar, next is WeatherScene.Night or WeatherScene.CloudyNight);
        Show(Twilight, twilight);
        Show(Clouds, partly || wet || next is WeatherScene.Cloud or WeatherScene.Fog);
        Show(Rain, next is WeatherScene.Rain or WeatherScene.Thunder);
        Show(Flakes, next == WeatherScene.Snow);
        Show(Bolt, next == WeatherScene.Thunder);
        Show(Fog, next == WeatherScene.Fog);
        Show(Wind, next == WeatherScene.Wind);
        Show(Unknown, next == WeatherScene.Unknown);

        SunShift.X = MoonShift.X = partly ? -9 : 0;
        SunShift.Y = MoonShift.Y = partly ? -4 : 0;
        CloudMotion.Y = wet || next == WeatherScene.Fog ? -4 : partly ? 5 : 2;
        FrontCloud.Fill = (Brush)FindResource(next is WeatherScene.Rain or WeatherScene.Thunder ? "StormCloudGlass" : "CloudFrontGlass");
        HorizonSun.Fill = (Brush)FindResource(dawn ? "DawnGlass" : "DuskGlass");

        if (!animate) return;

        var story = new Storyboard();
        if (Solar.Visibility == Visibility.Visible) {
            AddMotion(story, RaysRotation, RotateTransform.AngleProperty, 0, 360, 20);
            AddMotion(story, SunHaloScale, ScaleTransform.ScaleXProperty, 0.94, 1.08, 3.2, reverse: true);
            AddMotion(story, SunHaloScale, ScaleTransform.ScaleYProperty, 0.94, 1.08, 3.2, reverse: true);
        }
        if (Lunar.Visibility == Visibility.Visible) {
            AddMotion(story, MoonFloat, TranslateTransform.YProperty, -1.2, 1.2, 3.6, reverse: true);
            AddMotion(story, StarMajor, OpacityProperty, 0.42, 1.0, 2.2, reverse: true);
            AddMotion(story, StarMinor, OpacityProperty, 0.35, 1.0, 2.6, reverse: true, delay: 0.9);
        }
        if (twilight) {
            AddMotion(story, HorizonSunMotion, TranslateTransform.YProperty, dawn ? 14 : -5, dawn ? -5 : 14, 4.6);
            AddOpacityPulse(story, HorizonSun, 4.6, (0, 0), (0.22, 1), (0.76, 1), (1, 0));
            AddMotion(story, HorizonBar1, OpacityProperty, 0.65, 1.0, 2.3, reverse: true);
            AddMotion(story, HorizonBar2, OpacityProperty, 0.45, 0.9, 2.3, reverse: true, delay: 0.6);
        }
        if (Clouds.Visibility == Visibility.Visible) {
            AddMotion(story, CloudMotion, TranslateTransform.XProperty, -1.8, 2.5, 3.4, reverse: true);
            AddMotion(story, BackCloudMotion, TranslateTransform.XProperty, 1.6, -2.0, 4.2, reverse: true);
        }
        if (Rain.Visibility == Visibility.Visible) {
            AddRainDrop(story, Drop1, Drop1Motion, 0.96, 0);
            AddRainDrop(story, Drop2, Drop2Motion, 1.08, 0.27);
            AddRainDrop(story, Drop3, Drop3Motion, 0.92, 0.54);
            AddRainDrop(story, Drop4, Drop4Motion, 1.04, 0.78);
        }
        if (Flakes.Visibility == Visibility.Visible) {
            AddSnowFlake(story, Flake1, Flake1Motion, 2.4, 0);
            AddSnowFlake(story, Flake2, Flake2Motion, 2.7, 0.85);
            AddSnowFlake(story, Flake3, Flake3Motion, 2.5, 1.65);
        }
        if (Bolt.Visibility == Visibility.Visible) {
            AddOpacityPulse(story, Bolt, 3.4,
                (0, 0.55), (0.14, 1.0), (0.20, 0.35), (0.26, 1.0), (0.36, 0.55), (1, 0.55));
        }
        if (Fog.Visibility == Visibility.Visible) {
            AddMotion(story, Fog1Motion, TranslateTransform.XProperty, -2.5, 3.5, 3.4, reverse: true);
            AddMotion(story, Fog2Motion, TranslateTransform.XProperty, 3.0, -2.5, 4.0, reverse: true);
            AddMotion(story, Fog3Motion, TranslateTransform.XProperty, -2.0, 2.8, 3.7, reverse: true, delay: 0.5);
        }
        if (Wind.Visibility == Visibility.Visible) {
            AddMotion(story, Wind1Motion, TranslateTransform.XProperty, -4, 4.5, 1.6, reverse: true);
            AddMotion(story, Wind2Motion, TranslateTransform.XProperty, -3.5, 5.5, 1.4, reverse: true, delay: 0.35);
            AddMotion(story, Wind3Motion, TranslateTransform.XProperty, -4.5, 3.5, 1.8, reverse: true, delay: 0.7);
        }

        if (story.Children.Count == 0) return;
        motion = story;
        story.Begin(this, isControllable: true);
    }

    private void StopAnimation()
    {
        motion?.Remove(this);
        motion = null;
    }

    private void ApplyGlassPlate(WeatherScene scene, bool dark)
    {
        if (SystemParameters.HighContrast) {
            GlassPlate.Background = Brushes.Transparent;
            GlassPlate.BorderBrush = Brushes.Transparent;
            GlassPlate.BorderThickness = new Thickness(0);
            GlassSheen.Visibility = Visibility.Collapsed;
            return;
        }
        GlassPlate.BorderThickness = new Thickness(1);
        GlassSheen.Visibility = Visibility.Visible;

        (string top, string bottom, string border) = (scene, dark) switch {
            (WeatherScene.Sun, false) => ("#CCEBF6FF", "#B8FFE8BE", "#B0F0D59E"),
            (WeatherScene.Sun, true) => ("#42354E68", "#485B4527", "#587898B8"),
            (WeatherScene.Sunrise, false) => ("#CCE3EDFF", "#CCFFE0CE", "#B0EAC1AE"),
            (WeatherScene.Sunrise, true) => ("#4635476B", "#4A613C3C", "#5A8CA1C4"),
            (WeatherScene.Sunset, false) => ("#CCECDCF6", "#CCFDD8C8", "#B0DFB2CC"),
            (WeatherScene.Sunset, true) => ("#4848315A", "#4C643C4B", "#5A9B7EB0"),
            (WeatherScene.Night or WeatherScene.CloudyNight, false) => ("#D4DCEAF8", "#C8C6DCF2", "#B0A9C7E3"),
            (WeatherScene.Night or WeatherScene.CloudyNight, true) => ("#481F3249", "#5219283D", "#586E98C5"),
            (WeatherScene.Rain or WeatherScene.Thunder, false) => ("#D4DFEEFA", "#C4C6E2F6", "#B0A5C8E4"),
            (WeatherScene.Rain or WeatherScene.Thunder, true) => ("#48263C50", "#521E3244", "#586892B5"),
            (WeatherScene.Snow, false) => ("#DCEBF7FF", "#D0D7EEFA", "#B4B6D7EE"),
            (WeatherScene.Snow, true) => ("#4A2C4457", "#50253B4E", "#587DA8CA"),
            (WeatherScene.PartlyCloudy, false) => ("#D6EBF6FF", "#C8FCEFD6", "#B2BBD8EE"),
            (WeatherScene.PartlyCloudy, true) => ("#462B4157", "#483A4857", "#586F94B5"),
            _ => dark
                ? ("#422C3E4F", "#48233443", "#546786A0")
                : ("#D6EDF5FC", "#C8DCEAF5", "#B0B5CEE0")
        };

        var fill = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(top),
            (Color)ColorConverter.ConvertFromString(bottom),
            new Point(0, 0), new Point(0.95, 1));
        fill.Freeze();
        var stroke = new LinearGradientBrush(
            dark ? (Color)ColorConverter.ConvertFromString("#70B8D8F0") : Color.FromArgb(215, 255, 255, 255),
            (Color)ColorConverter.ConvertFromString(border),
            new Point(0, 0), new Point(1, 1));
        stroke.Freeze();
        GlassPlate.Background = fill;
        GlassPlate.BorderBrush = stroke;
    }

    private static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void AddRainDrop(Storyboard story, UIElement drop, TranslateTransform motion, double seconds, double delay)
    {
        AddMotion(story, motion, TranslateTransform.YProperty, -7, 13, seconds, delay: delay);
        AddMotion(story, motion, TranslateTransform.XProperty, 0.5, -1.5, seconds, delay: delay);
        AddOpacityPulse(story, drop, seconds, (0, 0), (0.2, 0.95), (0.72, 0.95), (1, 0), delay);
    }

    private static void AddSnowFlake(Storyboard story, UIElement flake, TranslateTransform motion, double seconds, double delay)
    {
        AddMotion(story, motion, TranslateTransform.YProperty, -8, 10, seconds, delay: delay);
        AddMotion(story, motion, TranslateTransform.XProperty, -2.5, 2.5, seconds * 0.55, reverse: true, delay: delay);
        AddOpacityPulse(story, flake, seconds, (0, 0), (0.22, 0.95), (0.75, 0.9), (1, 0), delay);
    }

    private static void AddOpacityPulse(Storyboard story, DependencyObject element, double seconds,
        params (double Percent, double Opacity)[] stops)
    {
        double delay = 0;
        AddOpacityPulse(story, element, seconds, stops, delay);
    }

    private static void AddOpacityPulse(Storyboard story, DependencyObject element, double seconds,
        (double Percent, double Opacity) first, (double Percent, double Opacity) second,
        (double Percent, double Opacity) third, (double Percent, double Opacity) fourth, double delay = 0) =>
        AddOpacityPulse(story, element, seconds, [first, second, third, fourth], delay);

    private static void AddOpacityPulse(Storyboard story, DependencyObject element, double seconds,
        (double Percent, double Opacity)[] stops, double delay)
    {
        var frames = new DoubleAnimationUsingKeyFrames {
            Duration = TimeSpan.FromSeconds(seconds),
            BeginTime = TimeSpan.FromSeconds(delay),
            RepeatBehavior = RepeatBehavior.Forever
        };
        foreach (var (percent, opacity) in stops)
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(opacity, KeyTime.FromPercent(percent)));
        Target(story, frames, element, OpacityProperty);
    }

    private static void AddMotion(Storyboard story, DependencyObject element, DependencyProperty property,
        double from, double to, double seconds, bool reverse = false, double delay = 0)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) {
            AutoReverse = reverse, BeginTime = TimeSpan.FromSeconds(delay), RepeatBehavior = RepeatBehavior.Forever
        };
        Target(story, animation, element, property);
    }

    private static void Target(Storyboard story, Timeline animation, DependencyObject element, DependencyProperty property)
    {
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        story.Children.Add(animation);
    }
}
