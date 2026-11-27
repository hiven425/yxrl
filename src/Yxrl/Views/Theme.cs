using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal static class Theme
{
    private static ImageSource? appIcon;
    internal static ImageSource? AppIcon
    {
        get
        {
            if (appIcon is not null) return appIcon;
            if (Application.Current is null) return null;
            try
            {
                var uri = new Uri("pack://application:,,,/Yxrl;component/app.ico", UriKind.Absolute);
                var frame = BitmapFrame.Create(uri);
                frame.Freeze();
                appIcon = frame;
            }
            catch (Exception ex) when (ex is UriFormatException or NotSupportedException or IOException or InvalidOperationException)
            {
            }
            return appIcon;
        }
    }

    private const string Key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly string[] Keys = [
        "Surface", "SurfaceGlass", "Paper", "Card", "HoverFill",
        "Ink", "Muted", "Faint", "Line", "PanelBorder",
        "Accent", "AccentHover", "AccentTop", "AccentBottom", "AccentSheen",
        "Selected", "OnSelected", "Hot",
        "Weekend", "Positive", "RestFill", "WorkFill",
        "Danger", "DangerFill", "DangerHover", "Warning", "WarningFill",
        "GlassFill", "GlassHover", "GlassBorder"
    ];
    private static readonly string[] LightColors = [
        "#F5F8FC", "#EBF5F9FD", "#FFFFFF", "#ECF3F9", "#E2ECF6",
        "#14202B", "#4B6073", "#566D80", "#D8E3EE", "#BDD0E1",
        "#005FB8", "#004E9A", "#0B6ED0", "#0057AD", "#38FFFFFF",
        "#005FB8", "#FFFFFF", "#E4F1FF",
        "#B83232", "#0E6F54", "#FFE9E7", "#E3F5EE",
        "#B42D37", "#D13438", "#B6282C", "#B45309", "#FEF3C7",
        "#D8FFFFFF", "#F2FFFFFF", "#C8CFE2F2"
    ];
    private static readonly string[] DarkColors = [
        "#16202A", "#E416202A", "#1E2B37", "#243442", "#2B3E4F",
        "#EEF4FA", "#A5B9CA", "#90A6B8", "#304354", "#465F74",
        "#76C7FF", "#8FD2FF", "#87CEFF", "#65BCFA", "#48FFFFFF",
        "#76C7FF", "#07243D", "#1B3952",
        "#FF8F8A", "#7CDCC0", "#4A2B2C", "#1E3D34",
        "#FF9492", "#C83A3E", "#DE4B4F", "#FBBF24", "#423015",
        "#44243545", "#5E2E4357", "#54A4CEE8"
    ];

    internal static bool IsDark { get; private set; }

    internal static Color SystemBar => (int?)Registry.GetValue(Key, "SystemUsesLightTheme", 1) == 0
        ? Color.FromRgb(34, 34, 34) : Color.FromRgb(242, 242, 242);

    internal static void Apply()
    {
        if (SystemParameters.HighContrast) {
            IsDark = false;
            var hc = new[] {
                SystemColors.WindowBrush, SystemColors.WindowBrush, SystemColors.WindowBrush, SystemColors.ControlBrush, SystemColors.ControlBrush,
                SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.WindowTextBrush, SystemColors.WindowTextBrush,
                SystemColors.HotTrackBrush, SystemColors.HotTrackBrush, SystemColors.HighlightBrush, SystemColors.HighlightBrush, SystemColors.HighlightBrush,
                SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, SystemColors.ControlBrush,
                SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, SystemColors.ControlBrush, SystemColors.ControlBrush,
                SystemColors.HotTrackBrush, SystemColors.HighlightBrush, SystemColors.HotTrackBrush, SystemColors.WindowTextBrush, SystemColors.ControlBrush,
                SystemColors.WindowBrush, SystemColors.ControlBrush, SystemColors.WindowTextBrush
            };
            for (int i = 0; i < Keys.Length; i++) Application.Current.Resources[Keys[i]] = hc[i];
            Application.Current.Resources["AccentGradient"] = SystemColors.HighlightBrush;
            Application.Current.Resources["AccentHoverGradient"] = SystemColors.HotTrackBrush;
            return;
        }
        IsDark = (int?)Registry.GetValue(Key, "AppsUseLightTheme", 1) == 0;
        ApplyPalette(IsDark);
    }

    internal static void ApplyPalette(bool dark)
    {
        IsDark = dark;
        var colors = dark ? DarkColors : LightColors;
        for (int i = 0; i < Keys.Length; i++) {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze();
            Application.Current.Resources[Keys[i]] = brush;
        }
        var accent = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#87CEFF" : "#0B6ED0"),
            (Color)ColorConverter.ConvertFromString(dark ? "#65BCFA" : "#0057AD"),
            new Point(0.5, 0), new Point(0.5, 1));
        accent.Freeze();
        Application.Current.Resources["AccentGradient"] = accent;

        var accentHover = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#9BD7FF" : "#0062C4"),
            (Color)ColorConverter.ConvertFromString(dark ? "#76C7FF" : "#004C97"),
            new Point(0.5, 0), new Point(0.5, 1));
        accentHover.Freeze();
        Application.Current.Resources["AccentHoverGradient"] = accentHover;
    }

    internal static bool ApplyWindowChrome(Window window, Native.BackdropKind backdrop, bool setBorder = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return false;
        if (SystemParameters.HighContrast || backdrop == Native.BackdropKind.None) {
            Native.ApplyDwmStyle(hwnd, IsDark && !SystemParameters.HighContrast, Native.BackdropKind.None);
            return false;
        }
        if (!window.AllowsTransparency && HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;
        uint? border = setBorder ? (IsDark ? 0x00745F46u : 0x00E1D0BDu) : null;
        return Native.ApplyDwmStyle(hwnd, IsDark, backdrop, border);
    }
}
