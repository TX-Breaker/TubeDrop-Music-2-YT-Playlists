using TubeDrop.Core.Cover;

namespace TubeDrop.Tests.Cover;

public sealed class CoverArtistExtractorTests
{
    [Theory]
    [InlineData("I Prevail Hurricane", "Hurricane", "I Prevail")]
    [InlineData("Hurricane - I Prevail", "Hurricane", "I Prevail")]
    [InlineData("I Prevail – Hurricane (Official Video)", "Hurricane", "I Prevail")]
    [InlineData("Hurricane by I Prevail", "Hurricane", "I Prevail")]
    public void Extract_DerivesArtistFromBestGuess(string bestGuess, string title, string expected)
    {
        var artist = CoverArtistExtractor.Extract(bestGuess, [], title);
        Assert.Equal(expected, artist);
    }

    [Fact]
    public void Extract_FallsBackToSuggestions_WhenBestGuessEmpty()
    {
        var artist = CoverArtistExtractor.Extract("", ["Egypt Central"], "Taking You Down");
        Assert.Equal("Egypt Central", artist);
    }

    [Theory]
    [InlineData("Hurricane", "Hurricane")]               // nothing but the title
    [InlineData("", "Hurricane")]                        // empty
    [InlineData("official music video", "Hurricane")]    // noise, no real artist near title? still text
    public void Extract_NoPlausibleArtist_ReturnsNullOrHarmless(string bestGuess, string title)
    {
        var artist = CoverArtistExtractor.Extract(bestGuess, [], title);
        // Either null, or at least not equal to the title.
        Assert.True(artist is null || !string.Equals(artist, title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_TitleOnly_ReturnsNull()
    {
        Assert.Null(CoverArtistExtractor.Extract("Hurricane", [], "Hurricane"));
    }
}
