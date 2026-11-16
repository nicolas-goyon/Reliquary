using Microsoft.Extensions.Time.Testing;

namespace Reliquary.BuildingBlocks.Tests;

public sealed class IdsTests
{
    [Fact]
    public void NewIdentifiersAreVersion7AndContainTheTimeOfCreation()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Ids.New();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(7, id.Version);
        Assert.InRange(UnixMilliseconds(id), before, after);
    }

    [Fact]
    public void NewIdentifiersFollowTheTimeOfCreation()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

        var first = Ids.New(time);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var second = Ids.New(time);

        Assert.Equal(7, first.Version);
        Assert.Equal(time.GetUtcNow().ToUnixTimeMilliseconds() - 1, UnixMilliseconds(first));
        Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0);
    }

    /// <summary>
    /// Reads the time of a UUIDv7 value: the first 48 bits are the Unix time in milliseconds (RFC 9562).
    /// </summary>
    private static long UnixMilliseconds(Guid id) => Convert.ToInt64(id.ToString("N")[..12], 16);
}
