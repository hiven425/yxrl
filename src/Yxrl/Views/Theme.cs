using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal static class Theme
{
    private const string Key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly string[] Keys = [
        "Surface", "SurfaceGlass", "Paper", "Card", "HoverFill",
        "Ink", "Muted", "Faint", "Line", "PanelBorder",
        "Accent", "AccentHover", "Selected", "OnSelected", "Hot",
        "Weekend", "Positive", "RestFill", "WorkFill", "Danger",
        "GlassFill", "GlassHover", "GlassBorder"
    ];
    private static readonly string[] LightColors = [
        "#F9FBFD", "#ECF9FBFD", "#FFFFFF", "#EFF4F8", "#E6EEF5",
        "#182430", "#4F6373", "#5A6F80", "#DCE4EC", "#C2D0DC",
        "#005FB8", "#0052A0", "#005FB8", "#FFFFFF", "#E6F2FF",
        "#B83232", "#0E6F54", "#FFE9E7", "#E3F5EE", "#B42D37",
        "#CCFFFFFF", "#EAFFFFFF", "#BDD6E7F5"
    ];
    private static readonly string[] DarkColors = [
        "#1D2730", "#E41D2730", "#24313C", "#283744", "#2E3F4D",
        "#EEF4F9", "#A9BCCB", "#96AAB9", "#374957", "#4E6474",
        "#76C7FF", "#8FD2FF", "#76C7FF", "#07243D", "#1E3B52",
        "#FF8F8A", "#7CDCC0", "#4A2B2C", "#1E3D34", "#FF9492",
        "#38344757", "#523E5466", "#489EC5E2"
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
                SystemColors.HotTrackBrush, SystemColors.HotTrackBrush, SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, SystemColors.ControlBrush,
                SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, SystemColors.ControlBrush, SystemColors.ControlBrush, SystemColors.HotTrackBrush,
                SystemColors.WindowBrush, SystemColors.ControlBrush, SystemColors.WindowTextBrush
            };
            for (int i = 0; i < Keys.Length; i++) Application.Current.Resources[Keys[i]] = hc[i];
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
        uint? border = setBorder ? (IsDark ? 0x0074644Eu : 0x00DCD0C2u) : null;
        return Native.ApplyDwmStyle(hwnd, IsDark, backdrop, border);
    }
}
