using AgbSynth.App.GBA;
using Xunit;

namespace AgbSynth.Tests;

public sealed class GbaAddressTests
{
    [Fact]
    public void TryToOffset_ConvertsRomPointer()
    {
        Assert.True(GbaAddress.TryToOffset(0x08000100, 1024, out int offset));
        Assert.Equal(0x100, offset);
    }

    [Fact]
    public void TryToOffset_RejectsNonRomPointer()
    {
        Assert.False(GbaAddress.TryToOffset(0x02000000, 1024, out _));
    }

    [Fact]
    public void ToPointer_ConvertsOffset()
    {
        Assert.Equal(0x08000100u, GbaAddress.ToPointer(0x100));
    }

    [Fact]
    public void Parser_AcceptsRomOffset()
    {
        Assert.True(GbaAddressParser.TryParseRomAddressOrOffset("0x100", 1024, out int offset, out var error));
        Assert.Null(error);
        Assert.Equal(0x100, offset);
    }

    [Fact]
    public void Parser_AcceptsGbaPointer()
    {
        Assert.True(GbaAddressParser.TryParseRomAddressOrOffset("0x08000100", 1024, out int offset, out var error));
        Assert.Null(error);
        Assert.Equal(0x100, offset);
    }
}
