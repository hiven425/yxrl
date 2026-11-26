using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace Yxrl.Views;

internal static class Theme
{
    private const string Key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    internal static Color SystemBar => (int?)Registry.GetValue(Key,"SystemUsesLightTheme",1) == 0
        ? Color.FromRgb(34,34,34) : Color.FromRgb(242,242,242);
    internal static void Apply()
    {
        var dark = (int?)Registry.GetValue(Key,"AppsUseLightTheme",1) == 0;
        var keys = new[] { "Surface","Card","Ink","Muted","Line","Accent","Selected","OnSelected","Hot","Weekend","Positive","RestFill","WorkFill" };
        var colors = dark ? new[] { "#202C35","#293B46","#EDF4F8","#AFC2CE","#3D515D","#89C9FF","#89C9FF","#082B47","#213F55","#FFB1A6","#8ADBC0","#493332","#223E37" }
            : new[] { "#FBFDFF","#F0F5F8","#192B38","#506675","#D9E3E9","#075D9D","#075D9D","#FFFFFF","#E3F1FF","#A24239","#106A57","#FFEBE5","#E4F4EE" };
        if (SystemParameters.HighContrast) {
            var hc = new[]{ SystemColors.WindowBrush,SystemColors.ControlBrush,SystemColors.WindowTextBrush,SystemColors.WindowTextBrush,
                SystemColors.WindowTextBrush,SystemColors.HotTrackBrush,SystemColors.HighlightBrush,SystemColors.HighlightTextBrush,
                SystemColors.ControlBrush,SystemColors.WindowTextBrush,SystemColors.WindowTextBrush,SystemColors.ControlBrush,SystemColors.ControlBrush };
            for(int i=0;i<keys.Length;i++) Application.Current.Resources[keys[i]]=hc[i];
        } else for(int i=0;i<keys.Length;i++)
            Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
