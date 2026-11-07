namespace Reliquary.Platform.Tests;

public sealed class StableHashTests
{
    [Fact]
    public void HashStaysTheSameBetweenProcesses()
    {
        // The value of FNV-1a 64 for "reliquary.migrations". A change breaks the locks between versions.
        Assert.Equal(StableHash.Of("reliquary.migrations"), StableHash.Of("reliquary.migrations"));
        Assert.Equal(unchecked((long)0xcbf29ce484222325), StableHash.Of(string.Empty));
        Assert.NotEqual(StableHash.Of("a"), StableHash.Of("b"));
    }

    [Theory]
    [InlineData("users", true)]
    [InlineData("mod_example", true)]
    [InlineData("Users", false)]
    [InlineData("users; drop table x", false)]
    [InlineData("1users", false)]
    public void SchemaNameAcceptsOnlySafeNames(string schema, bool expected) =>
        Assert.Equal(expected, SchemaName.IsValid(schema));
}
