using Reliquary.BuildingBlocks.Api;

namespace Reliquary.BuildingBlocks.Tests;

public sealed class PagingTests
{
    [Fact]
    public void MissingLimitGivesTheDefaultLimit()
    {
        Assert.True(PageRequest.TryCreate(null, null, out var request, out var error));

        Assert.Null(error);
        Assert.Equal(PageRequest.DefaultLimit, request.Limit);
        Assert.Null(request.Cursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PageRequest.MaxLimit + 1)]
    public void LimitOutsideTheRangeIsAFieldError(int limit)
    {
        Assert.False(PageRequest.TryCreate(limit, null, out _, out var error));

        Assert.NotNull(error);
        Assert.Equal("limit", error.Field);
        Assert.Equal(FieldErrorCodes.OutOfRange, error.Code);
    }

    [Fact]
    public void CursorGivesBackThePositionThatItEncodes()
    {
        var position = new Position("Lightning Bolt", Guid.Parse("0197f3a0-0000-7000-8000-000000000001"));

        var cursor = Cursors.Encode(position);

        Assert.True(Cursors.TryDecode<Position>(cursor, out var decoded));
        Assert.Equal(position, decoded);
        Assert.DoesNotContain("=", cursor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a cursor")]
    [InlineData("bm90IGpzb24")]
    public void CorruptCursorIsRejected(string cursor) =>
        Assert.False(Cursors.TryDecode<Position>(cursor, out _));

    private sealed record Position(string Name, Guid Id);
}
