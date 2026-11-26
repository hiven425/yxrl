using Yxrl.Desktop;

internal static class TaskbarChecks
{
    public static void Verify()
    {
        var monitor = new PixelRect(0,0,3072,1920);
        var bar = new PixelRect(0,1848,3072,1920);
        var clock = new PixelRect(2939,1848,3054,1920);
        var anchor = new ClockAnchor(1,clock,bar,monitor,144,monitor);
        Equal(new ClockPlacement(clock,clock),anchor.PlaceAt(bar,144));

        var inMotion = anchor.PlaceAt(bar.Offset(0,40),144);
        Equal(new PixelRect(2939,1888,3054,1960),inMotion?.Bounds);
        Equal(new PixelRect(0,0,115,32),inMotion?.Clip);
        Equal(null,anchor.PlaceAt(bar.Offset(0,70),144));
        Equal(null,anchor.PlaceAt(bar.Offset(0,-1848),144));
        Equal(null,anchor.PlaceAt(bar with { Right = 2560 },144));
        Equal(null,anchor.PlaceAt(bar,120));

        var leftMonitor = new PixelRect(-1400,-100,0,900);
        var leftBar = new PixelRect(-1400,-100,-1352,900);
        var leftClock = new PixelRect(-1400,835,-1352,900);
        var vertical = new ClockAnchor(2,leftClock,leftBar,leftMonitor,96,leftMonitor);
        Equal(new PixelRect(20,0,48,65),vertical.PlaceAt(leftBar.Offset(-20,0),96)?.Clip);

        var deferred = new Discovery([],[1],OtherClock:false,Retracted:true);
        Equal(anchor,deferred.MergeWith([anchor]).Single());
        var replaced = new Discovery([],[],OtherClock:true,Retracted:false);
        Equal(0,replaced.MergeWith([anchor]).Length);
        var updated = anchor with { Bounds = clock with { Left = 2925 } };
        var measured = new Discovery([updated],[],OtherClock:false,Retracted:false);
        Equal(updated,measured.MergeWith([anchor]).Single());
        Console.WriteLine("通过：任务栏收放保留原位、动画裁剪、DPI/布局失效和释放占位。");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"期望 {expected}，得到 {actual}");
    }
}
