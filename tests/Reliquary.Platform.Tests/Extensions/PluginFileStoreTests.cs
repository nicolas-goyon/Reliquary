using System.Security.Cryptography;
using Reliquary.BuildingBlocks.Api;
using Reliquary.Platform.Extensions;

namespace Reliquary.Platform.Tests.Extensions;

public sealed class PluginFileStoreTests
{
    private static readonly byte[] Small = [1, 2, 3];

    [Fact]
    public void CorrectFilesHaveNoErrors()
    {
        var errors = PluginFileStore.Check([new PluginFile("dist/index.js", Small), new PluginFile("dist/assets/icon-1.2.svg", Small), new PluginFile("ocr_data.wasm", Small)]);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("../index.js")]
    [InlineData("dist/../index.js")]
    [InlineData("/index.js")]
    [InlineData(".env.js")]
    [InlineData("dist//index.js")]
    [InlineData("dist\\index.js")]
    [InlineData("dist/index.js/")]
    [InlineData("dist/in dex.js")]
    [InlineData("")]
    public void IncorrectPathIsRefused(string path)
    {
        var error = Assert.Single(PluginFileStore.Check([new PluginFile(path, Small)]));

        Assert.Equal(("files[0].path", FieldErrorCodes.InvalidFormat), (error.Field, error.Code));
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("index.htm")]
    [InlineData("dist/run.sh")]
    [InlineData("README")]
    [InlineData("dist.js/README")]
    [InlineData("index.JS")]
    public void FileTypeOutsideTheListIsRefused(string path)
    {
        var error = Assert.Single(PluginFileStore.Check([new PluginFile(path, Small)]));

        Assert.Equal(("files[0].path", FieldErrorCodes.NotAllowed), (error.Field, error.Code));
    }

    [Fact]
    public void SamePathTwoTimesIsRefused()
    {
        var error = Assert.Single(PluginFileStore.Check([new PluginFile("index.js", Small), new PluginFile("index.js", Small)]));

        Assert.Equal("files[1].path", error.Field);
    }

    [Fact]
    public void VersionHasASizeLimitOf5Megabytes()
    {
        var half = new byte[(PluginFileStore.MaxVersionBytes / 2) + 1];

        var tooLarge = PluginFileStore.Check([new PluginFile("a.js", half), new PluginFile("b.js", half)]);
        var atTheLimit = PluginFileStore.Check([new PluginFile("a.js", new byte[PluginFileStore.MaxVersionBytes])]);

        Assert.Equal(("files", FieldErrorCodes.TooLong), (Assert.Single(tooLarge).Field, Assert.Single(tooLarge).Code));
        Assert.Empty(atTheLimit);
    }

    [Fact]
    public void VersionNeedsFilesAndHasAMaximumNumberOfFiles()
    {
        var tooMany = Enumerable.Range(0, PluginFileStore.MaxFiles + 1).Select(index => new PluginFile($"f{index}.js", Small)).ToList();

        Assert.Equal(FieldErrorCodes.Required, Assert.Single(PluginFileStore.Check([])).Code);
        Assert.Equal(FieldErrorCodes.TooLong, Assert.Single(PluginFileStore.Check(tooMany)).Code);
    }

    [Fact]
    public void IntegrityIsTheBase64Sha384Hash()
    {
        Assert.Equal("sha384-" + Convert.ToBase64String(SHA384.HashData(Small)), PluginFileStore.Integrity(Small));
    }
}
