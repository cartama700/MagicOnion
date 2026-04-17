using Server.Persistence;
using Server.Services;

namespace Server.Jobs;

/// <summary>
/// Write-Behind 플러셔. 100건 차거나 1초 경과 중 먼저 오는 조건으로 배치 INSERT.
/// </summary>
public sealed class MatchFlushJob : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly MatchWriteQueue _queue;
    private readonly IPlayerRepository _repo;
    private readonly ILogger<MatchFlushJob> _logger;

    public MatchFlushJob(MatchWriteQueue queue, IPlayerRepository repo, ILogger<MatchFlushJob> logger)
    {
        _queue = queue;
        _repo = repo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MatchFlushJob started — batch {Batch}, interval {Interval}s",
            BatchSize, FlushInterval.TotalSeconds);

        var buf = new List<MatchRecord>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            buf.Clear();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(FlushInterval);

            try
            {
                // Fill until batch full or interval elapsed.
                while (buf.Count < BatchSize && await _queue.Reader.WaitToReadAsync(cts.Token))
                {
                    while (buf.Count < BatchSize && _queue.Reader.TryRead(out var rec))
                        buf.Add(rec);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // interval timeout — flush whatever we have
            }

            if (buf.Count == 0) continue;

            try
            {
                await _repo.BulkInsertMatchesAsync(buf, stoppingToken);
                _logger.LogDebug("Flushed {Count} match records", buf.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Match flush failed — dropped {Count} records", buf.Count);
            }
        }

        // Drain on shutdown.
        buf.Clear();
        while (_queue.Reader.TryRead(out var rec)) buf.Add(rec);
        if (buf.Count > 0)
        {
            try { await _repo.BulkInsertMatchesAsync(buf, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "Final drain flush failed"); }
        }
    }
}
