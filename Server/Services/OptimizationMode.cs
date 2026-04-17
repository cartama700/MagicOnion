namespace Server.Services;

public sealed class OptimizationMode
{
    private int _enabled;
    public bool IsOn => Volatile.Read(ref _enabled) == 1;
    public void Set(bool on) => Volatile.Write(ref _enabled, on ? 1 : 0);
}
