using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal partial class CalendarWindow : Window
{
    private static readonly Size DefaultSize = new(412, 566);
    private static readonly Size MinimumSize = new(344, 460);
    private const int WmSizing = 0x0214, WmEnterSizeMove = 0x0231, WmExitSizeMove = 0x0232;

    private readonly SettingsStore store;
    private readonly CalendarService calendar;
    private readonly WeatherService weather;
    private readonly MonthViewModel vm;
    private readonly Action<int> openSettings;
    private readonly Capture.CaptureTools captureTools;
    private DateOnly today;
    private bool presented;
    private bool backdropActive;
    private Size preferredSize = DefaultSize;
    private PixelRect resizeArea;
    private PixelRect? resizeStart;
    private DayDetailWindow? details;
    private Button? selectedDayButton;
    private int detailWidth, detailGap;
    private PixelRect? boundsBeforeDetails;

    private PixelRect CalendarArea => detailWidth == 0 ? resizeArea
        : resizeArea with { Left = resizeArea.Left + detailWidth + detailGap };

    public CalendarWindow(SettingsStore store, CalendarService calendar, WeatherService weather,
        ReminderStore reminderStore, Action<int> openSettings, Capture.CaptureTools captureTools)
    {
        InitializeComponent();
        if (Theme.AppIcon is { } appIcon) Icon = appIcon;
        Width = DefaultSize.Width;
        Height = DefaultSize.Height;
        (this.store, this.calendar, this.weather, this.openSettings) = (store, calendar, weather, openSettings);
        this.reminderStore = reminderStore;
        this.captureTools = captureTools;
        vm = new(calendar);
        vm.Select(DateOnly.FromDateTime(DateTime.Now));
        Closing += (_, e) => {
            if (!presented) return;
            e.Cancel = true;
            Dispatcher.InvokeAsync(Dismiss);
        };
        LocationChanged += (_, _) => PositionDetails();
        MouseEnter += (_, _) => leaveTimeout.Reset();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        calendarHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(calendarHandle)?.AddHook(ResizeMessage);
        ApplyChrome();
    }

    private void CaptureMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = CaptureEntry.ContextMenu;
        foreach (MenuItem item in menu.Items)
            item.IsEnabled = item.Tag is null || (captureTools.Options.Enabled && (!Equals(item.Tag, "RestorePin") || captureTools.HasClosedPins));
        menu.PlacementTarget = CaptureEntry;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }
    private void CaptureCommand(object sender, RoutedEventArgs e)
    {
        CaptureEntry.ContextMenu.IsOpen = false;
        if (((MenuItem)sender).Tag is not string name) {
            openSettings(CaptureSettingsPage.Section);
            return;
        }
        var action = Enum.Parse<Capture.ShotAction>(name);
        Dispatcher.InvokeAsync(() => captureTools.Invoke(action), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void RecoverRecording(object sender, RoutedEventArgs e)
    {
        CaptureEntry.ContextMenu.IsOpen = false;
        Dispatcher.InvokeAsync(() => captureTools.OfferRecovery(interactive: true),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    internal void RefreshTheme()
    {
        ApplyChrome();
        WeatherIcon.RefreshTheme();
        RenderMonth();
        if (IsVisible) Tick(DateTime.Now);
        details?.RefreshTheme();
    }

    private void ApplyChrome()
    {
        backdropActive = Theme.ApplyWindowChrome(this, Native.BackdropKind.Acrylic, setBorder: true);
        if (backdropActive) {
            RootBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceGlass");
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.CornerRadius = new CornerRadius(0);
        } else {
            RootBorder.SetResourceReference(Border.BackgroundProperty, "Surface");
            RootBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.CornerRadius = new CornerRadius(12);
        }
    }

    private static (int Left, int Top, int Right, int Bottom) FrameInset(nint hwnd)
    {
        if (hwnd == 0 || !Native.GetWindowRect(hwnd, out var outer)) return (0, 0, 0, 0);
        var visible = Native.VisibleRect(hwnd);
        if (!visible.HasArea) return (0, 0, 0, 0);
        return (Math.Max(0, visible.Left - outer.Left), Math.Max(0, visible.Top - outer.Top),
            Math.Max(0, outer.Right - visible.Right), Math.Max(0, outer.Bottom - visible.Bottom));
    }

    private static PixelRect ExpandForFrame(PixelRect visible, (int Left, int Top, int Right, int Bottom) inset) =>
        new(visible.Left - inset.Left, visible.Top - inset.Top, visible.Right + inset.Right, visible.Bottom + inset.Bottom);

    private static PixelRect TrimFrame(PixelRect outer, (int Left, int Top, int Right, int Bottom) inset) =>
        new(outer.Left + inset.Left, outer.Top + inset.Top, outer.Right - inset.Right, outer.Bottom - inset.Bottom);

    private nint ResizeMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmEnterSizeMove) {
            resizing = true;
            leaveTimeout.Reset();
        } else if (message == WmSizing && CalendarArea.HasArea) {
            if (resizeStart is null && Native.GetWindowRect(hwnd, out var current)) resizeStart = current;
            var inset = FrameInset(hwnd);
            var allowed = ExpandForFrame(CalendarArea, inset);
            var proposed = Marshal.PtrToStructure<PixelRect>(lParam);
            var bounded = proposed.Intersect(allowed);
            if (bounded.HasArea && bounded != proposed) {
                Marshal.StructureToPtr(bounded, lParam, false);
                handled = true;
                return 1;
            }
        } else if (message == WmExitSizeMove) {
            resizing = false;
            var start = resizeStart;
            resizeStart = null;
            if (start is { } original && Native.GetWindowRect(hwnd, out var end) &&
                (end.Width != original.Width || end.Height != original.Height)) {
                var visible = TrimFrame(end, FrameInset(hwnd));
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                preferredSize = new(visible.Width / scale, visible.Height / scale);
                boundsBeforeDetails = null;
            }
        }
        return 0;
    }

    private void ScaleContent(object sender, SizeChangedEventArgs e)
    {
        double scale = Math.Min(e.NewSize.Width / DefaultSize.Width, e.NewSize.Height / DefaultSize.Height);
        if (scale <= 0) return;
        ContentScale.ScaleX = scale;
        ContentScale.ScaleY = scale;
        PositionDetails();
    }

    public void Present(ClockAnchor? anchor, bool resetDate = true)
    {
        presented = false;
        leaveTimeout.Reset();
        HideDetails();
        if (resetDate) vm.Select(DateOnly.FromDateTime(DateTime.Now));
        RenderMonth();
        Tick(DateTime.Now);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var visibleBounds = Placement(anchor, hwnd);
        if (!visibleBounds.HasArea) return;
        var initialInset = FrameInset(hwnd);
        var outer = ExpandForFrame(visibleBounds, initialInset);
        presented = true;
        Native.SetWindowPos(hwnd, Native.Topmost, outer.Left, outer.Top, outer.Width, outer.Height, Native.NoActivate);
        if (!IsVisible) Show();
        var actualInset = FrameInset(hwnd);
        if (actualInset != initialInset) {
            visibleBounds = Placement(anchor, hwnd);
            outer = ExpandForFrame(visibleBounds, actualInset);
        }
        // Showing the flyout above other windows must work even if Windows denies activation.
        Native.SetWindowPos(hwnd, Native.Topmost, outer.Left, outer.Top, outer.Width, outer.Height, Native.Show | Native.NoActivate);
        Native.SetForegroundWindow(hwnd);
        Activate();
        Focus();
        PollPointer();
    }

    private PixelRect Placement(ClockAnchor? anchor, nint hwnd)
    {
        var taskbar = anchor?.Taskbar ?? Native.FindWindow("Shell_TrayWnd", null);
        var monitorWindow = taskbar == 0 ? hwnd : taskbar;
        var info = Native.MonitorOf(monitorWindow);
        var work = anchor?.Work ?? info.Work;
        var monitor = anchor?.Monitor ?? info.Monitor;
        uint dpi = anchor?.Dpi ?? Native.GetDpiForWindow(monitorWindow);
        double scale = dpi == 0 ? 1 : dpi / 96.0;
        int gap = (int)Math.Round(8 * scale);
        var available = work;
        bool atTop = false, atLeft = false, vertical = false;
        if (Native.GetWindowRect(taskbar, out var bar) && bar.HasArea) {
            if (bar.Width >= bar.Height) {
                atTop = bar.Top < monitor.Center.Y;
                // Reserve the expanded taskbar even if it is auto-hidden when the window is reopened.
                available = atTop
                    ? work with { Top = Math.Max(work.Top, Math.Max(bar.Bottom, monitor.Top + bar.Height)) }
                    : work with { Bottom = Math.Min(work.Bottom, Math.Min(bar.Top, monitor.Bottom - bar.Height)) };
            } else {
                vertical = true;
                atLeft = bar.Left < monitor.Center.X;
                available = atLeft
                    ? work with { Left = Math.Max(work.Left, Math.Max(bar.Right, monitor.Left + bar.Width)) }
                    : work with { Right = Math.Min(work.Right, Math.Min(bar.Left, monitor.Right - bar.Width)) };
            }
        }
        if (!available.HasArea) available = work;
        if (!available.HasArea) return default;
        gap = Math.Min(gap, (Math.Min(available.Width, available.Height) - 1) / 2);
        resizeArea = new(available.Left + gap, available.Top + gap, available.Right - gap, available.Bottom - gap);
        SetSizeLimits(resizeArea, scale);
        int w = (int)Math.Round(Math.Clamp(preferredSize.Width, MinWidth, MaxWidth) * scale);
        int h = (int)Math.Round(Math.Clamp(preferredSize.Height, MinHeight, MaxHeight) * scale);
        int x = atLeft ? resizeArea.Left : resizeArea.Right - w;
        int y = atTop ? resizeArea.Top : resizeArea.Bottom - h;
        if (anchor is { } clock) {
            if (vertical) y = Math.Clamp(clock.Bounds.Bottom - h, resizeArea.Top, resizeArea.Bottom - h);
            else x = Math.Clamp(clock.Bounds.Right - w, resizeArea.Left, resizeArea.Right - w);
        }
        return new(x, y, x + w, y + h);
    }

    private void SetSizeLimits(PixelRect area, double scale)
    {
        var inset = FrameInset(new WindowInteropHelper(this).Handle);
        double extraW = (inset.Left + inset.Right) / scale;
        double extraH = (inset.Top + inset.Bottom) / scale;
        MinWidth = Math.Min(MinimumSize.Width, area.Width / scale) + extraW;
        MaxWidth = area.Width / scale + extraW;
        MinHeight = Math.Min(MinimumSize.Height, area.Height / scale) + extraH;
        MaxHeight = area.Height / scale + extraH;
    }

    public void Dismiss() { presented = false; leaveTimeout.Reset(); CloseReminderMenu(); HideDetails(); Hide(); }
    public void Finish() { presented = false; leaveTimeout.Reset(); CloseReminderMenu(); HideDetails(); Close(); }

    private void SettingsClick(object sender, RoutedEventArgs e) => openSettings(0);
    private void WeatherClick(object sender, RoutedEventArgs e) => openSettings(2);
    private void DetailToggleClick(object sender, RoutedEventArgs e) => ToggleDetails(vm.Selected);

    public void Tick(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        if (date != today) { if (vm.Selected == today) vm.Select(date); today = date; RenderMonth(); }
        TimePart.Text = ClockFormatter.Time(now, store.Current.Calendar.Hour12, false);
        SecondsPart.Text = now.ToString(":ss");
        AutomationProperties.SetName(LiveTime, TimePart.Text + SecondsPart.Text);
        TodayLine.Text = $"{date:yyyy年M月d日}  {CalendarService.Weekday(date)}  {calendar.Get(date).LunarDate}";
        RenderWeather(store.Current.City, weather.Read(store.Current.City), new DateTimeOffset(now));
    }

    private void RenderWeather(City? place, WeatherReading? reading, DateTimeOffset now)
    {
        if (place is null) {
            WeatherIcon.SetScene(WeatherScene.Unknown);
            WeatherTemp.Visibility = Visibility.Collapsed;
            WeatherCond.Text = "设置天气";
            WeatherCond.FontSize = 12;
            WeatherCond.FontWeight = FontWeights.SemiBold;
            WeatherCond.Foreground = ColorOf("Ink");
            WeatherCond.Margin = new Thickness(0);
            WeatherMeta.Text = "选择城市";
            WeatherSub.Visibility = Visibility.Collapsed;
            WeatherButton.ToolTip = "点击进入「城市与天气」设置";
            AutomationProperties.SetName(WeatherButton, "城市与天气，点击选择城市");
            return;
        }

        var current = reading?.Current;
        var scene = WeatherAppearance.At(current, now);
        WeatherIcon.SetScene(scene);
        var label = WeatherAppearance.Label(scene, current);

        WeatherCond.FontSize = 11.5;
        WeatherCond.FontWeight = FontWeights.Normal;
        WeatherCond.Foreground = ColorOf("Muted");

        if (current is { } w) {
            WeatherTemp.Text = $"{w.Temp:0}°";
            WeatherTemp.Visibility = Visibility.Visible;
            WeatherCond.Text = w.Condition;
            WeatherCond.Margin = new Thickness(4, 0, 0, 1);
        } else {
            WeatherTemp.Visibility = Visibility.Collapsed;
            WeatherCond.Text = reading is null or { WeatherPending: true } ? "更新中" : "暂不可用";
            WeatherCond.Margin = new Thickness(0);
        }

        string meta = place.Name;
        if (reading?.Air is { } a) meta += $" · AQI {a.Aqi:0}";
        WeatherMeta.Text = meta;

        var todayLocal = DateOnly.FromDateTime(now.DateTime);
        string sub = current is { } cw
            ? (cw.IsStale ? "缓存 " : "") + WeatherReading.ShortObservedTime(cw.ObservedAt, todayLocal)
            : reading?.Air is { } ca
                ? (ca.IsStale ? "缓存 " : "") + WeatherReading.ShortObservedTime(ca.ObservedAt, todayLocal)
                : "";
        WeatherSub.Text = sub;
        WeatherSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var tooltip = WeatherReading.DetailTooltip(place, reading, label, now, weather.Warning);
        WeatherButton.ToolTip = tooltip;
        AutomationProperties.SetName(WeatherButton, $"{place.Name} {label} {(current is { } t ? $"{t.Temp:0}°" : "")}".Trim());
    }

    private void PreviousClick(object sender, RoutedEventArgs e) => Move(-1);
    private void NextClick(object sender, RoutedEventArgs e) => Move(1);
    private void TodayClick(object sender, RoutedEventArgs e) => Select(DateOnly.FromDateTime(DateTime.Now));
    private void Move(int offset) { vm.Shift(offset); RenderMonth(); }
    private void Select(DateOnly day) { vm.Select(day); RenderMonth(); }

    private void ToggleDetails(DateOnly day)
    {
        if (details?.IsVisible == true && day == vm.Selected) {
            CloseDetails();
            return;
        }
        if (day != vm.Selected) Select(day);
        if (details?.IsVisible != true) OpenDetails();
        selectedDayButton?.Focus();
    }

    private void OpenDetails()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!IsVisible || resizeArea.Width < 3 || !Native.GetWindowRect(hwnd, out var monthOuter))
            return;
        if (details is null) {
            details = new(CloseDetails) { Owner = this };
            details.MouseEnter += (_, _) => leaveTimeout.Reset();
            details.Closing += (_, e) => {
                if (!presented) return;
                e.Cancel = true;
                Dispatcher.InvokeAsync(CloseDetails);
            };
        }
        var inset = FrameInset(hwnd);
        var monthVisible = TrimFrame(monthOuter, inset);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        detailGap = Math.Min((int)Math.Round(8 * scale), resizeArea.Width / 8);
        detailWidth = Math.Min((int)Math.Round(DayDetailWindow.PreferredWidth * scale), (resizeArea.Width - detailGap) / 2);
        var area = CalendarArea;
        var fittedVisible = FitWidth(monthVisible, area, scale);
        if (fittedVisible != monthVisible) boundsBeforeDetails = monthVisible;
        SetSizeLimits(area, scale);
        if (fittedVisible != monthVisible) PlaceWindow(this, ExpandForFrame(fittedVisible, inset));

        UpdateDetails(vm.Detail, store.Current.Calendar.Week(vm.Selected));
        PositionDetails();
        details.Show();
        PositionDetails();
        UpdateDetailToggleLabel();
    }

    private void UpdateDetails(DayInfo day, int? week)
    {
        details?.SetDate(day, week);
        if (selectedDayButton is not null) AutomationProperties.SetItemStatus(selectedDayButton, "日详情已展开");
    }

    private void PositionDetails()
    {
        if (detailWidth == 0 || details is null) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var month = Native.VisibleRect(hwnd);
        if (!month.HasArea) return;
        int right = month.Left - detailGap;
        PlaceWindow(details, new(right - detailWidth, month.Top, right, month.Bottom));
    }

    private static void PlaceWindow(Window window, PixelRect bounds)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        Native.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, Native.NoZOrder | Native.NoActivate);
    }

    private static PixelRect FitWidth(PixelRect month, PixelRect area, double scale)
    {
        int minimum = Math.Min((int)Math.Round(MinimumSize.Width * scale), area.Width);
        int width = Math.Clamp(month.Width, minimum, area.Width);
        int left = Math.Clamp(month.Left, area.Left, area.Right - width);
        return month with { Left = left, Right = left + width };
    }

    private void CloseDetails()
    {
        bool detailHadFocus = details?.IsActive == true;
        HideDetails();
        if (detailHadFocus && IsVisible) Activate();
        if (IsActive) selectedDayButton?.Focus();
    }

    private void HideDetails()
    {
        if (detailWidth == 0) {
            UpdateDetailToggleLabel();
            return;
        }
        detailWidth = 0;
        details?.Hide();
        if (selectedDayButton is not null) AutomationProperties.SetItemStatus(selectedDayButton, "");
        var restore = boundsBeforeDetails;
        boundsBeforeDetails = null;
        UpdateDetailToggleLabel();
        if (!presented || !IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var inset = FrameInset(hwnd);
        bool located = Native.GetWindowRect(hwnd, out var currentOuter);
        SetSizeLimits(resizeArea, scale);
        if (!located) return;
        var currentVisible = TrimFrame(currentOuter, inset);
        var boundsVisible = FitWidth(restore ?? currentVisible, resizeArea, scale);
        if (boundsVisible != currentVisible) PlaceWindow(this, ExpandForFrame(boundsVisible, inset));
    }

    private void UpdateDetailToggleLabel()
    {
        DetailToggleButton.Content = details?.IsVisible == true ? "收起 ‹" : "详情 ›";
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (IsManagingReminder) return;
        if (e.Key == Key.Escape) {
            if (details?.IsVisible == true) CloseDetails();
            else Dismiss();
            e.Handled = true;
            return;
        }
        switch (e.Key) {
            case Key.Enter: case Key.Space:
                if (Keyboard.FocusedElement is ButtonBase btn && !AutomationProperties.GetAutomationId(btn).StartsWith("Day-")) return;
                ToggleDetails(vm.Selected); break;
            case Key.Left: Select(vm.Selected.AddDays(-1)); break; case Key.Right: Select(vm.Selected.AddDays(1)); break;
            case Key.Up: Select(vm.Selected.AddDays(-7)); break; case Key.Down: Select(vm.Selected.AddDays(7)); break;
            case Key.PageUp: Move(-1); break; case Key.PageDown: Move(1); break;
            case Key.Home: Select(DateOnly.FromDateTime(DateTime.Now)); break;
            default: return;
        }
        e.Handled = true;
    }

    private Brush ColorOf(string key) => (Brush)FindResource(key);

    private Border MakeTagPill(string text, string fgKey, string bgKey) => new() {
        Background = ColorOf(bgKey),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1.5, 6, 1.5),
        Margin = new Thickness(0, 0, 5, 3),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = ColorOf(fgKey), FontWeight = FontWeights.Medium }
    };

    private void RenderMonth()
    {
        bool restoreFocus = selectedDayButton?.IsKeyboardFocused == true;
        selectedDayButton = null;
        var opt = store.Current.Calendar;
        MonthYear.Text = vm.Month.Year.ToString();
        MonthNumber.Text = vm.Month.Month.ToString();
        Previous.IsEnabled = vm.Month > CalendarService.First;
        Next.IsEnabled = vm.Month.AddMonths(1) <= CalendarService.Last;

        DaysGrid.Children.Clear();
        DaysGrid.ColumnDefinitions.Clear();
        DaysGrid.RowDefinitions.Clear();
        DaysGrid.ColumnDefinitions.Add(new() { Width = new GridLength(opt.ShowWeeks ? 22 : 0) });
        for (int i = 0; i < 7; i++) DaysGrid.ColumnDefinitions.Add(new());
        DaysGrid.RowDefinitions.Add(new() { Height = new GridLength(22) });

        void Put(UIElement element, int row, int column) { Grid.SetRow(element, row); Grid.SetColumn(element, column); DaysGrid.Children.Add(element); }
        TextBlock Text(string label, double size, string color) => new() {
            Text = label, FontSize = size, Foreground = ColorOf(color),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        if (opt.ShowWeeks) Put(Text("周", 11, "Faint"), 0, 0);
        for (int col = 0; col < 7; col++) {
            int dayIndex = (opt.WeekStart + col) % 7;
            Put(Text("日一二三四五六"[dayIndex].ToString(), 11.5, dayIndex is 0 or 6 ? "Weekend" : "Muted"), 0, col + 1);
        }

        int r = 0;
        var cellStyle = (Style)FindResource("DayCell");
        foreach (var row in vm.Weeks(opt, DateOnly.FromDateTime(DateTime.Now))) {
            r++;
            DaysGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            if (row.Number is { } week) {
                var weekText = Text(week.ToString(), 10.5, "Faint");
                Typography.SetNumeralAlignment(weekText, FontNumeralAlignment.Tabular);
                Put(weekText, r, 0);
            }
            for (int col = 0; col < 7; col++) {
                if (row.Days[col] is not { } cell) continue;
                var info = cell.Day;
                var date = info.Date;
                string color = cell.IsToday ? "OnSelected" : cell.Outside ? "Muted"
                    : date.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday ? "Weekend" : "Ink";
                var number = Text(date.Day.ToString(), 14.5, color);
                number.FontWeight = FontWeights.SemiBold;
                Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { number } };
                if (opt.ShowLunar) {
                    var sub = Text(info.Secondary, 10.5, cell.IsToday ? "OnSelected"
                        : info.Festivals.Length > 0 || info.Lunar.JieQi.Length > 0 ? "Accent" : "Muted");
                    if (cell.IsToday) sub.Opacity = 0.92;
                    content.Children.Add(sub);
                }
                var host = new Grid { Children = { content } };
                if (opt.ShowAdjustments && info.Mark.Length > 0) {
                    var mark = Text(info.Mark, 9.5, info.Mark == "休" ? "Weekend" : "Positive");
                    mark.FontWeight = FontWeights.SemiBold;
                    host.Children.Add(new Border {
                        Child = mark,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 1, 1, 0),
                        Background = ColorOf(info.Mark == "休" ? "RestFill" : "WorkFill"),
                        CornerRadius = new CornerRadius(3.5),
                        Padding = new Thickness(3, 0.5, 3, 0.5)
                    });
                }
                if (cell.IsToday && cell.IsSelected) {
                    host.Children.Insert(0, new Border {
                        BorderBrush = ColorOf("Surface"),
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(6.5),
                        Margin = new Thickness(0.5),
                        IsHitTestVisible = false
                    });
                }
                var button = new Button {
                    Style = cellStyle,
                    Content = host,
                    Focusable = true,
                    IsTabStop = cell.IsSelected,
                    Opacity = cell.Outside ? 0.72 : 1,
                    Background = cell.IsToday ? ColorOf("Selected") : cell.IsSelected ? ColorOf("Hot") : Brushes.Transparent,
                    BorderBrush = cell.IsToday ? ColorOf("Selected") : cell.IsSelected ? ColorOf("Accent") : Brushes.Transparent,
                    BorderThickness = new Thickness(cell.IsSelected && !cell.IsToday ? 1.2 : 1),
                    ToolTip = $"{date:yyyy-MM-dd} {info.LunarDate} {info.Festivals}".TrimEnd()
                };
                AutomationProperties.SetName(button, $"{date:yyyy年M月d日} {info.Festivals} {info.Mark}".Trim());
                AutomationProperties.SetAutomationId(button, $"Day-{date:yyyy-MM-dd}");
                AttachReminderMenu(button, host, date, cell.IsToday);
                if (cell.IsSelected) selectedDayButton = button;
                button.Click += (_, _) => ToggleDetails(date);
                Put(button, r, col + 1);
            }
        }

        var d = vm.Detail;
        var num = opt.Week(d.Date);
        DetailTitle.Text = $"{d.Date:M月d日}  {CalendarService.Weekday(d.Date)}" + (num.HasValue ? $"  ·  第{num}周" : "");
        DetailLunar.Text = $"{d.LunarDate} · {d.Ganzhi}";

        DetailTags.Children.Clear();
        foreach (var fest in d.Festivals.Split('、', StringSplitOptions.RemoveEmptyEntries))
            DetailTags.Children.Add(MakeTagPill(fest, "Accent", "Hot"));
        if (d.Lunar.JieQi.Length > 0)
            DetailTags.Children.Add(MakeTagPill(d.Lunar.JieQi, "Accent", "Hot"));
        if (opt.ShowAdjustments && d.Mark.Length > 0)
            DetailTags.Children.Add(MakeTagPill(d.Mark == "休" ? "放假" : "调休上班",
                d.Mark == "休" ? "Weekend" : "Positive",
                d.Mark == "休" ? "RestFill" : "WorkFill"));
        DetailTags.Visibility = DetailTags.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateDetailToggleLabel();
        if (details?.IsVisible == true) UpdateDetails(d, num);
        if (restoreFocus) selectedDayButton?.Focus();
    }
}
