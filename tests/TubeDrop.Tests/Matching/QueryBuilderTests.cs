using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Matching;

namespace TubeDrop.Tests.Matching;

public sealed class QueryBuilderTests
{
    private static TrackInfo Track(string artist, string title) => new()
    {
        SourcePath = "x.mp3",
        Artist = artist,
        Title = title,
    };

    [Fact]
    public void Build_ArtistAndTitle_ThreeOrderedQueries()
    {
        var queries = QueryBuilder.Build(Track("Daft Punk", "One More Time"));

        Assert.Equal(
            ["Daft Punk One More Time", "One More Time Daft Punk", "One More Time"],
            queries);
    }

    [Fact]
    public void Build_TitleOnly_SingleQuery()
    {
        var queries = QueryBuilder.Build(Track("", "One More Time"));

        Assert.Equal(["One More Time"], queries);
    }

    [Fact]
    public void Build_StripsNoise()
    {
        var queries = QueryBuilder.Build(Track("Artist", "Song (Official Video)"));

        Assert.Contains("Artist Song", queries);
        Assert.DoesNotContain(queries, q => q.Contains("Official", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_NonLatin_AddsTransliteratedVariants()
    {
        var queries = QueryBuilder.Build(Track("Кино", "Группа крови"));

        // Original scripts kept first, transliterated variants appended.
        Assert.Contains(queries, q => q.Contains("Кино"));
        Assert.Contains(queries, q => q.Contains("Gruppa krovi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_NonLatinNoArtist_SearchesVerbatimTitleFirst()
    {
        var track = new TrackInfo { SourcePath = "x.mp3", Artist = "", Title = "布瑞吉Bridge-来我敬你" };

        var queries = QueryBuilder.Build(track);

        Assert.Equal("布瑞吉Bridge-来我敬你", queries[0]); // exact file title, first
    }

    [Fact]
    public void Build_LatinOnly_NoTransliteratedDuplicates()
    {
        var queries = QueryBuilder.Build(Track("Queen", "Bohemian Rhapsody"));

        Assert.Equal(3, queries.Count);
    }

    [Fact]
    public void Build_DedupsCaseInsensitively()
    {
        // Artist equals title → "{artist} {title}" and "{title} {artist}" coincide.
        var queries = QueryBuilder.Build(Track("Iron Maiden", "Iron Maiden"));

        Assert.Equal(queries.Count, queries.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
