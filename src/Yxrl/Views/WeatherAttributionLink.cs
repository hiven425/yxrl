using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Yxrl.Views;

internal sealed class WeatherAttributionLink : Hyperlink
{
    internal static readonly Uri VisualCrossingUri = new("https://www.visualcrossing.com/");
    internal static readonly Uri OpenMeteoUri = new("https://open-meteo.com/");

    public WeatherAttributionLink() : base(new Run(VisualCrossing.Name))
    {
        NavigateUri = VisualCrossingUri;
        RequestNavigate += (_, e) => {
            e.Handled = true;
            Open(e.Uri);
        };
    }

    internal static void Open(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception) { MessageBox.Show($"可在浏览器中打开 {uri}", "天气数据"); }
    }
}
