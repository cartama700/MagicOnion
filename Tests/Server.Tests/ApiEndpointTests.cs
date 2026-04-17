using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Server.Services;
using Xunit;

namespace Server.Tests;

public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Metrics_ReturnsExpectedShape()
    {
        var client = _factory.CreateClient();
        var doc = await client.GetFromJsonAsync<MetricsDto>("/api/metrics");
        doc.Should().NotBeNull();
        doc!.Players.Should().Be(0);
        doc.Optimized.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_EmptyRoom_ReturnsEmptyArray()
    {
        var client = _factory.CreateClient();
        var doc = await client.GetFromJsonAsync<SnapshotDto>("/api/snapshot?room=ghost");
        doc!.P.Should().BeEmpty();
    }

    [Fact]
    public async Task Optimize_TogglesMode()
    {
        var client = _factory.CreateClient();
        var on  = await (await client.PostAsync("/api/optimize?on=true",  null)).Content.ReadFromJsonAsync<OptDto>();
        on!.Optimized.Should().BeTrue();
        var off = await (await client.PostAsync("/api/optimize?on=false", null)).Content.ReadFromJsonAsync<OptDto>();
        off!.Optimized.Should().BeFalse();
    }

    [Fact]
    public async Task Rooms_StartsEmpty()
    {
        var client = _factory.CreateClient();
        var rooms = await client.GetFromJsonAsync<RoomDto[]>("/api/rooms");
        rooms.Should().BeEmpty();
    }

    [Fact]
    public async Task HealthReady_Returns200WhenReady()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health/ready");
        resp.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Profile_ReturnsStubbedData()
    {
        var client = _factory.CreateClient();
        var doc = await client.GetFromJsonAsync<ProfileDto>("/api/profile/42");
        doc!.PlayerId.Should().Be(42);
        doc.DisplayName.Should().Be("bot-42");
        doc.Level.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Gacha_AppendsToMail()
    {
        var client = _factory.CreateClient();
        await client.PostAsync("/api/gacha/77", null);
        var mail = await client.GetFromJsonAsync<MailDto>("/api/mail/77");
        mail!.Items.Should().HaveCountGreaterOrEqualTo(1);
    }

    private sealed record ProfileDto(int PlayerId, string DisplayName, int Level, long Coins);
    private sealed record MailDto(int PlayerId, string[] Items);

    [Fact]
    public void SnapshotService_IsRegisteredAsSingleton()
    {
        // sanity: DI wiring picks up the singleton we expect
        using var scope = _factory.Services.CreateScope();
        var s1 = scope.ServiceProvider.GetRequiredService<SnapshotService>();
        var s2 = _factory.Services.GetRequiredService<SnapshotService>();
        s1.Should().BeSameAs(s2);
    }

    private sealed record MetricsDto(long Players, long Packets, double AvgAoi, long GcAllocatedBytes,
                                     int Gen0, int Gen1, int Gen2, bool Optimized);
    private sealed record SnapshotDto(string Room, float[] P);
    private sealed record OptDto(bool Optimized);
    private sealed record RoomDto(string Room, int Count);
}
