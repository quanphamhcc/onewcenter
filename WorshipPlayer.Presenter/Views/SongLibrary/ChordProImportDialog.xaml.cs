using System.IO;
using System.Windows;
using Microsoft.Win32;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// Dialog for importing ChordPro format files (.chopro, .chordpro, .cho).
/// Parses the file and shows detected metadata and preview before import.
/// </summary>
public partial class ChordProImportDialog : Window
{
    private ChordProParserService? _parserService;
    private string? _parsedContent;

    public ChordProParserService? ParserService
    {
        get => _parserService;
        set => _parserService = value;
    }

    public string? ImportedTitle { get; private set; }
    public string? ImportedArtist { get; private set; }
    public string? ImportedKey { get; private set; }
    public string? ParsedLyrics { get; private set; }
    public bool Confirmed { get; private set; }

    public ChordProImportDialog()
    {
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select ChordPro File",
            Filter = "ChordPro Files (*.chopro;*.chordpro;*.cho;*.pro;*.crd)|*.chopro;*.chordpro;*.cho;*.pro;*.crd|Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FileName;
            // Prevent path traversal
            if (path.Contains(".."))
            {
                MessageBox.Show("Invalid file path.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FilePathTextBox.Text = path;
            ParseFile(path);
        }
    }

    private void ParseFile(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            ParseContent(content);
        }
        catch (Exception ex)
        {
            PreviewTextBox.Text = $"Error reading file: {ex.Message}";
            ImportButton.IsEnabled = false;
        }
    }

    private void ParseContent(string content)
    {
        // Extract metadata from ChordPro directives
        ImportedTitle = ExtractDirective(content, "title") ?? ExtractDirective(content, "t");
        ImportedArtist = ExtractDirective(content, "artist") ?? ExtractDirective(content, "subtitle") ?? ExtractDirective(content, "st");
        ImportedKey = ExtractDirective(content, "key");

        TitleValue.Text = ImportedTitle ?? "(none)";
        ArtistValue.Text = ImportedArtist ?? "(none)";
        KeyValue.Text = ImportedKey ?? "(none)";

        // Strip chords and show lyrics preview
        _parsedContent = StripChords(content);
        PreviewTextBox.Text = _parsedContent;
        ParsedLyrics = _parsedContent;

        ImportButton.IsEnabled = true;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private static string? ExtractDirective(string content, string directiveName)
    {
        var prefix = $"{{{directiveName}:";
        var altPrefix = $"{{{directiveName}: ";
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].TrimEnd('}').Trim();
            }
            if (trimmed.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[altPrefix.Length..].TrimEnd('}').Trim();
            }
        }
        return null;
    }

    private static string StripChords(string content)
    {
        var lines = content.Split('\n');
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Skip directive lines
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
                continue;

            // Strip chord notation [Chord]
            var stripped = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\[.*?\]", "");
            result.Add(stripped);
        }

        return string.Join(Environment.NewLine, result).Trim();
    }
}
