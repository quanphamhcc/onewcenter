using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Calls the Python analysis script (librosa) to detect BPM, beats, and sections.
/// Uses subprocess to call Python — works with any Python 3.9+ installation.
/// </summary>
public class PythonAnalysisService
{
    private string _pythonPath;
    private readonly string _scriptPath;

    public PythonAnalysisService(string pythonPath, string scriptPath)
    {
        _pythonPath = pythonPath;
        _scriptPath = scriptPath;
    }

    /// <summary>
    /// Check if Python and librosa are available.
    /// </summary>
    public (bool success, string message) CheckPythonAvailable()
    {
        try
        {
            var result = RunCommand(_pythonPath, "--version");
            if (result.exitCode != 0)
                return (false, $"Python not found at: {_pythonPath}");

            string pythonVersion = result.output.Trim();

            var libCheck = RunCommand(_pythonPath,
                "-c \"import librosa; import numpy; import scipy; print('OK')\"");
            if (libCheck.exitCode != 0 || !libCheck.output.Contains("OK"))
            {
                // Show which python was checked and what's missing
                string missingDetail = !string.IsNullOrWhiteSpace(libCheck.error) 
                    ? libCheck.error.Trim() 
                    : libCheck.output.Trim();
                return (false, $"Using: {_pythonPath} ({pythonVersion})\n\n" +
                    $"Required packages not found in this Python installation.\n" +
                    $"Detail: {missingDetail}\n\n" +
                    $"Fix: Run this command using the SAME Python:\n" +
                    $"  \"{_pythonPath}\" -m pip install librosa numpy scipy scikit-learn");
            }

            return (true, $"Python OK: {pythonVersion} at {_pythonPath}");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }


    /// <summary>
    /// Run the full song analysis with real-time progress reporting.
    /// Returns updated Song object or null on failure.
    /// </summary>
    public async Task<(Song? song, string message)> AnalyzeSongAsync(
        string songFolderPath, string llmMode = "azure",
        Action<int, string>? onProgress = null,
        string quality = "default", double? bpmOverride = null,
        List<string>? sectionHints = null,
        Dictionary<string, string>? sectionLyrics = null,
        int linesPerSlide = 4, int slideLeadMs = 500,
        string? gridMode = null)
    {
        if (!File.Exists(_scriptPath))
            return (null, $"Analysis script not found: {_scriptPath}");

        if (!Directory.Exists(songFolderPath))
            return (null, $"Song folder not found: {songFolderPath}");

        try
        {
            // If section hints are provided, write them to a file for the Python script
            if (sectionHints != null && sectionHints.Count > 0)
            {
                var hintsPath = Path.Combine(songFolderPath, "_section_hints.json");
                var hintsJson = System.Text.Json.JsonSerializer.Serialize(sectionHints);
                await File.WriteAllTextAsync(hintsPath, hintsJson);
            }

            // If section lyrics are provided, write them for lyrics validation
            if (sectionLyrics != null && sectionLyrics.Count > 0)
            {
                var lyricsPath = Path.Combine(songFolderPath, "_section_lyrics.json");
                var lyricsJson = System.Text.Json.JsonSerializer.Serialize(sectionLyrics);
                await File.WriteAllTextAsync(lyricsPath, lyricsJson);
            }

            // Run the Python analysis script with LLM mode flag and optional quality/BPM.
            var args = $"\"{_scriptPath}\" \"{songFolderPath}\" --llm {llmMode}";
            if (!string.IsNullOrEmpty(quality) && quality != "default")
                args += $" --quality {quality}";
            if (bpmOverride.HasValue && bpmOverride.Value > 0)
                args += $" --bpm {bpmOverride.Value:F1}";
            // Slide grouping/timing options: lines-per-slide (2-4) and the read-ahead lead (ms).
            args += $" --lines-per-slide {Math.Clamp(linesPerSlide, 2, 4)}";
            args += $" --slide-lead-ms {Math.Max(0, slideLeadMs)}";
            // Grid mode override (auto/even/following) for the click grid, when the user chose one.
            if (!string.IsNullOrEmpty(gridMode))
                args += $" --grid-mode {gridMode}";

            var result = await RunCommandWithProgressAsync(_pythonPath, args,
                timeoutSeconds: 600, onProgress: onProgress);

            if (result.exitCode != 0)
            {
                // Combine stdout and stderr for error details since Python may print to stdout
                string errorDetails = !string.IsNullOrWhiteSpace(result.error) 
                    ? result.error 
                    : result.output;
                return (null, $"Analysis failed: {errorDetails}");
            }

            // Parse the JSON result from stdout
            string jsonResult = ExtractResultJson(result.output);
            if (string.IsNullOrEmpty(jsonResult))
                return (null, $"No result from analysis. Output:\n{result.output}");

            // Deserialize into Song
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var song = JsonSerializer.Deserialize<Song>(jsonResult, options);
            if (song == null)
                return (null, "Failed to parse analysis result");

            song.FolderPath = songFolderPath;
            return (song, "Analysis complete!");
        }
        catch (Exception ex)
        {
            return (null, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Result of music-driven downbeat (beat 1) detection. Times are in milliseconds.
    /// <see cref="Phase"/> is how many beats (0..timeSig-1) the grid anchor must shift forward so
    /// bar 1 lands on the true musical downbeat. <see cref="CorrectedAnchorMs"/> already has that
    /// shift applied. <see cref="Confidence"/> (0..1) is how dominant the winning phase was.
    /// </summary>
    public readonly record struct DownbeatResult(
        double Bpm, double BeatIntervalMs, double BarDurationMs, int TimeSig,
        double OriginalAnchorMs, int Phase, double ShiftMs, double CorrectedAnchorMs,
        double Confidence, bool SnareBackbeatOk, string Method, double TotalDurationMs,
        GridFit GridFit, System.Collections.Generic.List<double> CorrectedBeatsMs);

    /// <summary>
    /// How the beat grid was (or would be) built. <see cref="Recommended"/> is the analyzer's
    /// auto verdict ("even" for constant tempo, "following" for variable). <see cref="Applied"/> is
    /// what was actually emitted (honors a forced mode). <see cref="FitMaxDevMs"/> is the worst
    /// deviation of a single even grid from the detected beats — small = constant tempo.
    /// </summary>
    public readonly record struct GridFit(
        string Recommended, string Applied, bool EvenFits,
        double FitMaxDevMs, double FitRmsMs, double RawJitterMs, double FitBpm)
    {
        public bool IsConstantTempo => EvenFits;
    }

    /// <summary>
    /// Detect the TRUE musical downbeat (beat 1) from the stems — drums (kick/snare), bass and
    /// piano/guitar chord changes — independent of the click track's own accent. Fast: no Whisper,
    /// no LLM, no section detection. Requires Demucs-separated stems (drums/bass + a harmonic stem)
    /// in the song folder. Returns null with a message on failure.
    /// </summary>
    public async Task<(DownbeatResult? result, string message)> DetectDownbeatAsync(
        string songFolderPath, Action<int, string>? onProgress = null, string? gridMode = null)
    {
        if (!File.Exists(_scriptPath))
            return (null, $"Analysis script not found: {_scriptPath}");
        if (!Directory.Exists(songFolderPath))
            return (null, $"Song folder not found: {songFolderPath}");

        try
        {
            var args = $"\"{_scriptPath}\" \"{songFolderPath}\" --detect-downbeat";
            if (!string.IsNullOrEmpty(gridMode))
                args += $" --grid-mode {gridMode}";
            var run = await RunCommandWithProgressAsync(_pythonPath, args,
                timeoutSeconds: 300, onProgress: onProgress);

            if (run.exitCode != 0)
            {
                string errorDetails = !string.IsNullOrWhiteSpace(run.error) ? run.error : run.output;
                return (null, $"Downbeat detection failed: {errorDetails}");
            }

            string jsonResult = ExtractResultJson(run.output);
            if (string.IsNullOrEmpty(jsonResult))
                return (null, $"No result from downbeat detection. Output:\n{run.output}");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(jsonResult);
            var root = doc.RootElement;

            double GetD(string name, double dflt = 0) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : dflt;
            int GetI(string name, int dflt = 0) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : dflt;
            bool GetB(string name) =>
                root.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True);
            string GetS(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

            // Parse the nested grid_fit object (verdict + metrics) for the analyze-first dialog.
            GridFit gridFit = default;
            if (root.TryGetProperty("grid_fit", out var gf) && gf.ValueKind == JsonValueKind.Object)
            {
                double GfD(string n) => gf.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                bool GfB(string n) => gf.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                string GfS(string n) => gf.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
                gridFit = new GridFit(
                    Recommended: GfS("recommended"),
                    Applied: GfS("applied"),
                    EvenFits: GfB("even_fits"),
                    FitMaxDevMs: GfD("fit_max_dev_ms"),
                    FitRmsMs: GfD("fit_rms_ms"),
                    RawJitterMs: GfD("raw_jitter_ms"),
                    FitBpm: GfD("fit_bpm"));
            }

            var correctedBeats = new System.Collections.Generic.List<double>();
            if (root.TryGetProperty("corrected_beats_ms", out var cb) && cb.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in cb.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Number) correctedBeats.Add(e.GetDouble());
            }

            var result = new DownbeatResult(
                Bpm: GetD("bpm"),
                BeatIntervalMs: GetD("beat_interval_ms"),
                BarDurationMs: GetD("bar_duration_ms"),
                TimeSig: GetI("time_sig", 4),
                OriginalAnchorMs: GetD("original_anchor_ms"),
                Phase: GetI("phase"),
                ShiftMs: GetD("shift_ms"),
                CorrectedAnchorMs: GetD("corrected_anchor_ms"),
                Confidence: GetD("confidence"),
                SnareBackbeatOk: GetB("snare_backbeat_ok"),
                Method: GetS("method"),
                TotalDurationMs: GetD("total_duration_ms"),
                GridFit: gridFit,
                CorrectedBeatsMs: correctedBeats);

            return (result, "Downbeat detected.");
        }
        catch (Exception ex)
        {
            return (null, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract the JSON result line from Python output.
    /// </summary>
    private string ExtractResultJson(string output)
    {
        var lines = output.Split('\n');
        bool foundMarker = false;
        foreach (var line in lines)
        {
            if (line.Trim() == "__RESULT_JSON__")
            {
                foundMarker = true;
                continue;
            }
            if (foundMarker && line.Trim().StartsWith("{"))
            {
                return line.Trim();
            }
        }
        return "";
    }

    /// <summary>
    /// Set the Python executable path.
    /// </summary>
    public void SetPythonPath(string path)
    {
        _pythonPath = path;
    }

    // ─── PYTHON DISCOVERY (FEAT-002) ──────────────────────────────────────
    // Three cleanly-separated pieces:
    //   1. PythonCandidate            — a value describing one thing to try.
    //   2. ResolvePythonExecutable    — a PURE, side-effect-free selector driven by an INJECTED
    //                                   probe delegate (unit-testable with no process launches).
    //   3. BuildCandidateOrder        — builds the ordered candidate list from the environment
    //                                   (touches filesystem/env; compile-only verifiable here).
    //   4. ProbeCandidate             — the real probe (per-candidate process launches; compile
    //                                   -only verifiable here) that the UI passes into the pure
    //                                   resolver.

    /// <summary>
    /// Case-insensitive segment that marks the Windows Store alias stub (a fake 0-byte
    /// python.exe placeholder). Any candidate resolving under this path must NEVER be selected.
    /// </summary>
    public const string WindowsAppsStubSegment = @"\Microsoft\WindowsApps\";

    /// <summary>
    /// One thing to try when discovering Python. <see cref="ExecutablePath"/> is the executable to
    /// launch (e.g. "python", "py", or a concrete python.exe). <see cref="ArgumentPrefix"/> is an
    /// optional leading argument used for the Windows "py -3" launcher case; null/empty for a
    /// plain interpreter. <see cref="Description"/> is a short human-readable label for the dialog.
    /// </summary>
    public readonly record struct PythonCandidate(
        string ExecutablePath, string? ArgumentPrefix = null, string? Description = null)
    {
        /// <summary>True when this candidate is the Windows "py" launcher rather than a concrete exe.</summary>
        public bool IsLauncher => !string.IsNullOrWhiteSpace(ArgumentPrefix);

        public string Label => Description
            ?? (IsLauncher ? $"{ExecutablePath} {ArgumentPrefix}" : ExecutablePath);
    }

    /// <summary>
    /// Result of probing one candidate. <see cref="VersionOk"/> means `--version` exited 0 and
    /// reported "Python 3". <see cref="PackagesOk"/> means the librosa/numpy/scipy import check
    /// passed. <see cref="ResolvedConcretePath"/> is the concrete python.exe (used for the `py -3`
    /// launcher case so the chosen path is always a concrete interpreter). <see cref="Detail"/> is
    /// a short reason string for the dialog.
    /// </summary>
    public readonly record struct PythonProbeResult(
        bool VersionOk, bool PackagesOk, string? ResolvedConcretePath, string Detail);

    /// <summary>
    /// PURE, side-effect-free resolver. Given a configured path, an ordered candidate list, and an
    /// INJECTED probe delegate, returns the chosen concrete python path (or null) plus a list of
    /// human-readable descriptions of everything tried and why it was rejected. Performs NO
    /// File/Process access itself — all I/O lives in the injected <paramref name="probe"/> — so it
    /// is fully unit-testable. Never throws on empty/null/garbage inputs.
    ///
    /// Rules:
    ///   (a) the configured path (when non-empty) is placed FIRST;
    ///   (b) any candidate whose path OR resolved concrete path contains
    ///       <see cref="WindowsAppsStubSegment"/> (case-insensitive) is ALWAYS rejected;
    ///   (c) candidates are tried in order; the FIRST with VersionOk AND PackagesOk wins;
    ///   (d) for a `py -3` launcher win, the returned path is the resolved concrete python.exe.
    /// </summary>
    public static (string? chosenPath, IReadOnlyList<string> triedDescriptions) ResolvePythonExecutable(
        string? configuredPath,
        IEnumerable<PythonCandidate> candidateOrder,
        Func<PythonCandidate, PythonProbeResult> probe)
    {
        var tried = new List<string>();

        // Build the effective order: configured path first (when non-empty), then the rest.
        var ordered = new List<PythonCandidate>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            ordered.Add(BuildConfiguredCandidate(configuredPath!.Trim()));
        if (candidateOrder != null)
            ordered.AddRange(candidateOrder);

        foreach (var candidate in ordered)
        {
            string label = candidate.Label;

            // (b) Reject the WindowsApps stub up front — never even trust the probe for it.
            if (ContainsWindowsAppsStub(candidate.ExecutablePath))
            {
                tried.Add($"{label}: skipped (Windows Store stub under {WindowsAppsStubSegment.Trim('\\')})");
                continue;
            }

            PythonProbeResult result;
            try
            {
                result = probe(candidate);
            }
            catch (Exception ex)
            {
                // A probe that throws must never crash discovery.
                tried.Add($"{label}: error ({ex.Message})");
                continue;
            }

            // A `py -3` launcher may resolve to a concrete exe that is itself the stub.
            if (ContainsWindowsAppsStub(result.ResolvedConcretePath))
            {
                tried.Add($"{label}: skipped (resolved to Windows Store stub)");
                continue;
            }

            if (!result.VersionOk)
            {
                tried.Add($"{label}: version check failed ({result.Detail})");
                continue;
            }

            if (!result.PackagesOk)
            {
                tried.Add($"{label}: packages missing ({result.Detail})");
                continue;
            }

            // Winner. For a launcher, prefer the concrete python.exe it resolved to.
            string chosen = !string.IsNullOrWhiteSpace(result.ResolvedConcretePath)
                ? result.ResolvedConcretePath!
                : candidate.ExecutablePath;
            tried.Add($"{label}: OK ({result.Detail})");
            return (chosen, tried);
        }

        return (null, tried);
    }

    /// <summary>
    /// Turn a user-configured Settings value into a candidate. Usually this is a concrete
    /// python.exe, but a user may enter the Windows launcher instead (e.g. "py", "py -3",
    /// "py -3.13"). Detect that launcher form and build a launcher candidate (ExecutablePath "py"
    /// + ArgumentPrefix) so the probe resolves it to a concrete interpreter, instead of trying to
    /// Process.Start a bare "py -3" string as if it were one executable path (which would always
    /// fail). Anything else is treated as a concrete executable path, exactly as before.
    /// This is pure string handling — no File/Process access.
    /// </summary>
    /// <summary>
    /// True when a configured Settings value is the Windows "py" launcher form (e.g. "py",
    /// "py -3") rather than a concrete python.exe path. Pure string check (no I/O). Used by the UI
    /// so the "configured path not used" notice isn't wrongly shown when a launcher config
    /// correctly resolves to a concrete interpreter.
    /// </summary>
    public static bool IsLauncherConfiguredValue(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return false;
        string trimmed = configured.Trim();
        string[] parts = trimmed.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1
            && string.Equals(parts[0], "py", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains('\\')
            && !trimmed.Contains('/');
    }

    private static PythonCandidate BuildConfiguredCandidate(string configured)
    {
        // "py", "py -3", "py -3.13" etc. become a launcher candidate; a real path like
        // "C:\Python 3\python.exe" contains a separator and is never misread as a launcher.
        if (IsLauncherConfiguredValue(configured))
        {
            string[] parts = configured.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string? argPrefix = parts.Length > 1 ? string.Join(' ', parts[1..]) : null;
            return new PythonCandidate("py", argPrefix,
                $"configured launcher ({configured})");
        }

        return new PythonCandidate(configured, Description: $"configured path ({configured})");
    }

    private static bool ContainsWindowsAppsStub(string? path)
        => !string.IsNullOrEmpty(path)
           && path!.Contains(WindowsAppsStubSegment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Build the ordered list of python candidates from the environment (EXCLUDING the configured
    /// path, which the pure resolver prepends). Order: `py -3` then bare `py`; real install
    /// locations under %LocalAppData% and Program Files; then bare `python`/`python3` on PATH.
    /// Glob expansion is guarded by try/catch so a missing base directory never throws.
    /// Touches the filesystem/environment, so it is compile-only verifiable on a non-Windows host.
    /// </summary>
    public static IReadOnlyList<PythonCandidate> BuildCandidateOrder()
    {
        var candidates = new List<PythonCandidate>
        {
            // Windows "py" launcher — prefer an explicit Python 3 then the bare launcher.
            new PythonCandidate("py", "-3", "py -3 launcher"),
            new PythonCandidate("py", null, "py launcher"),
        };

        string localAppData = SafeGetFolder(Environment.SpecialFolder.LocalApplicationData);

        // %LocalAppData%\Python\pythoncore-*\python.exe
        if (!string.IsNullOrEmpty(localAppData))
        {
            AddGlobbedExecutables(candidates,
                Path.Combine(localAppData, "Python"), "pythoncore-*", "python.exe");
            // %LocalAppData%\Programs\Python\Python3*\python.exe
            AddGlobbedExecutables(candidates,
                Path.Combine(localAppData, "Programs", "Python"), "Python3*", "python.exe");
        }

        // C:\Program Files\Python3*\python.exe
        string programFiles = SafeGetFolder(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
            AddGlobbedExecutables(candidates, programFiles, "Python3*", "python.exe");

        // C:\Python3*\python.exe
        AddGlobbedExecutables(candidates, @"C:\", "Python3*", "python.exe");

        // Finally, bare interpreters resolved via PATH.
        candidates.Add(new PythonCandidate("python", null, "python (PATH)"));
        candidates.Add(new PythonCandidate("python3", null, "python3 (PATH)"));

        return candidates;
    }

    private static string SafeGetFolder(Environment.SpecialFolder folder)
    {
        try { return Environment.GetFolderPath(folder); }
        catch { return ""; }
    }

    /// <summary>
    /// Expand <paramref name="baseDir"/>\<paramref name="dirGlob"/>\<paramref name="exeName"/> and
    /// append any matches as concrete candidates. Fully guarded so a missing/inaccessible base
    /// directory simply yields nothing.
    /// </summary>
    private static void AddGlobbedExecutables(
        List<PythonCandidate> candidates, string baseDir, string dirGlob, string exeName)
    {
        try
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                return;

            foreach (var dir in Directory.GetDirectories(baseDir, dirGlob))
            {
                try
                {
                    string exe = Path.Combine(dir, exeName);
                    if (File.Exists(exe))
                        candidates.Add(new PythonCandidate(exe, null, exe));
                }
                catch
                {
                    // Skip an inaccessible directory entry and keep going.
                }
            }
        }
        catch
        {
            // Missing base dir / permission issue — just contribute no candidates.
        }
    }

    /// <summary>
    /// The real probe used by the UI. Reuses CheckPythonAvailable semantics so "works" == "usable
    /// for analysis". For a plain exe: run `--version` (exit 0 AND output starts with "Python 3")
    /// then the librosa/numpy/scipy import check. For a `py -3` launcher: first resolve the
    /// concrete interpreter via `py -3 -c "import sys;print(sys.executable)"`, reject it if under
    /// the WindowsApps stub, then probe that concrete exe. Every launch is wrapped in try/catch so
    /// a missing exe / Win32Exception just yields VersionOk=false and discovery continues.
    /// Distinguishes "version ok but packages missing" so the dialog can say so.
    /// Does per-candidate process launches, so it is compile-only verifiable on this sandbox.
    /// </summary>
    public static PythonProbeResult ProbeCandidate(PythonCandidate candidate)
    {
        try
        {
            if (candidate.IsLauncher)
            {
                // Resolve the concrete interpreter the launcher would use.
                var resolve = RunProcess(candidate.ExecutablePath,
                    $"{candidate.ArgumentPrefix} -c \"import sys;print(sys.executable)\"");
                if (resolve.exitCode != 0)
                    return new PythonProbeResult(false, false, null,
                        $"{candidate.Label} could not resolve an interpreter");

                string concrete = resolve.output.Trim().Split('\n').FirstOrDefault()?.Trim() ?? "";
                if (string.IsNullOrEmpty(concrete))
                    return new PythonProbeResult(false, false, null,
                        $"{candidate.Label} returned no interpreter path");

                if (ContainsWindowsAppsStub(concrete))
                    return new PythonProbeResult(false, false, concrete,
                        "resolved to Windows Store stub");

                var launcherProbe = ProbeConcreteExecutable(concrete);
                return launcherProbe with { ResolvedConcretePath = concrete };
            }

            return ProbeConcreteExecutable(candidate.ExecutablePath);
        }
        catch (Exception ex)
        {
            // Includes Win32Exception when the exe is missing/unlaunchable.
            return new PythonProbeResult(false, false, null, ex.Message);
        }
    }

    /// <summary>
    /// Run `--version` then the import check against a concrete interpreter path, never throwing.
    /// </summary>
    private static PythonProbeResult ProbeConcreteExecutable(string exePath)
    {
        try
        {
            var version = RunProcess(exePath, "--version");
            bool versionOk = version.exitCode == 0
                && version.output.Trim().StartsWith("Python 3", StringComparison.Ordinal);
            if (!versionOk)
                return new PythonProbeResult(false, false, null,
                    $"not a usable Python 3 ({version.output.Trim()}{version.error.Trim()})");

            string pyVersion = version.output.Trim();

            var libCheck = RunProcess(exePath,
                "-c \"import librosa; import numpy; import scipy; print('OK')\"");
            bool packagesOk = libCheck.exitCode == 0 && libCheck.output.Contains("OK");
            if (!packagesOk)
            {
                string missing = !string.IsNullOrWhiteSpace(libCheck.error)
                    ? libCheck.error.Trim()
                    : libCheck.output.Trim();
                return new PythonProbeResult(true, false, null, $"{pyVersion}; {missing}");
            }

            return new PythonProbeResult(true, true, null, pyVersion);
        }
        catch (Exception ex)
        {
            return new PythonProbeResult(false, false, null, ex.Message);
        }
    }

    /// <summary>
    /// Static, short-timeout process launcher used by the real probe. Mirrors
    /// <see cref="RunCommandAsync"/>: it starts the concurrent stdout/stderr reads FIRST, then
    /// waits for exit with a hard timeout, and KILLS the process if the timeout elapses. This
    /// matters because a probe candidate may be a hung interpreter — reading both pipes
    /// concurrently prevents a full stderr buffer from deadlocking the read, and the kill ensures
    /// the timeout actually bounds a wedged child instead of being dead code.
    /// </summary>
    private static (int exitCode, string output, string error) RunProcess(
        string exe, string args, int timeoutMs = 10000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start process");

        // Start reading BOTH streams before waiting so a full pipe buffer can never deadlock.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            // Hung or flooding interpreter — kill it so the timeout genuinely bounds discovery.
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return (-1, "", $"Timed out after {timeoutMs} ms");
        }

        // Process has exited; the read tasks are complete or will complete immediately.
        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        return (process.ExitCode, output, error);
    }

    private (int exitCode, string output, string error) RunCommand(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start process");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        return (process.ExitCode, output, error);
    }

    private async Task<(int exitCode, string output, string error)> RunCommandAsync(
        string exe, string args, int timeoutSeconds = 60)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        bool exited = await Task.Run(() =>
            process.WaitForExit(timeoutSeconds * 1000));

        if (!exited)
        {
            process.Kill();
            return (-1, "", "Analysis timed out");
        }

        string output = await outputTask;
        string error = await errorTask;
        return (process.ExitCode, output, error);
    }

    /// <summary>
    /// Run command with real-time progress reporting via stdout line parsing.
    /// Lines starting with __PROGRESS__ are parsed for progress updates.
    /// Format: __PROGRESS__|percent|description
    /// </summary>
    private async Task<(int exitCode, string output, string error)> RunCommandWithProgressAsync(
        string exe, string args, int timeoutSeconds = 180,
        Action<int, string>? onProgress = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start process");

        var outputBuilder = new System.Text.StringBuilder();
        var errorTask = process.StandardError.ReadToEndAsync();

        // Read stdout line by line for progress updates
        var readTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                string? line = await process.StandardOutput.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("__PROGRESS__"))
                {
                    // Parse: __PROGRESS__|percent|description
                    var parts = line.Split('|', 3);
                    if (parts.Length >= 3 && int.TryParse(parts[1], out int percent))
                    {
                        onProgress?.Invoke(percent, parts[2]);
                    }
                }
                else
                {
                    outputBuilder.AppendLine(line);
                }
            }
        });

        bool exited = await Task.Run(() =>
            process.WaitForExit(timeoutSeconds * 1000));

        if (!exited)
        {
            process.Kill();
            return (-1, "", "Analysis timed out");
        }

        await readTask;
        string output = outputBuilder.ToString();
        string error = await errorTask;
        return (process.ExitCode, output, error);
    }

    /// <summary>
    /// Check if NVIDIA GPU is available and if PyTorch has CUDA support.
    /// Returns: (hasNvidiaGpu, hasCudaTorch, cudaDeviceName)
    /// </summary>
    public (bool hasNvidiaGpu, bool hasCudaTorch, string deviceInfo) CheckCudaAvailability()
    {
        try
        {
            // Check if NVIDIA GPU exists (nvidia-smi)
            var nvidiaCheck = RunCommand("nvidia-smi", "--query-gpu=name --format=csv,noheader,nounits");
            bool hasNvidiaGpu = nvidiaCheck.exitCode == 0 && !string.IsNullOrWhiteSpace(nvidiaCheck.output);
            string gpuName = hasNvidiaGpu ? nvidiaCheck.output.Trim().Split('\n')[0].Trim() : "";

            if (!hasNvidiaGpu)
                return (false, false, "No NVIDIA GPU detected");

            // Check if PyTorch has CUDA support
            var cudaCheck = RunCommand(_pythonPath,
                "-c \"import torch; print('CUDA:' + str(torch.cuda.is_available()) + '|' + (torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'N/A'))\"");

            if (cudaCheck.exitCode == 0 && cudaCheck.output.Contains("CUDA:True"))
            {
                string deviceName = cudaCheck.output.Trim().Split('|').LastOrDefault() ?? gpuName;
                return (true, true, deviceName);
            }

            return (true, false, gpuName);
        }
        catch
        {
            return (false, false, "Check failed");
        }
    }

    /// <summary>
    /// Install CUDA-enabled PyTorch. This downloads ~2.5GB on first run.
    /// Returns success and any error message.
    /// </summary>
    public async Task<(bool success, string message)> InstallCudaPyTorchAsync(
        Action<string>? onStatus = null)
    {
        try
        {
            onStatus?.Invoke("Uninstalling CPU-only PyTorch...");

            // Uninstall current torch first
            var uninstall = await RunCommandAsync(_pythonPath,
                "-m pip uninstall torch torchaudio torchvision -y", timeoutSeconds: 60);

            onStatus?.Invoke("Downloading CUDA-enabled PyTorch (~2.5GB)...\nThis is a one-time download for GPU acceleration.");

            // Install CUDA version (cu121 = CUDA 12.1, widely compatible)
            var install = await RunCommandAsync(_pythonPath,
                "-m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu121",
                timeoutSeconds: 600);

            if (install.exitCode != 0)
            {
                // Try cu124 as fallback
                onStatus?.Invoke("Trying alternative CUDA version...");
                install = await RunCommandAsync(_pythonPath,
                    "-m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu124",
                    timeoutSeconds: 600);
            }

            if (install.exitCode != 0)
            {
                return (false, $"Installation failed:\n{install.error}");
            }

            // Verify CUDA works
            onStatus?.Invoke("Verifying CUDA installation...");
            var verify = RunCommand(_pythonPath,
                "-c \"import torch; assert torch.cuda.is_available(); print('CUDA OK: ' + torch.cuda.get_device_name(0))\"");

            if (verify.exitCode == 0 && verify.output.Contains("CUDA OK"))
            {
                string gpuName = verify.output.Trim().Replace("CUDA OK: ", "");
                return (true, $"GPU acceleration enabled!\nUsing: {gpuName}");
            }

            return (false, "CUDA PyTorch installed but GPU not detected.\n" +
                "This may mean your NVIDIA drivers need updating.");
        }
        catch (Exception ex)
        {
            return (false, $"Installation error: {ex.Message}");
        }
    }
}
