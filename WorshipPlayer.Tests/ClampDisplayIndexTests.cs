using WorshipPlayer.Presenter.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F7: DisplayManager.ClampDisplayIndex pure helper. Out-of-range maps to -1,
/// in-range is preserved, and -1 (unassigned) is preserved.
/// </summary>
public class ClampDisplayIndexTests
{
    [Theory]
    [InlineData(1, 1, -1)]  // saved index past the only monitor -> unassigned
    [InlineData(0, 1, 0)]   // valid single-monitor index preserved
    [InlineData(-1, 2, -1)] // unassigned preserved
    [InlineData(1, 2, 1)]   // valid index within two monitors preserved
    [InlineData(2, 2, -1)]  // out-of-range for two monitors -> unassigned
    public void ClampDisplayIndex_BehavesPerTable(int savedIndex, int monitorCount, int expected)
    {
        Assert.Equal(expected, DisplayManager.ClampDisplayIndex(savedIndex, monitorCount));
    }
}
