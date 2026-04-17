using FluentAssertions;
using Server.Persistence;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class MatchWriteQueueTests
{
    [Fact]
    public async Task TryEnqueue_ReaderGetsRecord()
    {
        var q = new MatchWriteQueue();
        var rec = new MatchRecord(Guid.CreateVersion7(), 1, "room-00", DateTime.UtcNow, DateTime.UtcNow, 42);
        q.TryEnqueue(rec).Should().BeTrue();

        var read = await q.Reader.ReadAsync();
        read.Should().Be(rec);
    }

    [Fact]
    public void Backlog_TracksCount()
    {
        var q = new MatchWriteQueue();
        for (int i = 0; i < 5; i++)
            q.TryEnqueue(new MatchRecord(Guid.CreateVersion7(), i, "r", DateTime.UtcNow, DateTime.UtcNow, 0));
        q.ApproxBacklog.Should().Be(5);
    }

    [Fact]
    public void UuidV7_IsTimeOrdered()
    {
        // sanity: Guid.CreateVersion7 emits monotonic (strictly-or-equal-time) ids.
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.CreateVersion7()).ToArray();
        var sorted = ids.OrderBy(g => g.ToString()).ToArray();
        // Not guaranteed strict across all bits, but timestamp prefix (first 6 bytes) must be monotonic
        // within same-ms generation. Compare prefixes.
        for (int i = 1; i < ids.Length; i++)
        {
            var a = ids[i - 1].ToByteArray()[..6];
            var b = ids[i].ToByteArray()[..6];
            var cmp = a.AsSpan().SequenceCompareTo(b);
            cmp.Should().BeLessOrEqualTo(0);
        }
    }
}
