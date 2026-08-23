using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13LcdHidAccessTests
{
    [Fact]
    public void Selects_the_only_992_byte_output_collection()
    {
        var expected = Collection("lcd", 992);

        var actual = G13LcdHidAccess.SelectOutputCollection(
            [Collection("input", 0), expected, Collection("feature", 8)]);

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Missing_output_collection_is_an_explicit_failure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            G13LcdHidAccess.SelectOutputCollection([Collection("input", 0)]));

        Assert.Contains("992-byte output collectionがありません", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiguous_output_collections_are_an_explicit_failure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            G13LcdHidAccess.SelectOutputCollection([Collection("lcd-a", 992), Collection("lcd-b", 992)]));

        Assert.Contains("2件あり、一意に選べません", error.Message, StringComparison.Ordinal);
    }

    private static G13HidCollectionInfo Collection(string path, ushort outputLength) =>
        new(path, 0x046D, 0xC21C, 0xFF00, 1, 8, outputLength, 0);
}
