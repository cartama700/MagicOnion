namespace Server.Lifecycle;

/// <summary>
/// Readiness gate for K8s. Flipped to false at SIGTERM so Service stops routing
/// new traffic; Liveness stays green until we're actually ready to exit.
/// </summary>
public sealed class ReadinessGate
{
    private int _ready = 1;
    public bool IsReady => Volatile.Read(ref _ready) == 1;
    public void MarkNotReady() => Volatile.Write(ref _ready, 0);
}
