using FluentAssertions;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class SnapshotServiceTests
{
    [Fact]
    public void Set_PutsPlayerInRoom()
    {
        var s = new SnapshotService();
        s.Set("world", 1, 10f, 20f);
        s.RawRoom("world").Should().ContainKey(1);
        s.RawRoom("world")[1].Should().Be((10f, 20f));
    }

    [Fact]
    public void Remove_DeletesOnlyTargetPlayer()
    {
        var s = new SnapshotService();
        s.Set("world", 1, 0f, 0f);
        s.Set("world", 2, 0f, 0f);
        s.Remove("world", 1);
        s.RawRoom("world").Should().NotContainKey(1).And.ContainKey(2);
    }

    [Fact]
    public void RoomList_ReturnsCountsPerRoom()
    {
        var s = new SnapshotService();
        s.Set("a", 1, 0f, 0f);
        s.Set("a", 2, 0f, 0f);
        s.Set("b", 3, 0f, 0f);

        var rooms = s.RoomList();
        rooms.Should().BeEquivalentTo(new[] { ("a", 2), ("b", 1) });
    }

    [Fact]
    public void SerializeFlat_EmitsIdXyTriples()
    {
        var s = new SnapshotService();
        s.Set("r", 7, 1.5f, 2.5f);
        s.Set("r", 8, 3.5f, 4.5f);

        var flat = s.SerializeFlat("r");
        flat.Should().HaveCount(6);
        // order is non-deterministic; reassemble into set of triples
        var triples = new HashSet<(int, float, float)>();
        for (int i = 0; i < flat.Length; i += 3)
            triples.Add(((int)flat[i], flat[i + 1], flat[i + 2]));
        triples.Should().BeEquivalentTo(new[] { (7, 1.5f, 2.5f), (8, 3.5f, 4.5f) });
    }

    [Fact]
    public void SerializeFlat_UnknownRoom_ReturnsEmpty()
    {
        var s = new SnapshotService();
        s.SerializeFlat("nope").Should().BeEmpty();
    }
}
