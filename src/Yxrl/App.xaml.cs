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
    private nint lastBar;
    private ClockMouse? mouse;
    private string? inputError;
    private DispatcherTimer? timer;
    private SettingsStore store=null!;
    private CalendarService calendar=null!;
    private WeatherService weather=null!;
    private CalendarWindow panel=null!;
    private CancellationTokenSource? weatherRequest;
    private long? weatherCity;

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
        panel=new(store,calendar,weather,ApplySettings,Shutdown);
        listener=ThreadPool.RegisterWaitForSingleObject(openSignal,(_,_)=>Dispatcher.BeginInvoke(OpenSettings),null,Timeout.Infinite,false);
        SystemEvents.UserPreferenceChanged+=PreferenceChanged;
        ApplySettings();
        timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(120)};
        timer.Tick+=(_,_)=>Tick(); timer.Start(); Tick();
    }
    private void PreferenceChanged(object sender,UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => {
        if(stopping) return;
        Theme.Apply();
        foreach(var view in overlays.Values) view.Hide();
        RefreshVisible();
    });
    private ClockAnchor? ActiveAnchor => detected.FirstOrDefault(x=>x.Taskbar==lastBar) ?? detected.FirstOrDefault();
    private void OpenSettings()
    {
        if(stopping) return;
        if(firstScan) { wantSettings=true; return; }
        panel.Present(ActiveAnchor,true);
    }
    private void ApplySettings()
    {
        if(store.Current.Clock.Enabled && mouse is null) {
            try { mouse=new ClockMouse(a=>Dispatcher.BeginInvoke(()=>ClickClock(a))); inputError=null; }
            catch(Win32Exception ex) { inputError=ex.Message; }
        } else if(!store.Current.Clock.Enabled && mouse is not null) { mouse.Dispose(); mouse=null; }
        nextScan=DateTime.MinValue;
        lastRenderSecond=-1;
        if(weatherCity!=store.Current.City?.Id) {
            weatherCity=store.Current.City?.Id; weatherRequest?.Cancel(); nextWeather=DateTime.MinValue;
        }
        UpdateStatus(); RefreshVisible();
    }
    private void ClickClock(ClockAnchor anchor)
    {
        if(stopping || mouse is null || !detected.Contains(anchor)) return;
        if(panel.IsVisible && lastBar==anchor.Taskbar) panel.Dismiss();
        else { lastBar=anchor.Taskbar; panel.Present(anchor,false); }
    }
    private void Tick()
    {
        if(stopping) return;
        var now=DateTime.Now; var utc=DateTime.UtcNow;
        if(Native.GetWindowRect(Native.FindWindow("Shell_TrayWnd",null),out var primary) && primary!=lastPrimary) {
            lastPrimary=primary; nextScan=DateTime.MinValue;
        }
        if(!scanning && utc>=nextScan) Scan();
        RefreshVisible();
        var second=now.Ticks/TimeSpan.TicksPerSecond;
        if(lastRenderSecond!=second) {
            lastRenderSecond=second;
            if(overlays.Values.Any(x=>x.IsVisible)) {
                var text=ClockFormatter.Render(now,store.Current,calendar,weather.Read(store.Current.City));
                foreach(var view in overlays.Values) view.Draw(text,now);
            }
            if(panel.IsVisible) panel.Tick(now);
        }
        if(store.Current.City is { } city && weatherRequest is null && utc>=nextWeather) RefreshWeather(city);
    }
    private async void RefreshWeather(City city)
    {
        var source=new CancellationTokenSource(); weatherRequest=source;
        nextWeather=DateTime.UtcNow.AddMinutes(10);
        try { await weather.Refresh(city,source.Token); }
        catch(OperationCanceledException) when(source.IsCancellationRequested) { }
        finally { source.Dispose(); if(weatherRequest==source) weatherRequest=null; }
    }
    private async void Scan()
    {
        scanning=true; nextScan=DateTime.UtcNow.AddSeconds(2);
        try {
            var result=await Task.Run(ClockLocator.Find);
            if(stopping) return;
            detected=result.Clocks; otherClock=result.OtherClock;
            foreach(var id in overlays.Keys.Except(detected.Select(a=>a.Taskbar)).ToArray()) {
                overlays[id].Close(); overlays.Remove(id);
            }
            UpdateStatus(); RefreshVisible();
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
        panel.IntegrationStatus = !store.Current.Clock.Enabled ? "原位时钟已关闭。再次运行 Yxrl.exe 可打开设置。" :
            inputError ?? (detected.Length>0 ? "已连接任务栏原时钟。左键打开月历，再点收起。" :
            otherClock ? "优效日历正在使用原时间位置。正常退出优效后，这里会自动接入。" :
            "正在等待 Windows 任务栏显示原时钟。检测到后会自动接入。");
    }
    private void RefreshVisible()
    {
        List<ClockAnchor> visible=[];
        foreach(var a in detected) {
            var exposed=mouse is not null && Native.IsWindowVisible(a.Taskbar) && Native.GetWindowRect(a.Taskbar,out var bar)
                && bar==a.Bar && Native.OnTaskbar(a.Taskbar,a.Bounds.Center);
            overlays.TryGetValue(a.Taskbar,out var view);
            if(!exposed) { if(view?.IsVisible==true)view.Hide(); continue; }
            if(view is null) { view=new ClockOverlay(); overlays[a.Taskbar]=view; }
            if(!view.IsVisible) { view.Colorize(ClockOverlay.SampleBackground(a)); lastRenderSecond=-1; }
            view.Place(a); visible.Add(a);
        }
        mouse?.Update(visible.ToArray());
    }
    protected override void OnExit(ExitEventArgs e)
    {
        stopping=true; timer?.Stop();
        if(owner) {
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
