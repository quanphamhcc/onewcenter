using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="OutputWindowPlanner"/> (đợt 2 T16 / R8 — pure Core).
/// Verifies the enable/disable ↔ open/close reconciliation contract: which outputs get
/// a window, and which open windows must be closed.
/// </summary>
public class OutputWindowPlannerTests
{
    private static OutputConfig Audience(string id, int displayIndex, bool enabled = true) =>
        new() { Id = id, Name = id, Purpose = ScreenPurpose.Audience, DisplayIndex = displayIndex, IsEnabled = enabled };

    [Fact]
    public void Plan_NullOutputs_PlansNothing()
    {
        var plan = OutputWindowPlanner.PlanAudienceWindows(null, 2);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_EmptyOutputs_PlansNothing()
    {
        var plan = OutputWindowPlanner.PlanAudienceWindows(new List<OutputConfig>(), 2);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_EnabledAudienceWithValidDisplay_IsPlanned()
    {
        var outputs = new List<OutputConfig> { Audience("main", 1) };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        var single = Assert.Single(plan);
        Assert.Equal("main", single.Output.Id);
        Assert.Equal(1, single.ClampedDisplayIndex);
    }

    [Fact]
    public void Plan_DisabledOutput_IsNotPlanned()
    {
        var outputs = new List<OutputConfig>
        {
            Audience("main", 0),
            Audience("live", 1, enabled: false)
        };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        var single = Assert.Single(plan);
        Assert.Equal("main", single.Output.Id);
    }

    [Fact]
    public void Plan_StageOutput_IsNeverPlanned()
    {
        var outputs = new List<OutputConfig>
        {
            new() { Id = "stage", Name = "Stage", Purpose = ScreenPurpose.Stage, DisplayIndex = 1, IsEnabled = true }
        };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_UnassignedDisplay_IsNotPlanned()
    {
        var outputs = new List<OutputConfig> { Audience("main", -1) };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_RemovedMonitor_IsNotPlanned()
    {
        // Saved index 1 but only 1 monitor left → clamped to -1 → no window.
        var outputs = new List<OutputConfig> { Audience("main", 1) };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 1);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_NoMonitors_PlansNothing()
    {
        var outputs = new List<OutputConfig> { Audience("main", 0) };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 0);
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_MultipleAudiences_AllPlanned()
    {
        var outputs = new List<OutputConfig>
        {
            Audience("main", 0),
            Audience("live", 1)
        };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        Assert.Equal(2, plan.Count);
        Assert.Equal(new[] { "main", "live" }, plan.Select(p => p.Output.Id).ToArray());
    }

    [Fact]
    public void WindowsToClose_ReturnsOpenIdsMissingFromPlan()
    {
        var outputs = new List<OutputConfig> { Audience("main", 0) };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        var toClose = OutputWindowPlanner.WindowsToClose(new[] { "main", "live", "old" }, plan);
        Assert.Equal(new[] { "live", "old" }, toClose.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void WindowsToClose_EmptyWhenAllPlanned()
    {
        var outputs = new List<OutputConfig> { Audience("main", 0) };
        var plan = OutputWindowPlanner.PlanAudienceWindows(outputs, 2);
        var toClose = OutputWindowPlanner.WindowsToClose(new[] { "main" }, plan);
        Assert.Empty(toClose);
    }

    [Fact]
    public void IsEnabled_DefaultsTrue_ForBackwardCompatibility()
    {
        // Old JSON without "isEnabled" must deserialize to enabled.
        var output = new OutputConfig { Id = "x" };
        Assert.True(output.IsEnabled);
    }
}
