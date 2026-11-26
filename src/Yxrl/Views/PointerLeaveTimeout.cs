namespace Yxrl.Views;

internal sealed class PointerLeaveTimeout
{
    private const long DelayMilliseconds = 5000;
    private long? outsideSince;

    public void Reset() => outsideSince = null;

    public bool ShouldDismiss(long milliseconds, bool outside)
    {
        if (!outside) {
            Reset();
            return false;
        }

        outsideSince ??= milliseconds;
        return milliseconds - outsideSince.Value >= DelayMilliseconds;
    }
}
