using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Yxrl.Desktop;
using Yxrl.Views;

namespace Yxrl;

public partial class App : Application
{
    private const string InstanceKey = @"Local\Yxrl.PersonalCalendar.v1";
    private Mutex? instance;
    private EventWaitHandle? openSignal;
    private RegisteredWaitHandle? listener;
    private bool owner, stopping, firstScan=true, awaitingVisible=true, scanning, otherClock, wantSettings;
    private DateTime nextScan, nextWeather;
    private PixelRect lastPrimary;
    private long lastRenderSecond=-1;
    private readonly Dictionary<nint, ClockOverlay> overlays=[];
    private ClockAnchor[] detected=[];
    private nint[] lastMissing=[];
    private nint lastBar;
    private ClockMouse? mouse;
    private string? inputError;
    private DispatcherTimer? timer;
    private SettingsStore store=null!;
    private CalendarService calendar=null!;
    private WeatherService weather=null!;
    private CalendarWindow panel=null!;
    private ReminderDelivery? reminderDelivery;
    private SettingsWindow? settingsWindow;
    private CancellationTokenSource? weatherRequest;
    private long? weatherCity;
    private bool directWeatherConnection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance=new Mutex(true,InstanceKey,out owner);
        openSignal=new EventWaitHandle(false,EventResetMode.AutoReset,InstanceKey+".Open");
        if (!owner) {
            foreach(var p in Process.GetProcessesByName("Yxrl")) { using(p) if(p.Id!=Environment.ProcessId) Native.AllowSetForegroundWindow(p.Id); }
            openSignal.Set(); Shutdown(); return;
        }
        wantSettings = e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase);
        Theme.Apply();
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Yxrl");
        store=new(directory); store.Load(); calendar=new(store); weather=new(directory); weather.Load();
        var reminders = new ReminderStore(directory);
        reminders.Load();
        panel=new(store,calendar,weather,reminders,OpenSettings);
        reminderDelivery = new(reminders, () => ActiveAnchor?.Taskbar ?? Native.FindWindow("Shell_TrayWnd", null));
        listener=ThreadPool.RegisterWaitForSingleObject(openSignal,(_,_)=>Dispatcher.BeginInvoke(OpenSettings),null,Timeout.Infinite,false);
        SystemEvents.UserPreferenceChanged+=PreferenceChanged;
        ApplySettings();
        timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(25)};
        timer.Tick+=(_,_)=>Tick(); timer.Start(); Tick();
        reminderDelivery.Start();
    }
    private void PreferenceChanged(object sender,UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => {
        if(stopping) return;
        Theme.Apply();
        RefreshVisible(DateTime.Now,recolor:true);
    });
    private ClockAnchor? ActiveAnchor => detected.FirstOrDefault(x=>x.Taskbar==lastBar) ?? detected.FirstOrDefault();
    private void OpenSettings()
    {
        if(stopping) return;
        if(firstScan) { wantSettings=true; return; }
        if (panel.FocusReminderEditor()) return;
        if (settingsWindow is not null) { settingsWindow.BringForward(); return; }
        bool resumeDate = panel.IsVisible;
        if (!panel.IsPinned) panel.Dismiss();
        settingsWindow = new SettingsWindow(store, calendar, weather, ApplySettings,
            () => panel.Present(ActiveAnchor, resetDate: !resumeDate), Shutdown);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        UpdateStatus();
        settingsWindow.Present(ActiveAnchor);
    }
    private void ApplySettings()
    {
        if(mouse is null) {
            try {
                mouse=new ClockMouse(a=>Dispatcher.BeginInvoke(()=>ClickClock(a)),
                    point=>Dispatcher.BeginInvoke(()=>panel.HandleDesktopClick(point)));
                inputError=null;
            }
            catch(Win32Exception ex) { inputError=ex.Message; }
        }
        nextScan=DateTime.MinValue;
        nextWeather=DateTime.MinValue;
        lastRenderSecond=-1;
        if(weatherCity!=store.Current.City?.Id || directWeatherConnection!=store.Current.DirectWeatherConnection) {
            weatherCity=store.Current.City?.Id; directWeatherConnection=store.Current.DirectWeatherConnection;
            weatherRequest?.Cancel();
        }
        UpdateStatus(); RefreshVisible(DateTime.Now);
    }
    private void ClickClock(ClockAnchor anchor)
    {
        if(stopping || mouse is null || !store.Current.Clock.Enabled || !detected.Contains(anchor)) return;
        if (panel.FocusReminderEditor()) return;
        if (settingsWindow is not null) {
            if (!settingsWindow.IsEnabled) { settingsWindow.BringForward(); return; }
            settingsWindow.Close();
        }
        if(panel.IsVisible && lastBar==anchor.Taskbar) panel.Dismiss();
        else { lastBar=anchor.Taskbar; panel.Present(anchor); }
    }
    private void Tick()
    {
        if(stopping) return;
        var now=DateTime.Now; var utc=DateTime.UtcNow;
        if(Native.GetWindowRect(Native.FindWindow("Shell_TrayWnd",null),out var primary) && primary!=lastPrimary) {
            lastPrimary=primary; nextScan=DateTime.MinValue;
        }
        if(!scanning && utc>=nextScan) Scan();
        if (panel.IsVisible) panel.PollPointer();
        RefreshVisible(now);
        var second=now.Ticks/TimeSpan.TicksPerSecond;
        if(lastRenderSecond!=second) {
            lastRenderSecond=second;
            if(overlays.Values.Any(x=>x.IsVisible)) {
                var text=ClockFormatter.Render(now,store.Current,calendar,weather.Read(store.Current.City));
                foreach(var a in detected) {
                    if (overlays.TryGetValue(a.Taskbar,out var view) && view.IsVisible) view.Draw(a,text);
                }
            }
            if(panel.IsVisible) panel.Tick(now);
            if(settingsWindow is { IsVisible: true, WindowState: not WindowState.Minimized })
                settingsWindow.Page.Tick(now);
        }
        if(store.Current.City is { } city && weatherRequest is null && utc>=nextWeather) RefreshWeather(city);
    }
    private async void RefreshWeather(City city)
    {
        var source=new CancellationTokenSource(); weatherRequest=source;
        nextWeather=DateTime.UtcNow.AddMinutes(10);
        try { await weather.Refresh(city, source.Token, directWeatherConnection); }
        catch(OperationCanceledException) when(source.IsCancellationRequested) { }
        finally { source.Dispose(); if(weatherRequest==source) weatherRequest=null; }
    }
    private async void Scan()
    {
        scanning=true; nextScan=DateTime.UtcNow.AddSeconds(2);
        try {
            var previous=detected;
            var result=await Task.Run(()=>ClockLocator.Find(previous));
            if(stopping) return;
            detected=result.MergeWith(previous,lastMissing); otherClock=result.OtherClock;
            lastMissing=result.Missing;
            // A visible taskbar may momentarily lack its XAML clock during a reveal.
            if (detected.Any(anchor => lastMissing.Contains(anchor.Taskbar)))
                nextScan = DateTime.UtcNow.AddMilliseconds(250);
            foreach(var id in overlays.Keys.Except(detected.Select(a=>a.Taskbar)).ToArray()) {
                overlays[id].Close(); overlays.Remove(id);
            }
            UpdateStatus(); RefreshVisible(DateTime.Now);
            bool requested = firstScan && (wantSettings || store.ReadError is not null || inputError is not null || !store.Current.Clock.Enabled);
            firstScan=false;
            if (awaitingVisible && !result.Retracted) {
                awaitingVisible=false;
                requested |= otherClock && detected.Length==0;
            }
            if(requested) OpenSettings();
        } finally { scanning=false; }
    }
    private void UpdateStatus()
    {
        if (settingsWindow is null) return;
        settingsWindow.Page.Status = !store.Current.Clock.Enabled ? "原位时钟已关闭。再次运行 Yxrl.exe 可打开设置。" :
            inputError ?? (detected.Length>0 ? "已连接任务栏原时钟。左键打开月历，再点收起。" :
            otherClock ? "优效日历正在使用原时间位置。正常退出优效后，这里会自动接入。" :
            "正在等待 Windows 任务栏显示原时钟。检测到后会自动接入。");
    }
    private void RefreshVisible(DateTime now, bool recolor=false)
    {
        var clickable = new List<ClockAnchor>();
        ClockText? text = null;
        foreach(var a in detected) {
            overlays.TryGetValue(a.Taskbar,out var view);
            if (mouse is null || !store.Current.Clock.Enabled || !Native.IsWindowVisible(a.Taskbar) || !Native.GetWindowRect(a.Taskbar,out var bar) ||
                a.PlaceAt(bar,Native.GetDpiForWindow(a.Taskbar)) is not { } placement) {
                if (view?.IsVisible==true) view.Hide();
                continue;
            }
            if(view is null) { view=new ClockOverlay(); overlays[a.Taskbar]=view; }
            if (recolor || !view.IsVisible)
                view.Colorize(ClockOverlay.SampleBackground(a.Taskbar,placement.Bounds,placement.Visible,bar));
            if (!view.IsVisible) {
                text ??= ClockFormatter.Render(now,store.Current,calendar,weather.Read(store.Current.City));
                view.Draw(a,text);
            }
            if (!view.Place(a.Taskbar,placement)) {
                view.Hide();
                continue;
            }
            if (bar == a.Bar) clickable.Add(a);
        }
        mouse?.Update(clickable.ToArray(),panel.ObserveOutsideClicks);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        stopping=true; timer?.Stop();
        if(owner) {
            reminderDelivery?.Dispose();
            SystemEvents.UserPreferenceChanged-=PreferenceChanged;
            listener?.Unregister(null); mouse?.Dispose(); weatherRequest?.Cancel(); weather?.Dispose();
            if(panel is not null) panel.Finish();
            foreach(var view in overlays.Values.ToArray()) view.Close();
            instance?.ReleaseMutex();
        }
        openSignal?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
}
