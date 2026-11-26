using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace Yxrl.Views;

internal sealed class WeatherAttributionLink : Hyperlink
{
    public WeatherAttributionLink() : base(new Run(VisualCrossing.Name))
    {
        NavigateUri = new Uri("https://www.visualcrossing.com/");
        RequestNavigate += (_, e) => {
            e.Handled = true;
            try { Process.Start(new ProcessStartInfo(NavigateUri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Win32Exception) { MessageBox.Show($"可在浏览器中打开 {NavigateUri}", "天气数据来源"); }
        };
    }
}
