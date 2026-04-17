using System.Collections.Concurrent;
using FluentAssertions;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class AoiFilterTests
{
    private static ConcurrentDictionary<int, (float X, float Y)> World(params (int id, float x, float y)[] entries)
    {
        var d = new ConcurrentDictionary<int, (float X, float Y)>();
        foreach (var (id, x, y) in entries) d[id] = (x, y);
        return d;
    }

    [Fact]
    public void EmptyWorld_ReturnsZero()
    {
        var w = World();
        AoiFilter.Naive(w, 1, 0, 0, 100).Should().Be(0);
        AoiFilter.Optimized(w, 1, 0, 0, 100).Should().Be(0);
    }

    [Fact]
    public void ExcludesSelf()
    {
        var w = World((1, 0, 0), (2, 10, 10));
        AoiFilter.Naive(w, 1, 0, 0, 100).Should().Be(1);
        AoiFilter.Optimized(w, 1, 0, 0, 100).Should().Be(1);
    }

    [Fact]
    public void RadiusBoundary_Inclusive()
    {
        // exactly on boundary: distance² = radius² → included (≤)
        var w = World((2, 100, 0));
        AoiFilter.Naive(w, 1, 0, 0, 100).Should().Be(1);
        AoiFilter.Optimized(w, 1, 0, 0, 100).Should().Be(1);
    }

    [Fact]
    public void RadiusBoundary_OneOutOneIn()
    {
        var w = World((2, 50, 0), (3, 150, 0));
        AoiFilter.Naive(w, 1, 0, 0, 100).Should().Be(1);
        AoiFilter.Optimized(w, 1, 0, 0, 100).Should().Be(1);
    }

    [Theory]
    [InlineData(42, 100, 200f)]
    [InlineData(7, 500, 150f)]
    [InlineData(99, 2_000, 80f)]
    public void NaiveAndOptimized_AgreeOnRandomWorlds(int seed, int playerCount, float radius)
    {
        var rng = new Random(seed);
        var w = new ConcurrentDictionary<int, (float X, float Y)>();
        for (int i = 1; i <= playerCount; i++)
            w[i] = (rng.NextSingle() * 1200f, rng.NextSingle() * 720f);

        var (mx, my) = (w[1].X, w[1].Y);

        var naive = AoiFilter.Naive(w, 1, mx, my, radius);
        var optim = AoiFilter.Optimized(w, 1, mx, my, radius);

        // Optimized caps at 256 (stack-rented buffer); naive does not.
        // Use Math.Min for the upper bound when worlds are dense.
        Math.Min(naive, 256).Should().Be(optim);
    }
}
