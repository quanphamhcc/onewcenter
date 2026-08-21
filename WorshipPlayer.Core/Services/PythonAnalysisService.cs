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
        Dictionary<string, string>? sectionLyrics = null)
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

            // Run the Python analysis script with LLM mode flag and optional quality/BPM
            var args = $"\"{_scriptPath}\" \"{songFolderPath}\" --llm {llmMode}";
            if (!string.IsNullOrEmpty(quality) && quality != "default")
                args += $" --quality {quality}";
            if (bpmOverride.HasValue && bpmOverride.Value > 0)
                args += $" --bpm {bpmOverride.Value:F1}";

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
