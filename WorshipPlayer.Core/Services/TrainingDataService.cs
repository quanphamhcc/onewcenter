using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// AI Training Data Service — logs every corrected song as training data.
/// Used for few-shot learning (Option C) and future model fine-tuning (Option A).
/// 
/// Each song save creates a training record with:
/// - Audio features (BPM, key, energy profile, track count)
/// - Final corrected section map
/// - Beat positions
/// - Metadata (whether cue track was used, manual corrections made)
/// </summary>
public class TrainingDataService
{
    private string _logFolderPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public string LogFolderPath => _logFolderPath;
    public bool IsConfigured => !string.IsNullOrEmpty(_logFolderPath) && Directory.Exists(_logFolderPath);

    public TrainingDataService(string logFolderPath)
    {
        _logFolderPath = logFolderPath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public void SetLogFolder(string path)
    {
        _logFolderPath = path;
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Save a song's final state as training data after user correction.
    /// </summary>
    public async Task SaveTrainingDataAsync(Song song, bool wasCueTrackUsed, bool wasManuallyEdited)
    {
        if (!IsConfigured) return;

        var record = new TrainingRecord
        {
            Timestamp = DateTime.UtcNow,
            SongTitle = song.Title,
            Bpm = song.Bpm,
            Key = song.OriginalKey,
            TimeSignature = $"{song.TimeSignatureNumerator}/4",
            TrackCount = song.Tracks.Count,
            TrackLabels = song.Tracks.Select(t => t.Label).ToList(),
            HasClickTrack = song.Tracks.Any(t => t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase)),
            HasCueTrack = song.Tracks.Any(t => t.Label.Contains("Cue", StringComparison.OrdinalIgnoreCase)),
            WasCueTrackUsed = wasCueTrackUsed,
            WasManuallyEdited = wasManuallyEdited,
            BeatPositionsMs = song.BeatPositionsMs,
            Sections = song.Sections.Select(s => new TrainingSectionData
            {
                Name = s.Name,
                StartBar = s.StartBar,
                EndBar = s.EndBar,
                LoopInMs = s.LoopInMs,
                LoopOutMs = s.LoopOutMs,
                DurationMs = s.LoopOutMs - s.LoopInMs
            }).ToList(),
            EnergyProfile = ComputeEnergyProfile(song),
            SectionPattern = string.Join(" → ", song.Sections.Select(s => s.Name))
        };

        // Save as individual JSON file
        string safeName = string.Join("_", song.Title.Split(Path.GetInvalidFileNameChars()));
        string filename = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string filepath = Path.Combine(_logFolderPath, filename);

        var json = JsonSerializer.Serialize(record, _jsonOptions);
        await File.WriteAllTextAsync(filepath, json);
    }

    /// <summary>
    /// Find similar songs from training history (for few-shot LLM prompting).
    /// Similarity based on: BPM proximity, same key, similar track count.
    /// </summary>
    public List<TrainingRecord> FindSimilarSongs(Song newSong, int maxResults = 3)
    {
        if (!IsConfigured) return new();

        var records = LoadAllRecords();
        if (records.Count == 0) return new();

        // Score each record by similarity
        var scored = records.Select(r => new
        {
            Record = r,
            Score = ComputeSimilarity(newSong, r)
        })
        .OrderByDescending(x => x.Score)
        .Take(maxResults)
        .Where(x => x.Score > 0.3) // Minimum similarity threshold
        .Select(x => x.Record)
        .ToList();

        return scored;
    }

    /// <summary>
    /// Get all training records (for export/analysis).
    /// </summary>
    public List<TrainingRecord> LoadAllRecords()
    {
        if (!IsConfigured) return new();

        var records = new List<TrainingRecord>();
        foreach (var file in Directory.GetFiles(_logFolderPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<TrainingRecord>(json, _jsonOptions);
                if (record != null)
                    records.Add(record);
            }
            catch { }
        }
        return records;
    }

    private double ComputeSimilarity(Song song, TrainingRecord record)
    {
        double score = 0;

        // BPM proximity (within 10 BPM = high score)
        double bpmDiff = Math.Abs(song.Bpm - record.Bpm);
        score += Math.Max(0, 1.0 - bpmDiff / 20.0) * 0.3;

        // Same key
        if (song.OriginalKey == record.Key)
            score += 0.2;

        // Similar track count
        double trackDiff = Math.Abs(song.Tracks.Count - record.TrackCount);
        score += Math.Max(0, 1.0 - trackDiff / 5.0) * 0.2;

        // Similar section count
        double sectionDiff = Math.Abs(song.Sections.Count - record.Sections.Count);
        score += Math.Max(0, 1.0 - sectionDiff / 5.0) * 0.15;

        // Has same track types (click, cue, etc.)
        bool bothHaveClick = song.Tracks.Any(t => t.Label.Contains("Click", StringComparison.OrdinalIgnoreCase)) == record.HasClickTrack;
        if (bothHaveClick) score += 0.15;

        return score;
    }

    private List<double> ComputeEnergyProfile(Song song)
    {
        // Simplified energy profile: relative energy per section (0-1)
        if (song.Sections.Count == 0) return new();
        double maxDuration = song.Sections.Max(s => s.LoopOutMs - s.LoopInMs);
        return song.Sections.Select(s => (s.LoopOutMs - s.LoopInMs) / maxDuration).ToList();
    }
}

/// <summary>
/// A training record for one song — saved after user correction.
/// </summary>
public class TrainingRecord
{
    public DateTime Timestamp { get; set; }
    public string SongTitle { get; set; } = "";
    public double Bpm { get; set; }
    public string Key { get; set; } = "";
    public string TimeSignature { get; set; } = "4/4";
    public int TrackCount { get; set; }
    public List<string> TrackLabels { get; set; } = new();
    public bool HasClickTrack { get; set; }
    public bool HasCueTrack { get; set; }
    public bool WasCueTrackUsed { get; set; }
    public bool WasManuallyEdited { get; set; }
    public List<double>? BeatPositionsMs { get; set; }
    public List<TrainingSectionData> Sections { get; set; } = new();
    public List<double> EnergyProfile { get; set; } = new();
    public string SectionPattern { get; set; } = "";
}

public class TrainingSectionData
{
    public string Name { get; set; } = "";
    public int StartBar { get; set; }
    public int EndBar { get; set; }
    public double LoopInMs { get; set; }
    public double LoopOutMs { get; set; }
    public double DurationMs { get; set; }
}
