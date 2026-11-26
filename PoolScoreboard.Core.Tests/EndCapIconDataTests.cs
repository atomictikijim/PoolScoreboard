using System.Text;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

public class EndCapIconDataTests
{
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };

    [Fact]
    public void TryParse_RoundTripsAPngDataUri()
    {
        var dataUri = EndCapIconData.ToDataUri("image/png", PngHeader);

        Assert.True(EndCapIconData.TryParse(dataUri, out var mime, out var bytes));
        Assert.Equal("image/png", mime);
        Assert.Equal(PngHeader, bytes);
    }

    [Fact]
    public void TryParse_RoundTripsASvgPayloadToo()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        var dataUri = EndCapIconData.ToDataUri("image/svg+xml", svg);

        Assert.True(EndCapIconData.TryParse(dataUri, out var mime, out var bytes));
        Assert.Equal("image/svg+xml", mime);
        Assert.Equal(svg, bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-data-uri")]
    [InlineData("data:image/png,notbase64")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:;base64,AAAA")]
    [InlineData("data:text/html;base64,PGgxPmhpPC9oMT4=")]
    [InlineData("data:image/png;base64,!!!!not base64!!!!")]
    public void TryParse_RejectsAnythingThatIsNotABase64ImageUri(string? dataUri)
    {
        Assert.False(EndCapIconData.TryParse(dataUri, out var mime, out var bytes));
        Assert.Equal(string.Empty, mime);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryParse_TrimsWhitespaceAroundTheMime()
    {
        Assert.True(EndCapIconData.TryParse("data: image/png ;base64,AAE=", out var mime, out var bytes));
        Assert.Equal("image/png", mime);
        Assert.Equal(new byte[] { 0, 1 }, bytes);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/PNG", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/jpg", ".jpg")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/bmp", ".bmp")]
    [InlineData("image/svg+xml", ".svg")]
    [InlineData("image/webp", ".bin")]
    public void ExtensionFor_MapsEveryMimeTheAppCanSee(string mime, string expected) =>
        Assert.Equal(expected, EndCapIconData.ExtensionFor(mime));

    [Fact]
    public void ToDataUri_KeepsTheMimeVerbatim() =>
        Assert.Equal("data:image/svg+xml;base64,AAE=", EndCapIconData.ToDataUri("image/svg+xml", new byte[] { 0, 1 }));
}
