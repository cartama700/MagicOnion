using FluentAssertions;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class InMemoryLeaderboardTests
{
    [Fact]
    public async Task AddAndTop_ReturnsDescendingByScore()
    {
        var lb = new InMemoryLeaderboard();
        await lb.AddScoreAsync("r", 1, 5);
        await lb.AddScoreAsync("r", 2, 50);
        await lb.AddScoreAsync("r", 3, 25);

        var top = await lb.TopAsync("r", 10);
        top.Select(t => t.PlayerId).Should().Equal(2, 3, 1);
        top.Select(t => t.Score).Should().Equal(50.0, 25.0, 5.0);
    }

    [Fact]
    public async Task AddScore_AccumulatesPerPlayer()
    {
        var lb = new InMemoryLeaderboard();
        await lb.AddScoreAsync("r", 7, 10);
        await lb.AddScoreAsync("r", 7, 15);
        var top = await lb.TopAsync("r", 1);
        top[0].Should().Be((7, 25.0));
    }

    [Fact]
    public async Task Remove_DropsPlayerFromTop()
    {
        var lb = new InMemoryLeaderboard();
        await lb.AddScoreAsync("r", 1, 10);
        await lb.AddScoreAsync("r", 2, 20);
        await lb.RemoveAsync("r", 2);
        var top = await lb.TopAsync("r", 10);
        top.Should().HaveCount(1);
        top[0].PlayerId.Should().Be(1);
    }

    [Fact]
    public async Task UnknownRoom_ReturnsEmpty()
    {
        var lb = new InMemoryLeaderboard();
        (await lb.TopAsync("ghost", 5)).Should().BeEmpty();
    }
}

public class KpiSnapshotTests
{
    [Fact]
    public void Record_TracksPeakAndAverage()
    {
        var k = new KpiSnapshot();
        k.Record(10, 100, 2.5);
        k.Record(50, 80, 3.0);
        k.Record(20, 120, 1.0);

        k.PeakPlayers.Should().Be(50);
        k.PeakPacketsPerSec.Should().Be(120);
        k.LastPacketsPerSec.Should().Be(120);
        k.LastAvgAoi.Should().Be(1.0);
        k.TotalSamples.Should().Be(3);
        k.TotalPackets.Should().Be(300);
        k.AvgPacketsPerSec.Should().Be(100.0);
    }

    [Fact]
    public void EmptySnapshot_ReturnsZeroAverage()
    {
        new KpiSnapshot().AvgPacketsPerSec.Should().Be(0);
    }
}
