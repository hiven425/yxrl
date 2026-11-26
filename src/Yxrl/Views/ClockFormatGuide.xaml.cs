using System.Windows;

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
}
