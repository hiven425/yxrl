using System.Windows;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal partial class ClockFormatGuide : Window
{
    public ClockFormatGuide()
    {
        InitializeComponent();
        Loaded += (_, _) => {
            if (Owner is null) return;
            MaxWidth = Math.Max(MinWidth, Owner.ActualWidth - 12);
            MaxHeight = Math.Max(MinHeight, Owner.ActualHeight - 12);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica))
            Background = Brushes.Transparent;
        else
            SetResourceReference(BackgroundProperty, "Surface");
    }
}
