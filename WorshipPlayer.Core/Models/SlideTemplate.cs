using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Extended slide template that builds on SlideTheme with additional formatting options.
/// Includes text transforms, letter spacing, line backgrounds, borders, and pixel margins.
/// </summary>
public class SlideTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default Template";

    [JsonPropertyName("baseThemeId")]
    public string? BaseThemeId { get; set; }

    // ─── Text Formatting ───────────────────────────────────────────
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 60;

    [JsonPropertyName("fontWeight")]
    public string FontWeight { get; set; } = "Bold";

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("fontItalic")]
    public bool FontItalic { get; set; } = false;

    [JsonPropertyName("horizontalAlignment")]
    public TextAlignment HorizontalAlignment { get; set; } = TextAlignment.Center;

    [JsonPropertyName("verticalAlignment")]
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Center;

    [JsonPropertyName("hasShadow")]
    public bool HasShadow { get; set; } = true;

    [JsonPropertyName("shadowColor")]
    public string ShadowColor { get; set; } = "#000000";

    [JsonPropertyName("shadowBlur")]
    public double ShadowBlur { get; set; } = 4.0;

    [JsonPropertyName("hasOutline")]
    public bool HasOutline { get; set; } = false;

    [JsonPropertyName("outlineColor")]
    public string OutlineColor { get; set; } = "#000000";

    [JsonPropertyName("outlineWidth")]
    public double OutlineWidth { get; set; } = 1.0;

    // ─── Advanced Formatting ───────────────────────────────────────
    [JsonPropertyName("textTransform")]
    public TextTransformType TextTransform { get; set; } = TextTransformType.None;

    [JsonPropertyName("letterSpacing")]
    public double LetterSpacing { get; set; } = 0;

    [JsonPropertyName("lineHeight")]
    public double LineHeight { get; set; } = 1.3;

    [JsonPropertyName("lineBackgroundEnabled")]
    public bool LineBackgroundEnabled { get; set; } = false;

    [JsonPropertyName("lineBackgroundColor")]
    public string LineBackgroundColor { get; set; } = "#000000";

    [JsonPropertyName("lineBackgroundOpacity")]
    public double LineBackgroundOpacity { get; set; } = 0.5;

    // ─── Border ────────────────────────────────────────────────────
    [JsonPropertyName("borderStyle")]
    public BorderStyleType BorderStyle { get; set; } = BorderStyleType.None;

    [JsonPropertyName("borderColor")]
    public string BorderColor { get; set; } = "#FFFFFF";

    [JsonPropertyName("borderWidth")]
    public double BorderWidth { get; set; } = 1.0;

    // ─── Margins (pixels) ──────────────────────────────────────────
    [JsonPropertyName("marginTop")]
    public double MarginTop { get; set; } = 40;

    [JsonPropertyName("marginLeft")]
    public double MarginLeft { get; set; } = 60;

    [JsonPropertyName("marginRight")]
    public double MarginRight { get; set; } = 60;

    [JsonPropertyName("marginBottom")]
    public double MarginBottom { get; set; } = 40;

    // ─── Template Metadata ─────────────────────────────────────────
    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; } = false;

    [JsonPropertyName("templateCategory")]
    public TemplateCategory TemplateCategory { get; set; } = TemplateCategory.General;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = "";
}

/// <summary>
/// Text transform options for template text.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextTransformType
{
    None,
    Uppercase,
    Lowercase,
    Capitalize
}

/// <summary>
/// Border style options.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BorderStyleType
{
    None,
    Solid,
    Dashed,
    Dotted
}

/// <summary>
/// Categories for slide templates.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TemplateCategory
{
    General,
    Lyrics,
    Scripture,
    Announcements,
    LowerThird,
    Title,
    Custom
}
