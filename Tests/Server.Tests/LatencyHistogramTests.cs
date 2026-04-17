using FluentAssertions;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class LatencyHistogramTests
{
    [Fact]
    public void Empty_ReturnsZeros()
    {
        var h = new LatencyHistogram();
        var (p50, p95, p99, avg, n) = h.SnapshotAndReset();
        (p50, p95, p99, avg, n).Should().Be((0, 0, 0, 0.0, 0L));
    }

    [Fact]
    public void AllSameValue_AllPercentilesMatch()
    {
        var h = new LatencyHistogram();
        for (int i = 0; i < 1000; i++) h.Record(15);
        var (p50, p95, p99, _, n) = h.SnapshotAndReset();
        n.Should().Be(1000);
        // 15ms falls into bucket with upper bound 20
        p50.Should().Be(20);
        p95.Should().Be(20);
        p99.Should().Be(20);
    }

    [Fact]
    public void SpikySamples_P99IsHigherThanP50()
    {
        // p99 target = ceil(1000 * 0.99) = 990 samples. 980 fast means the spike bucket
        // is what pushes us past the 990 threshold → p99 falls into the slow bucket.
        var h = new LatencyHistogram();
        for (int i = 0; i < 980; i++) h.Record(5);
        for (int i = 0; i < 20;  i++) h.Record(500);
        var (p50, p95, p99, _, _) = h.SnapshotAndReset();
        p50.Should().BeLessOrEqualTo(10);
        p99.Should().BeGreaterOrEqualTo(100);
        p99.Should().BeGreaterThan(p50);
    }

    [Fact]
    public void SnapshotResets_State()
    {
        var h = new LatencyHistogram();
        h.Record(50);
        h.SnapshotAndReset();
        var (_, _, _, _, n) = h.SnapshotAndReset();
        n.Should().Be(0);
    }
}
