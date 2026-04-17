using System.Collections.Concurrent;

namespace Server.Endpoints;

// Phase 14 — Hybrid API. Stateful MagicOnion Hub 와 같은 프로세스에서 도는 Stateless Minimal API.
// 실제 라이브 게임의 프로파일/가챠/우편함처럼 짧은 요청-응답 패턴.
public static class ProfileEndpoints
{
    private static readonly ConcurrentDictionary<int, (string Name, int Level, long Coins)> Profiles = new();
    private static readonly ConcurrentDictionary<int, List<string>> Inbox = new();
    private static readonly string[] GachaPool =
        { "Common Sword", "Rare Bow", "Epic Staff", "Legendary Dragon", "Mythic Phoenix" };

    public static void MapProfile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile/{playerId:int}", (int playerId) =>
        {
            var p = Profiles.GetOrAdd(playerId, id => ($"bot-{id}", Random.Shared.Next(1, 50), 1000));
            return Results.Json(new { playerId, displayName = p.Name, level = p.Level, coins = p.Coins });
        });

        app.MapPost("/api/gacha/{playerId:int}", (int playerId) =>
        {
            var roll = GachaPool[Random.Shared.Next(GachaPool.Length)];
            Inbox.GetOrAdd(playerId, _ => new List<string>()).Add(roll);
            return Results.Json(new { playerId, drew = roll });
        });

        app.MapGet("/api/mail/{playerId:int}", (int playerId) =>
        {
            var items = Inbox.TryGetValue(playerId, out var v) ? v.ToArray() : Array.Empty<string>();
            return Results.Json(new { playerId, items });
        });
    }
}
