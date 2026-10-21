using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for the pure <see cref="TrainingDataPaths"/> helper that resolves the default AI
/// training-data folder and decides whether a saved path is usable. These replace the old
/// hardcoded missing-drive default (E:\temp\OWClogs) that forced a folder prompt on every launch.
/// </summary>
public class TrainingDataPathsTests
{
    [Fact]
    public void DefaultFolder_IsRootedUnderDocuments_AndEndsWithOwcTrainingData()
    {
        var folder = TrainingDataPaths.DefaultFolder();

        Assert.False(string.IsNullOrWhiteSpace(folder));
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.StartsWith(documents, folder);
        Assert.Equal(Path.Combine(documents, "OWC", "TrainingData"), folder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"E:\temp\OWClogs")]
    public void IsUsable_EmptyOrNonexistentPath_ReturnsFalse(string? path)
    {
        Assert.False(TrainingDataPaths.IsUsable(path));
    }

    [Fact]
    public void IsUsable_ExistingDirectory_ReturnsTrue()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"TrainingDataPathsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            Assert.True(TrainingDataPaths.IsUsable(temp));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }
}
