using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class ProjectFileTransactionTests
{
    [Fact]
    public void Commit_ReplacesWritesAndAppliesDeletions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string replacedPath = Path.Combine(directory, "replace.txt");
            string createdPath = Path.Combine(directory, "created.txt");
            string deletedPath = Path.Combine(directory, "delete.txt");
            File.WriteAllText(replacedPath, "old");
            File.WriteAllText(deletedPath, "remove me");

            var transaction = new ProjectFileTransaction();
            transaction.AddWrite(replacedPath, "new"u8.ToArray());
            transaction.AddWrite(createdPath, "created"u8.ToArray());
            transaction.AddDelete(deletedPath);

            transaction.Commit();

            Assert.Equal("new", File.ReadAllText(replacedPath));
            Assert.Equal("created", File.ReadAllText(createdPath));
            Assert.False(File.Exists(deletedPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Commit_WhenStagingFails_LeavesOriginalFilesUntouched()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string originalPath = Path.Combine(directory, "original.txt");
            string invalidParent = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(originalPath, "old");
            File.WriteAllText(invalidParent, "file");

            var transaction = new ProjectFileTransaction();
            transaction.AddWrite(originalPath, "new"u8.ToArray());
            transaction.AddWrite(Path.Combine(invalidParent, "invalid.txt"), "invalid"u8.ToArray());

            Assert.ThrowsAny<IOException>(transaction.Commit);
            Assert.Equal("old", File.ReadAllText(originalPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AddWrite_RejectsDuplicateTargets()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agbsynth-{Guid.NewGuid():N}.txt");
        var transaction = new ProjectFileTransaction();
        transaction.AddWrite(path, []);

        Assert.Throws<InvalidOperationException>(() => transaction.AddWrite(path, []));
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"AgbSynth.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
