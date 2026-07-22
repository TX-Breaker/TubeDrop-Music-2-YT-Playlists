using TubeDrop.Core.Ingestion;

namespace TubeDrop.Tests.Ingestion;

public sealed class FolderNameDeriverTests : IDisposable
{
    private readonly string _root;

    public FolderNameDeriverTests()
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

    private string Dir(string relative)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    [Fact]
    public void Master_SingleDroppedFolder_UsesItsName()
    {
        var f1 = CreateFile("My Album/01.mp3");
        var result = FolderNameDeriver.Derive([Dir("My Album")], [f1], FolderNameMode.Master);

        Assert.Equal("My Album", result.Name);
        Assert.False(result.FellBackToMaster);
    }

    [Fact]
    public void Master_MultipleDrops_UsesCommonRoot()
    {
        var f1 = CreateFile("Collection/CD1/a.mp3");
        var f2 = CreateFile("Collection/CD2/b.mp3");
        var result = FolderNameDeriver.Derive(
            [Dir("Collection/CD1"), Dir("Collection/CD2")], [f1, f2], FolderNameMode.Master);

        Assert.Equal("Collection", result.Name);
    }

    [Fact]
    public void Subfolder_AllTracksInOneSubfolder_UsesIt()
    {
        var f1 = CreateFile("Box/Disc 1/a.mp3");
        var f2 = CreateFile("Box/Disc 1/b.mp3");
        var result = FolderNameDeriver.Derive([Dir("Box")], [f1, f2], FolderNameMode.Subfolder);

        Assert.Equal("Disc 1", result.Name);
        Assert.False(result.FellBackToMaster);
    }

    [Fact]
    public void Subfolder_TracksSpanMultipleSubfolders_FallsBackToMaster()
    {
        var f1 = CreateFile("Box/Disc 1/a.mp3");
        var f2 = CreateFile("Box/Disc 2/b.mp3");
        var result = FolderNameDeriver.Derive([Dir("Box")], [f1, f2], FolderNameMode.Subfolder);

        Assert.Equal("Box", result.Name);
        Assert.True(result.FellBackToMaster);
    }

    [Fact]
    public void Subfolder_TracksDirectlyInMaster_FallsBackToMaster()
    {
        var f1 = CreateFile("Album/a.mp3");
        var result = FolderNameDeriver.Derive([Dir("Album")], [f1], FolderNameMode.Subfolder);

        Assert.Equal("Album", result.Name);
        Assert.True(result.FellBackToMaster);
    }

    [Fact]
    public void LooseFilesOnly_NoName()
    {
        var f1 = CreateFile("a.mp3");
        var result = FolderNameDeriver.Derive([f1], [f1], FolderNameMode.Master);

        Assert.Null(result.Name);
    }

    [Fact]
    public void NoAudioFiles_NoName()
    {
        var result = FolderNameDeriver.Derive([Dir("Empty")], [], FolderNameMode.Master);
        Assert.Null(result.Name);
    }
}
