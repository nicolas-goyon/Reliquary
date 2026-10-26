namespace Reliquary.BuildingBlocks.Tests;

public sealed class IdsTests
{
    [Fact]
    public void NewIdentifiersAreVersion7AndFollowTheTimeOfCreation()
    {
        var first = Ids.New();
        Thread.Sleep(2);
        var second = Ids.New();

        Assert.Equal(7, first.Version);
        Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0);
    }
}
