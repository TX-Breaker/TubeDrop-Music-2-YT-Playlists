using TubeDrop.Core.Ingestion;

namespace TubeDrop.Tests.Ingestion;

public sealed class DropScannerTests : IDisposable
{
    private readonly string _root;

    public DropScannerTests()
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
            // best-effort cleanup
        }
    }

    private string CreateFile(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return full;
    }

    [Fact]
    public void Scan_SingleAudioFile_IsAccepted()
    {
        var mp3 = CreateFile("song.mp3");

        var result = DropScanner.Scan([mp3]);

        Assert.Equal([mp3], result.AudioFiles);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.MissingCount);
    }

    [Fact]
    public void Scan_Folder_IsRecursive()
    {
        CreateFile("album/01.flac");
        CreateFile("album/cd2/02.opus");
        CreateFile("album/cover.jpg");

        var result = DropScanner.Scan([Path.Combine(_root, "album")]);

        Assert.Equal(2, result.AudioFiles.Count);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void Scan_NonAudio_CountedAsSkipped_NeverThrows()
    {
        var txt = CreateFile("notes.txt");
        var exe = CreateFile("setup.exe");

        var result = DropScanner.Scan([txt, exe]);

        Assert.Empty(result.AudioFiles);
        Assert.Equal(2, result.SkippedCount);
    }

    [Fact]
    public void Scan_MissingPath_CountedAsMissing()
    {
        var result = DropScanner.Scan([Path.Combine(_root, "ghost.mp3")]);

        Assert.Empty(result.AudioFiles);
        Assert.Equal(1, result.MissingCount);
    }

    [Fact]
    public void Scan_FileDroppedTwiceOrInsideDroppedFolder_IsCollapsed()
    {
        var file = CreateFile("album/track.mp3");

        var result = DropScanner.Scan([file, file, Path.Combine(_root, "album")]);

        Assert.Single(result.AudioFiles);
    }

    [Theory]
    [InlineData("a.mp3")]
    [InlineData("a.FLAC")]
    [InlineData("a.m4a")]
    [InlineData("a.ogg")]
    [InlineData("a.opus")]
    [InlineData("a.wav")]
    [InlineData("a.wma")]
    [InlineData("a.aiff")]
    [InlineData("a.ape")]
    [InlineData("a.mpc")]
    [InlineData("a.wv")]
    [InlineData("a.dsf")]
    public void IsAudio_AcceptsAtlFormats_CaseInsensitive(string name) =>
        Assert.True(AudioFileTypes.IsAudio(name));

    [Theory]
    [InlineData("a.jpg")]
    [InlineData("a.txt")]
    [InlineData("a.pdf")]
    [InlineData("a")]
    [InlineData("a.mp3.bak")]
    public void IsAudio_RejectsNonAudio(string name) =>
        Assert.False(AudioFileTypes.IsAudio(name));
}
