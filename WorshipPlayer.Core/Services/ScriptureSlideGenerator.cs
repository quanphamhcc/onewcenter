using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Generates presentation slides from Bible verses.
/// Splits passages into slides based on configurable lines per slide,
/// with options for verse number display and reference/translation visibility.
/// </summary>
public class ScriptureSlideGenerator
{
    /// <summary>
    /// Options for scripture slide generation.
    /// </summary>
    public class SlideGenerationOptions
    {
        /// <summary>
        /// Maximum number of lines per slide. Default is 4.
        /// </summary>
        public int LinesPerSlide { get; set; } = 4;

        /// <summary>
        /// Whether to show verse numbers inline with text.
        /// </summary>
        public bool ShowVerseNumbers { get; set; } = true;

        /// <summary>
        /// Whether to show the scripture reference (e.g., "John 3:16") on each slide.
        /// </summary>
        public bool ShowReference { get; set; } = true;

        /// <summary>
        /// Whether to show the translation name (e.g., "KJV") on each slide.
        /// </summary>
        public bool ShowTranslationName { get; set; } = true;

        /// <summary>
        /// Approximate max characters per line before wrapping counts as a new line.
        /// Used for estimating line count from verse text.
        /// </summary>
        public int MaxCharsPerLine { get; set; } = 60;
    }

    /// <summary>
    /// Generate slides from a list of Bible verses.
    /// </summary>
    /// <param name="verses">The verses to generate slides from.</param>
    /// <param name="reference">The scripture reference for display purposes.</param>
    /// <param name="options">Generation options (or null for defaults).</param>
    /// <returns>List of slides containing the scripture passage.</returns>
    public List<Slide> GenerateSlides(List<BibleVerse> verses, ScriptureReference reference, SlideGenerationOptions? options = null)
    {
        options ??= new SlideGenerationOptions();
        var slides = new List<Slide>();

        if (verses.Count == 0)
            return slides;

        var currentLines = new List<string>();
        int currentLineCount = 0;
        int slideOrder = 0;

        foreach (var verse in verses)
        {
            var verseText = options.ShowVerseNumbers
                ? $"{verse.Verse} {verse.Text}"
                : verse.Text;

            // Estimate how many lines this verse will take
            var estimatedLines = EstimateLineCount(verseText, options.MaxCharsPerLine);

            // If adding this verse would exceed the limit, create a slide first
            if (currentLineCount > 0 && currentLineCount + estimatedLines > options.LinesPerSlide)
            {
                slides.Add(CreateSlide(currentLines, reference, options, slideOrder++));
                currentLines.Clear();
                currentLineCount = 0;
            }

            currentLines.Add(verseText);
            currentLineCount += estimatedLines;
        }

        // Add the final slide if there is remaining content
        if (currentLines.Count > 0)
        {
            slides.Add(CreateSlide(currentLines, reference, options, slideOrder));
        }

        return slides;
    }

    /// <summary>
    /// Generate slides from raw scripture text (pre-formatted, not from individual verses).
    /// Splits by line count.
    /// </summary>
    /// <param name="scriptureText">The scripture text with newlines.</param>
    /// <param name="referenceDisplay">Reference display string.</param>
    /// <param name="translationName">Translation name to show.</param>
    /// <param name="options">Generation options.</param>
    /// <returns>List of slides.</returns>
    public List<Slide> GenerateSlidesFromText(string scriptureText, string referenceDisplay, string translationName, SlideGenerationOptions? options = null)
    {
        options ??= new SlideGenerationOptions();
        var slides = new List<Slide>();

        if (string.IsNullOrWhiteSpace(scriptureText))
            return slides;

        var lines = scriptureText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        int slideOrder = 0;
        for (int i = 0; i < lines.Count; i += options.LinesPerSlide)
        {
            var slideLines = lines.Skip(i).Take(options.LinesPerSlide).ToList();
            var slideText = string.Join("\n", slideLines);

            var subtitle = "";
            if (options.ShowReference && options.ShowTranslationName)
                subtitle = $"{referenceDisplay} ({translationName})";
            else if (options.ShowReference)
                subtitle = referenceDisplay;
            else if (options.ShowTranslationName)
                subtitle = translationName;

            slides.Add(new Slide
            {
                Type = SlideType.Scripture,
                Order = slideOrder++,
                Content = new SlideContent
                {
                    Text = slideText,
                    Subtitle = subtitle
                }
            });
        }

        return slides;
    }

    private Slide CreateSlide(List<string> lines, ScriptureReference reference, SlideGenerationOptions options, int order)
    {
        var text = string.Join("\n", lines);

        var subtitle = "";
        if (options.ShowReference && options.ShowTranslationName)
            subtitle = $"{reference.ToReferenceString()} ({reference.TranslationId})";
        else if (options.ShowReference)
            subtitle = reference.ToReferenceString();
        else if (options.ShowTranslationName)
            subtitle = reference.TranslationId;

        return new Slide
        {
            Type = SlideType.Scripture,
            Order = order,
            Content = new SlideContent
            {
                Text = text,
                Subtitle = subtitle
            }
        };
    }

    private int EstimateLineCount(string text, int maxCharsPerLine)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        // Count actual newlines plus wrapping estimates
        var segments = text.Split('\n');
        int totalLines = 0;

        foreach (var segment in segments)
        {
            var lines = (int)Math.Ceiling((double)segment.Length / maxCharsPerLine);
            totalLines += Math.Max(1, lines);
        }

        return totalLines;
    }
}
