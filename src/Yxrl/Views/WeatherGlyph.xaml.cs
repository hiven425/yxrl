using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yxrl.Views;

internal partial class WeatherGlyph : UserControl
{
    private readonly List<(IAnimatable Element, DependencyProperty Property)> moving = [];
    private bool animationsEnabled;
    internal WeatherScene Scene { get; private set; }

    public WeatherGlyph()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => SetScene(Scene);
    }

    internal void SetScene(WeatherScene next)
    {
        bool animate = IsVisible && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        if (next == Scene && animate == animationsEnabled) return;
        foreach (var (element, property) in moving) element.BeginAnimation(property, null);
        moving.Clear();
        Scene = next;
        animationsEnabled = animate;

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
        CloudMotion.Y = wet || next == WeatherScene.Fog ? -3 : partly ? 4 : 1;
        HorizonSun.Fill = (Brush)FindResource(dawn ? "DawnGlass" : "DuskGlass");
        if (!animate) return;

        if (Solar.Visibility == Visibility.Visible)
            AddMotion(RaysRotation, RotateTransform.AngleProperty, 0, 360, 18);
        if (Lunar.Visibility == Visibility.Visible)
            AddMotion(Stars, OpacityProperty, .46, 1, 2.4, reverse: true);
        if (twilight) {
            AddMotion(HorizonSunMotion, TranslateTransform.YProperty, dawn ? 13 : -6, dawn ? -6 : 13, 4.4);
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.4), RepeatBehavior = RepeatBehavior.Forever };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(.22)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(.76)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            StartMotion(HorizonSun, OpacityProperty, fade);
        }
        if (Clouds.Visibility == Visibility.Visible)
            AddMotion(CloudMotion, TranslateTransform.XProperty, -2, 3, 3.2, reverse: true);
        if (Rain.Visibility == Visibility.Visible) {
            AddMotion(FirstDropMotion, TranslateTransform.YProperty, -9, 15, .94);
            AddMotion(SecondDropMotion, TranslateTransform.YProperty, -10, 12, 1.06, delay: .28);
            AddMotion(ThirdDropMotion, TranslateTransform.YProperty, -7, 17, .87, delay: .48);
        }
        if (Flakes.Visibility == Visibility.Visible) {
            AddMotion(SnowMotion, TranslateTransform.XProperty, -2, 3, 1.4, reverse: true);
            AddMotion(SnowMotion, TranslateTransform.YProperty, -6, 10, 2.3);
        }
        if (Bolt.Visibility == Visibility.Visible)
            AddMotion(Bolt, OpacityProperty, .62, 1, 2.2, reverse: true);
        if (Fog.Visibility == Visibility.Visible)
            AddMotion(FogMotion, TranslateTransform.XProperty, -3, 4, 3.5, reverse: true);
        if (Wind.Visibility == Visibility.Visible)
            AddMotion(WindMotion, TranslateTransform.XProperty, -5, 5, 1.5, reverse: true);
    }

    private static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void AddMotion(IAnimatable element, DependencyProperty property,
        double from, double to, double seconds, bool reverse = false, double delay = 0)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) {
            AutoReverse = reverse, BeginTime = TimeSpan.FromSeconds(delay), RepeatBehavior = RepeatBehavior.Forever
        };
        StartMotion(element, property, animation);
    }

    private void StartMotion(IAnimatable element, DependencyProperty property, AnimationTimeline animation)
    {
        element.BeginAnimation(property, animation);
        moving.Add((element, property));
    }
}
