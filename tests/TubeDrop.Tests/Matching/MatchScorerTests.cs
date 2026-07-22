using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Matching;

namespace TubeDrop.Tests.Matching;

public sealed class MatchScorerTests
{
    private static TrackInfo Track(string artist, string title, int duration = 200, string album = "") => new()
    {
        SourcePath = "x.mp3",
        Artist = artist,
        Title = title,
        DurationSeconds = duration,
        Album = album,
    };

    private static MatchCandidate Candidate(
        string title, string artist, int duration = 200,
        bool official = false, string channel = "") => new()
    {
        VideoId = "v",
        Title = title,
        Artists = artist.Length > 0 ? [artist] : [],
        Channel = channel.Length > 0 ? channel : artist,
        DurationSeconds = duration,
        IsOfficialArtistChannel = official,
    };

    [Fact]
    public void PerfectMatch_ScoresNearOne()
    {
        var scored = MatchScorer.Score(
            Track("Daft Punk", "Harder Better Faster Stronger", 227),
            Candidate("Harder, Better, Faster, Stronger", "Daft Punk", 227, official: true));

        Assert.True(scored.Score >= 0.95, $"expected ≥0.95, got {scored.Score}");
        Assert.Empty(scored.PenaltyReasons);
    }

    [Fact]
    public void WrongSong_ScoresBelowThreshold()
    {
        var scored = MatchScorer.Score(
            Track("Daft Punk", "Harder Better Faster Stronger", 227),
            Candidate("Completely Different Track", "Somebody Else", 95));

        Assert.True(scored.Score < 0.75, $"expected <0.75, got {scored.Score}");
    }

    [Theory]
    [InlineData("Song (Live)", "live")]
    [InlineData("Song (Cover)", "cover")]
    [InlineData("Song Remix", "remix")]
    [InlineData("Song sped up", "sped up")]
    [InlineData("Song slowed reverb", "slowed")]
    [InlineData("Song 8D Audio", "8d")]
    [InlineData("Song Karaoke Version", "karaoke")]
    [InlineData("Song Instrumental", "instrumental")]
    public void VariantOnlyInCandidate_Penalized(string candidateTitle, string reason)
    {
        var clean = MatchScorer.Score(Track("A", "Song"), Candidate("Song", "A"));
        var variant = MatchScorer.Score(Track("A", "Song"), Candidate(candidateTitle, "A"));

        Assert.Contains(reason, variant.PenaltyReasons);
        Assert.True(variant.Score <= clean.Score - 0.20,
            $"variant {variant.Score} should be well below clean {clean.Score}");
    }

    [Fact]
    public void VariantInBothSourceAndCandidate_NotPenalized()
    {
        var scored = MatchScorer.Score(
            Track("A", "Song (Live)"),
            Candidate("Song (Live)", "A"));

        Assert.Empty(scored.PenaltyReasons);
    }

    [Fact]
    public void Penalties_AreCapped()
    {
        var scored = MatchScorer.Score(
            Track("A", "Song", 200),
            Candidate("Song live cover remix karaoke instrumental", "A", 200, official: true));

        // 5 markers × 0.25 = 1.25 uncapped; cap keeps the score ≥ 0.
        Assert.True(scored.PenaltyReasons.Count >= 5);
        Assert.True(scored.Score > 0.0);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(3, 1.0)]
    [InlineData(9, 0.5)]
    [InlineData(15, 0.0)]
    [InlineData(60, 0.0)]
    public void DurationFactor_FollowsSpecCurve(int delta, double expectedFactor)
    {
        var baseline = MatchScorer.Score(
            Track("A", "Song", 200),
            Candidate("Song", "A", 200));
        var shifted = MatchScorer.Score(
            Track("A", "Song", 200),
            Candidate("Song", "A", 200 + delta));

        var lost = baseline.Score - shifted.Score;
        Assert.Equal(0.20 * (1.0 - expectedFactor), lost, precision: 6);
    }

    [Fact]
    public void TopicChannel_GetsAuthorityBonus()
    {
        var plain = MatchScorer.Score(
            Track("Queen", "Somebody To Love"),
            Candidate("Somebody To Love", "Queen"));
        var topic = MatchScorer.Score(
            Track("Queen", "Somebody To Love"),
            Candidate("Somebody To Love", "Queen", channel: "Queen - Topic"));

        Assert.Equal(0.10, topic.Score - plain.Score, precision: 6);
    }

    [Fact]
    public void NonLatin_TransliteratedComparison_Works()
    {
        // Cyrillic source vs Latin candidate: transliteration must bridge them.
        var scored = MatchScorer.Score(
            Track("Кино", "Группа крови", 283),
            Candidate("Gruppa krovi", "Kino", 283, official: true));

        Assert.True(scored.Score >= 0.75, $"expected ≥0.75, got {scored.Score}");
    }

    [Fact]
    public void RemixerInCandidateTitle_CreditsArtist()
    {
        // Real case: file artist is the remixer; YouTube lists the original artist,
        // but the remixer's name is in the candidate title.
        var scored = MatchScorer.Score(
            Track("Mat Weasel Busters, Mat Weasel", "The Speedfreak Boombox (Mat Weasel Remix)", 300),
            Candidate("The Speedfreak - Boombox (Mat Weasel Remix)", "The Speedfreak", 300));

        Assert.True(scored.Score >= 0.75, $"expected ≥0.75, got {scored.Score}");
    }

    [Fact]
    public void ExactTitleMatchingDuration_NotVetoedByArtist()
    {
        // Real case: identical title, different uploader artist, same length.
        var scored = MatchScorer.Score(
            Track("Astro, Tony Boy", "PANINARO RMX (prod. 2nightfall)", 180),
            Candidate("PANINARO RMX (prod. 2nightfall)", "SomeUploader", 180));

        Assert.True(scored.Score >= 0.75, $"expected ≥0.75, got {scored.Score}");
    }

    [Fact]
    public void DifferentSongSameArtist_StillRejected()
    {
        // A genuinely different title must stay below threshold. The near-exact
        // title floor can't apply (title differs), and the different length helps.
        var scored = MatchScorer.Score(
            Track("Mat Weasel Busters", "Dies ist meine Barbara Hardcore", 240),
            Candidate("Hardcore Kidding", "Mat Weasel Busters", 275));

        Assert.True(scored.Score < 0.75, $"expected <0.75, got {scored.Score}");
    }

    [Fact]
    public void RmxNotPenalizedAgainstRemixCandidate()
    {
        var withRmx = MatchScorer.Score(
            Track("A", "Song RMX", 200),
            Candidate("Song Remix", "A", 200));

        Assert.DoesNotContain("remix", withRmx.PenaltyReasons);
    }

    [Fact]
    public void UnknownDurations_Neutral()
    {
        var scored = MatchScorer.Score(
            Track("A", "Song", 0),
            Candidate("Song", "A", 0));

        // duration contributes 0.5 × 0.20 = 0.10
        Assert.True(scored.Score is >= 0.79 and <= 0.81, $"got {scored.Score}");
    }
}
