using TubeDrop.Core.Ingestion;

namespace TubeDrop.Tests.Ingestion;

public sealed class IngestPipelineTests : IDisposable
{
    private readonly string _root;

    public IngestPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TubeDropTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateFile(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return full;
    }

    private sealed class FakeTagReader(Func<string, TrackInfo?> read) : ITagReader
    {
        public TrackInfo? Read(string path) => read(path);
    }

    private static TrackInfo Track(string path, string artist, string title, int duration) => new()
    {
        SourcePath = path,
        Artist = artist,
        Title = title,
        DurationSeconds = duration,
    };

    [Fact]
    public void Ingest_DuplicateWithinTolerance_Collapsed()
    {
        var a = CreateFile("a.mp3");
        var b = CreateFile("b.mp3");
        var reader = new FakeTagReader(p => Track(p, "Queen", "Bohemian Rhapsody",
            p.EndsWith("a.mp3", StringComparison.Ordinal) ? 354 : 355));

        var result = new IngestPipeline(reader).Ingest([a, b]);

        Assert.Single(result.Tracks);
        Assert.Equal(1, result.DuplicateCount);
    }

    [Fact]
    public void Ingest_SameSongDifferentDuration_NotDuplicate()
    {
        var a = CreateFile("a.mp3");
        var b = CreateFile("b.mp3");
        var reader = new FakeTagReader(p => Track(p, "Queen", "Bohemian Rhapsody",
            p.EndsWith("a.mp3", StringComparison.Ordinal) ? 354 : 420));

        var result = new IngestPipeline(reader).Ingest([a, b]);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(0, result.DuplicateCount);
    }

    [Fact]
    public void Ingest_DedupIsCaseAccentAndPunctuationInsensitive()
    {
        var a = CreateFile("a.mp3");
        var b = CreateFile("b.mp3");
        var reader = new FakeTagReader(p => p.EndsWith("a.mp3", StringComparison.Ordinal)
            ? Track(p, "Beyoncé", "Déjà Vu", 240)
            : Track(p, "beyonce", "deja vu!", 241));

        var result = new IngestPipeline(reader).Ingest([a, b]);

        Assert.Single(result.Tracks);
        Assert.Equal(1, result.DuplicateCount);
    }

    [Fact]
    public void Ingest_UnreadableFile_CountedAsError()
    {
        var a = CreateFile("a.mp3");
        var reader = new FakeTagReader(_ => null);

        var result = new IngestPipeline(reader).Ingest([a]);

        Assert.Empty(result.Tracks);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void Ingest_PropagatesScanCounters()
    {
        CreateFile("dir/song.mp3");
        CreateFile("dir/cover.jpg");
        var reader = new FakeTagReader(p => Track(p, "A", "B", 100));

        var result = new IngestPipeline(reader).Ingest(
            [Path.Combine(_root, "dir"), Path.Combine(_root, "ghost.mp3")]);

        Assert.Single(result.Tracks);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.MissingCount);
    }

    [Fact]
    public void NormalizeKey_StripsNoiseAndAccents()
    {
        Assert.Equal(
            IngestPipeline.NormalizeKey("Beyoncé", "Déjà Vu (Official Video)"),
            IngestPipeline.NormalizeKey("beyonce", "deja vu"));
    }
}
