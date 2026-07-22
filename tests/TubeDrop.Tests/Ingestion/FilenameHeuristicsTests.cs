using TubeDrop.Core.Ingestion;

namespace TubeDrop.Tests.Ingestion;

public sealed class FilenameHeuristicsTests
{
    [Theory]
    [InlineData("Daft Punk - Harder Better Faster Stronger.mp3", "Daft Punk", "Harder Better Faster Stronger", 0)]
    [InlineData("Queen - Bohemian Rhapsody.flac", "Queen", "Bohemian Rhapsody", 0)]
    [InlineData(@"C:\music\ACDC - Back In Black.mp3", "ACDC", "Back In Black", 0)]
    public void Parse_ArtistDashTitle(string file, string artist, string title, int trackNo)
    {
        var result = FilenameHeuristics.Parse(file);
        Assert.Equal((artist, title, trackNo), result);
    }

    [Theory]
    [InlineData("01. Bohemian Rhapsody.mp3", "", "Bohemian Rhapsody", 1)]
    [InlineData("12. Some Song.flac", "", "Some Song", 12)]
    public void Parse_TrackNumberDotTitle(string file, string artist, string title, int trackNo)
    {
        var result = FilenameHeuristics.Parse(file);
        Assert.Equal((artist, title, trackNo), result);
    }

    [Theory]
    [InlineData("03 - Queen - Somebody To Love.mp3", "Queen", "Somebody To Love", 3)]
    [InlineData("101 - Artist - Title.ogg", "Artist", "Title", 101)]
    public void Parse_TrackNumberArtistTitle(string file, string artist, string title, int trackNo)
    {
        var result = FilenameHeuristics.Parse(file);
        Assert.Equal((artist, title, trackNo), result);
    }

    [Theory]
    [InlineData("Artist_-_Title.mp3", "Artist", "Title")]
    [InlineData("Some_Artist_-_Some_Song.mp3", "Some Artist", "Some Song")]
    public void Parse_UnderscoresBecomeSpaces(string file, string artist, string title)
    {
        var (a, t, _) = FilenameHeuristics.Parse(file);
        Assert.Equal((artist, title), (a, t));
    }

    [Fact]
    public void Parse_DotSeparatedName_CleanedWhenNoSpaces()
    {
        var (_, title, _) = FilenameHeuristics.Parse("Some.Song.Name.mp3");
        Assert.Equal("Some Song Name", title);
    }

    [Theory]
    [InlineData("Rick Astley - Never Gonna Give You Up (Official Video).mp3", "Rick Astley", "Never Gonna Give You Up")]
    [InlineData("Artist - Song (Official Music Video).mp3", "Artist", "Song")]
    [InlineData("Artist - Song [Official Audio].mp3", "Artist", "Song")]
    [InlineData("Artist - Song (Lyric Video).mp3", "Artist", "Song")]
    [InlineData("Artist - Song (Videoclip Ufficiale).mp3", "Artist", "Song")]
    public void Parse_StripsReleaseNoise(string file, string artist, string title)
    {
        var (a, t, _) = FilenameHeuristics.Parse(file);
        Assert.Equal((artist, title), (a, t));
    }

    [Fact]
    public void Parse_BareTitle_NoArtist()
    {
        var (artist, title, trackNo) = FilenameHeuristics.Parse("Yesterday.mp3");
        Assert.Equal("", artist);
        Assert.Equal("Yesterday", title);
        Assert.Equal(0, trackNo);
    }

    [Fact]
    public void Parse_KeepsMeaningfulParentheses()
    {
        var (artist, title, _) = FilenameHeuristics.Parse("Artist - Song (Acoustic Version).mp3");
        Assert.Equal("Artist", artist);
        Assert.Equal("Song (Acoustic Version)", title);
    }

    [Fact]
    public void CleanNoise_RemovesOfficialVideoAnywhere()
    {
        Assert.Equal("My Song", FilenameHeuristics.CleanNoise("My Song (Official Video)"));
        Assert.Equal("My Song", FilenameHeuristics.CleanNoise("My Song [HD]"));
    }
}
