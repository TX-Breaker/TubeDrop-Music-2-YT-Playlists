using TubeDrop.Core.Ingestion;

namespace TubeDrop.Tests.Ingestion;

/// <summary>
/// TagReader tests run against real files generated on the fly:
/// a minimal PCM WAV (valid, tagless) exercises the heuristics fallback,
/// and ATL itself is used to write tags for the tagged-file case.
/// </summary>
public sealed class TagReaderTests : IDisposable
{
    private readonly string _root;

    public TagReaderTests()
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

    /// <summary>Writes a valid PCM WAV of the given duration (8 kHz, 8-bit, mono).</summary>
    private string CreateWav(string name, int seconds = 2)
    {
        var path = Path.Combine(_root, name);
        const int sampleRate = 8000;
        var dataSize = sampleRate * seconds;

        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8);
        w.Write(36 + dataSize);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(sampleRate);
        w.Write(sampleRate);        // byte rate (8-bit mono)
        w.Write((short)1);          // block align
        w.Write((short)8);          // bits per sample
        w.Write("data"u8);
        w.Write(dataSize);
        w.Write(new byte[dataSize]);
        return path;
    }

    [Fact]
    public void Read_TaglessWav_FallsBackToFilenameHeuristics()
    {
        var path = CreateWav("Daft Punk - One More Time.wav");

        var track = new TagReader().Read(path);

        Assert.NotNull(track);
        Assert.Equal(TrackMetadataOrigin.FilenameHeuristics, track.Origin);
        Assert.Equal("Daft Punk", track.Artist);
        Assert.Equal("One More Time", track.Title);
        Assert.Equal(2, track.DurationSeconds);
    }

    [Fact]
    public void Read_TaggedFile_UsesTags()
    {
        var path = CreateWav("random_name.wav");
        var atlTrack = new ATL.Track(path)
        {
            Artist = "Queen",
            Title = "Somebody To Love",
            Album = "A Day at the Races",
            TrackNumber = 4,
            Year = 1976,
        };
        Assert.True(atlTrack.Save());

        var track = new TagReader().Read(path);

        Assert.NotNull(track);
        Assert.Equal(TrackMetadataOrigin.Tags, track.Origin);
        Assert.Equal("Queen", track.Artist);
        Assert.Equal("Somebody To Love", track.Title);
        Assert.Equal("A Day at the Races", track.Album);
        Assert.Equal(4, track.TrackNumber);
        Assert.Equal(1976, track.Year);
    }

    [Fact]
    public void Read_CorruptFile_ReturnsTrackWithHeuristics_OrNull_NeverThrows()
    {
        var path = Path.Combine(_root, "Artist - Broken Song.mp3");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02]);

        var exception = Record.Exception(() => new TagReader().Read(path));

        Assert.Null(exception);
    }
}
