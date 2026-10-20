using System.Collections.Generic;
using WorshipPlayer.Core.Services;
using Xunit;
using Candidate = WorshipPlayer.Core.Services.PythonAnalysisService.PythonCandidate;
using ProbeResult = WorshipPlayer.Core.Services.PythonAnalysisService.PythonProbeResult;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the PURE Python resolver (PythonAnalysisService.ResolvePythonExecutable). These
/// use a FAKE probe delegate so NO real processes are launched — they exercise only the
/// ordering/priority/WindowsApps-skip/concrete-path selection logic. The real probe and the
/// environment enumeration do process/filesystem I/O and are verified by compilation only on this
/// Linux sandbox (no Python, no `py` launcher, no Windows).
/// </summary>
public class PythonAnalysisResolverTests
{
    // A fake probe built from a lookup of executable path -> result. Unknown paths are "nothing".
    private static System.Func<Candidate, ProbeResult> FakeProbe(
        Dictionary<string, ProbeResult> map)
        => c => map.TryGetValue(c.ExecutablePath, out var r)
            ? r
            : new ProbeResult(false, false, null, "not found");

    private static ProbeResult Usable(string? concrete = null)
        => new ProbeResult(true, true, concrete, "Python 3.13");

    private static ProbeResult VersionOnly()
        => new ProbeResult(true, false, null, "packages missing");

    [Fact]
    public void ConfiguredValidPath_WinsOverEverything()
    {
        var candidates = new[]
        {
            new Candidate("python", null, "python (PATH)"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            [@"C:\Py\python.exe"] = Usable(),
            ["python"] = Usable(),
        });

        var (chosen, tried) = PythonAnalysisService.ResolvePythonExecutable(
            @"C:\Py\python.exe", candidates, probe);

        Assert.Equal(@"C:\Py\python.exe", chosen);
        Assert.NotEmpty(tried);
    }

    [Fact]
    public void ConfiguredInvalidPath_IsSkipped_DiscoveryProceeds()
    {
        var candidates = new[]
        {
            new Candidate("python", null, "python (PATH)"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            // Configured path is not usable; bare python is.
            [@"C:\Bad\python.exe"] = new ProbeResult(false, false, null, "no such file"),
            ["python"] = Usable(),
        });

        var (chosen, _) = PythonAnalysisService.ResolvePythonExecutable(
            @"C:\Bad\python.exe", candidates, probe);

        Assert.Equal("python", chosen);
    }

    [Fact]
    public void WindowsAppsStub_IsAlwaysRejected_EvenWhenProbeReportsUsable()
    {
        string stub = @"C:\Users\Admin\AppData\Local\Microsoft\WindowsApps\python.exe";
        var candidates = new[]
        {
            new Candidate(stub, null, "windowsapps stub"),
            new Candidate("python3", null, "python3 (PATH)"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            // Even though the fake probe would call it usable, it must never be chosen.
            [stub] = Usable(),
            ["python3"] = Usable(),
        });

        var (chosen, tried) = PythonAnalysisService.ResolvePythonExecutable(
            null, candidates, probe);

        Assert.Equal("python3", chosen);
        Assert.Contains(tried, t => t.Contains("skipped"));
    }

    [Fact]
    public void WindowsAppsStub_AsConfiguredPath_IsRejected()
    {
        string stub = @"C:\Users\Admin\AppData\Local\Microsoft\WindowsApps\python.exe";
        var candidates = new[]
        {
            new Candidate("python", null, "python (PATH)"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            [stub] = Usable(),
            ["python"] = Usable(),
        });

        var (chosen, _) = PythonAnalysisService.ResolvePythonExecutable(stub, candidates, probe);

        Assert.Equal("python", chosen);
    }

    [Fact]
    public void Py3Launcher_IsOrderedBefore_BarePython()
    {
        var candidates = new[]
        {
            new Candidate("py", "-3", "py -3 launcher"),
            new Candidate("python", null, "python (PATH)"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            // Both usable; the launcher appears first so it must win.
            ["py"] = Usable(@"C:\Py313\python.exe"),
            ["python"] = Usable(),
        });

        var (chosen, _) = PythonAnalysisService.ResolvePythonExecutable(null, candidates, probe);

        // The launcher win returns its concrete resolved exe, not the bare "python".
        Assert.Equal(@"C:\Py313\python.exe", chosen);
    }

    [Fact]
    public void Py3LauncherWin_ReturnsConcreteResolvedPath()
    {
        var candidates = new[]
        {
            new Candidate("py", "-3", "py -3 launcher"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            ["py"] = Usable(@"C:\Users\Admin\AppData\Local\Python\pythoncore-3.13-64\python.exe"),
        });

        var (chosen, _) = PythonAnalysisService.ResolvePythonExecutable(null, candidates, probe);

        Assert.Equal(@"C:\Users\Admin\AppData\Local\Python\pythoncore-3.13-64\python.exe", chosen);
    }

    [Fact]
    public void PackagesMissingCandidate_IsSkipped_AndRecorded()
    {
        var candidates = new[]
        {
            new Candidate("python", null, "python (PATH)"),
            new Candidate(@"C:\Py313\python.exe", null, "python313"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            ["python"] = VersionOnly(),           // version ok but no librosa
            [@"C:\Py313\python.exe"] = Usable(),  // fully usable
        });

        var (chosen, tried) = PythonAnalysisService.ResolvePythonExecutable(null, candidates, probe);

        Assert.Equal(@"C:\Py313\python.exe", chosen);
        Assert.Contains(tried, t => t.Contains("packages missing"));
    }

    [Fact]
    public void EmptyConfigured_AndEmptyCandidates_DoNotThrow_ReturnNull()
    {
        var (chosen, tried) = PythonAnalysisService.ResolvePythonExecutable(
            "", new List<Candidate>(), _ => new ProbeResult(false, false, null, "n/a"));

        Assert.Null(chosen);
        Assert.Empty(tried);
    }

    [Fact]
    public void NullAndGarbageInputs_DoNotThrow()
    {
        // Null configured path + null candidate list + a probe that always throws.
        var (chosen, _) = PythonAnalysisService.ResolvePythonExecutable(
            null, new[] { new Candidate("garbage!!", null, "garbage") },
            _ => throw new System.InvalidOperationException("boom"));

        Assert.Null(chosen);
    }

    [Fact]
    public void Py3Launcher_ResolvingToStub_IsRejected()
    {
        string stub = @"C:\Users\Admin\AppData\Local\Microsoft\WindowsApps\python.exe";
        var candidates = new[]
        {
            new Candidate("py", "-3", "py -3 launcher"),
            new Candidate("python3", null, "python3 (PATH)"),
        };
        var probe = FakeProbe(new Dictionary<string, ProbeResult>
        {
            // py -3 resolves to the stub, which must be rejected even though version/packages ok.
            ["py"] = new ProbeResult(true, true, stub, "resolved stub"),
            ["python3"] = Usable(),
        });

        var (chosen, tried) = PythonAnalysisService.ResolvePythonExecutable(null, candidates, probe);

        Assert.Equal("python3", chosen);
        Assert.Contains(tried, t => t.Contains("stub"));
    }
}
