using Cysharp.Runtime.Multicast.Distributed.Redis;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Server.Endpoints;
using Server.Jobs;
using Server.Lifecycle;
using Server.Persistence;
using Server.Services;
using Server.Services.Llm;
using Server.Services.Ops;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5050, o => o.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(5001, o => o.Protocols = HttpProtocols.Http2);
});

builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<SnapshotService>();
builder.Services.AddSingleton<OptimizationMode>();
builder.Services.AddSingleton<KpiSnapshot>();
builder.Services.AddSingleton<LatencyHistogram>();
builder.Services.AddSingleton<MatchWriteQueue>();
builder.Services.AddHostedService<MatchFlushJob>();
builder.Services.AddSingleton<ReadinessGate>();
builder.Services.AddHostedService<GracefulShutdownService>();
builder.Services.AddSingleton<RankingSnapshotJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RankingSnapshotJob>());
builder.Services.AddHostedService<KpiRollupJob>();
builder.Services.AddHostedService<ConsoleStatus>();
builder.Services.AddGrpc();

// Phase 16 — LLM Provider Registry. 기본 Mock 이라 API 키 없어도 전 경로 동작.
builder.Services.AddHttpClient();
var llmOpt = new LlmOptions();
builder.Configuration.GetSection("Llm").Bind(llmOpt);
builder.Services.AddSingleton(llmOpt);
builder.Services.AddSingleton<ILlmProvider>(sp => llmOpt.Provider?.ToLowerInvariant() switch
{
    "openai" => new OpenAiLlmProvider(sp.GetRequiredService<IHttpClientFactory>(), llmOpt),
    _        => new MockLlmProvider(),
});
builder.Services.AddSingleton<SpikeAnalyzer>();

var magicOnion = builder.Services.AddMagicOnion();
var backplaneConn = builder.Configuration.GetConnectionString("RedisBackplane")
                 ?? builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(backplaneConn))
{
    magicOnion.UseRedisGroup(
        configure: o => o.ConnectionMultiplexer = ConnectionMultiplexer.Connect(backplaneConn),
        registerAsDefault: true);
}

// --- Optional persistence (graceful-degrade when connection strings empty) ---
var mysqlConn = builder.Configuration.GetConnectionString("Mysql");
var redisConn = builder.Configuration.GetConnectionString("Redis");

if (!string.IsNullOrWhiteSpace(mysqlConn))
{
    builder.Services.AddSingleton<IPlayerRepository>(_ => new PlayerRepository(mysqlConn));
}
else
{
    builder.Services.AddSingleton<IPlayerRepository, NullPlayerRepository>();
}

if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
    builder.Services.AddSingleton<ILeaderboard, RedisLeaderboard>();
}
else
{
    builder.Services.AddSingleton<ILeaderboard, InMemoryLeaderboard>();
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(mysqlConn))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    try { MigrationRunner.EnsureSchema(mysqlConn, logger); }
    catch (Exception ex) { logger.LogWarning(ex, "DB migration skipped — server will run without persistence"); }
}
else
{
    app.Logger.LogInformation("ConnectionStrings:Mysql empty — persistence disabled (in-memory mode)");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapMagicOnionService();

app.MapGet("/api/metrics", (MetricsService m, OptimizationMode o, KpiSnapshot k) => new
{
    players = m.ConnectedPlayers,
    packets = k.LastPacketsPerSec,
    avgAoi = k.LastAvgAoi,
    gcAllocatedBytes = GC.GetTotalAllocatedBytes(),
    gen0 = GC.CollectionCount(0),
    gen1 = GC.CollectionCount(1),
    gen2 = GC.CollectionCount(2),
    // 토글 효과를 즉시 보여주는 rate 지표 (KpiRollupJob 1초 주기 델타 기반)
    allocMbPerSec = k.LastAllocRateMb,
    gen0PerSec = k.LastGen0PerSec,
    gen1PerSec = k.LastGen1PerSec,
    optimized = o.IsOn,
});

app.MapGet("/api/kpi", (KpiSnapshot k) => new
{
    peakPlayers = k.PeakPlayers,
    peakPacketsPerSec = k.PeakPacketsPerSec,
    avgPacketsPerSec = k.AvgPacketsPerSec,
    totalPackets = k.TotalPackets,
    samples = k.TotalSamples,
    p50Ms = k.LastP50Ms,
    p95Ms = k.LastP95Ms,
    p99Ms = k.LastP99Ms,
    avgLatencyMs = k.LastAvgLatencyMs,
    lastUpdatedUtc = k.LastUpdatedUtc,
});

app.MapGet("/api/ranking", (RankingSnapshotJob r, string? room) =>
{
    var key = room ?? "world";
    var top = r.Latest.TryGetValue(key, out var v) ? v : Array.Empty<(int, double)>();
    return Results.Json(new { room = key, top = top.Select(t => new { playerId = t.PlayerId, score = t.Score }) });
});

app.MapGet("/api/rooms", (SnapshotService s) =>
    Results.Json(s.RoomList().Select(r => new { room = r.Room, count = r.Count })));

app.MapGet("/api/snapshot", (SnapshotService s, string? room) =>
    Results.Json(new { room = room ?? "world", p = s.SerializeFlat(room ?? "world") }));

app.MapPost("/api/optimize", (OptimizationMode o, bool on) =>
{
    o.Set(on);
    return Results.Ok(new { optimized = o.IsOn });
});

// Phase 13 — health probes (분리된 readiness/liveness)
app.MapGet("/health/live",  () => Results.Ok("ok"));
app.MapGet("/health/ready", (ReadinessGate g) =>
    g.IsReady ? Results.Ok("ready") : Results.Json(new { ready = false }, statusCode: 503));

// Phase 14 — Hybrid API (Stateless Minimal API endpoints)
app.MapProfile();

// Phase 16 — AI 운영 보조자 (SSE 스트리밍)
app.MapOps();

app.MapGet("/api/leaderboard", async (ILeaderboard lb, string? room, int? n) =>
{
    var top = await lb.TopAsync(room ?? "world", n ?? 10);
    return Results.Json(new { room = room ?? "world", top = top.Select(t => new { playerId = t.PlayerId, score = t.Score }) });
});

app.Run();

public partial class Program { } // for WebApplicationFactory<Program>
