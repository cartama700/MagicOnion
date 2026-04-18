using System.Threading;
using Grpc.Net.Client;
using MagicOnion.Client;
using Shared;

// Args: <botCount> <serverUrl(s, comma-separated)> <tickMs> [roomCount] [scenario: even|herd|cluster] [connectWindowMs]
var botCount    = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 1000;
var serverArg   = args.Length > 1 ? args[1] : "http://localhost:5001";
var tickMs      = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 100;
var roomCount   = args.Length > 3 && int.TryParse(args[3], out var r) ? Math.Max(1, r) : 1;
var scenario    = args.Length > 4 ? args[4].ToLowerInvariant() : "even";
var windowArg   = args.Length > 5 && int.TryParse(args[5], out var w) ? w : 0;

var serverUrls  = serverArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

Console.WriteLine(
    $"[BotClients] {botCount} bots / {roomCount} rooms / {scenario} / {tickMs}ms tick");
Console.WriteLine($"[BotClients] targets: {string.Join(", ", serverUrls)}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Connect window: 각 봇이 [0, window) 범위에서 랜덤하게 connect → 동시 접속 폭주 완화.
// 6번째 인자로 명시 가능. 0(미지정)이면 시나리오별 기본값.
var connectWindowMs = windowArg > 0 ? windowArg : scenario switch
{
    "herd" => Math.Max(15000, botCount * 20),   // blast: 봇당 ~20ms (1000봇 ≈ 20s) → 초당 ~50봇씩 등장
    _      => Math.Max(30000, botCount * 60),   // realistic ramp: 봇당 ~60ms (1000봇 ≈ 60s) → 초당 ~17봇씩 등장
};
Console.WriteLine($"[BotClients] connect window: {connectWindowMs}ms (random jitter per bot)");

var connected = 0;
var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
_ = Task.Run(async () =>
{
    while (!progressCts.IsCancellationRequested)
    {
        await Task.Delay(1000, progressCts.Token).ContinueWith(_ => { });
        var c = Volatile.Read(ref connected);
        Console.WriteLine($"[BotClients] connected {c}/{botCount}");
        if (c >= botCount) break;
    }
});

var tasks = new Task[botCount];
for (var i = 0; i < botCount; i++)
{
    var id = i + 1;
    var roomId = $"room-{i % roomCount:D2}";
    var url = serverUrls[i % serverUrls.Length];
    var connectDelayMs = Random.Shared.Next(0, connectWindowMs);
    tasks[i] = RunBotAsync(id, roomId, url, tickMs, scenario, connectDelayMs, () => Interlocked.Increment(ref connected), cts.Token);
}

await Task.WhenAll(tasks);

static async Task RunBotAsync(int id, string roomId, string url, int tickMs, string scenario, int connectDelayMs, Action onConnected, CancellationToken ct)
{
    const float worldW = 1200f, worldH = 720f;
    const float clusterX = 600f, clusterY = 360f;

    try
    {
        if (connectDelayMs > 0) await Task.Delay(connectDelayMs, ct);

        using var channel = GrpcChannel.ForAddress(url);
        var receiver = new NullReceiver();
        var hub = await StreamingHubClient.ConnectAsync<IMovementHub, IMovementHubReceiver>(channel, receiver, cancellationToken: ct);

        var rng = new Random(id);
        float x = rng.NextSingle() * worldW;
        float y = rng.NextSingle() * worldH;

        await hub.JoinAsync(id, roomId, x, y);
        onConnected();

        while (!ct.IsCancellationRequested)
        {
            if (scenario == "cluster")
            {
                // Drift toward cluster point with jitter → AOI density spike (O(N²) broadcast).
                var dx = clusterX - x;
                var dy = clusterY - y;
                var len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 1f) { dx /= len; dy /= len; }
                x += dx * 4f + (rng.NextSingle() - 0.5f) * 3f;
                y += dy * 4f + (rng.NextSingle() - 0.5f) * 3f;
            }
            else
            {
                x += (rng.NextSingle() - 0.5f) * 20f;
                y += (rng.NextSingle() - 0.5f) * 20f;
            }
            x = Math.Clamp(x, 0, worldW);
            y = Math.Clamp(y, 0, worldH);

            await hub.MoveAsync(new PlayerMoveDto
            {
                PlayerId = id, X = x, Y = y,
                SentAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            await Task.Delay(tickMs, ct);
        }

        await hub.LeaveAsync();
        await hub.DisposeAsync();
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[bot {id}] {ex.GetType().Name}: {ex.Message}");
    }
}

sealed class NullReceiver : IMovementHubReceiver
{
    public void OnPlayerMoved(PlayerMoveDto moveData) { }
    public void OnPlayerJoined(PlayerMoveDto moveData) { }
    public void OnPlayerLeft(int playerId) { }
}
