using System.Threading.Channels;
using Server.Persistence;

namespace Server.Services;

/// <summary>
/// Write-Behind 버퍼. Hub 의 Leave 경로는 여기에 1회 TryWrite 만 호출하면 돌아감 (μs 단위).
/// 실제 DB 쓰기는 MatchFlushJob 이 배치/트랜잭션으로 흡수.
/// </summary>
public sealed class MatchWriteQueue
{
    private readonly Channel<MatchRecord> _channel = Channel.CreateBounded<MatchRecord>(
        new BoundedChannelOptions(capacity: 65536)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // 폭주 시 오래된 쓰기 드롭 → 메인 스레드 보호
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<MatchRecord> Reader => _channel.Reader;

    public bool TryEnqueue(MatchRecord record) => _channel.Writer.TryWrite(record);

    public int ApproxBacklog => _channel.Reader.Count;
}
