using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// Configuration for one presenter output (đợt 2 T15 / R8 — nhiều outputs).
/// Replaces the two fixed display indices (<see cref="PresenterSettings.OutputDisplay"/> /
/// <see cref="PresenterSettings.StageDisplay"/>) with a list: each output has a name, a
/// target display index, a purpose (Audience/Stage) and an optional per-output foreground
/// template override (a <see cref="SlideTemplate"/> id).
/// </summary>
/// <remarks>
/// R8 mapping note (xác nhận trước T15, ghi trong watch-state.md 2026-10-23): hành vi ghi
/// đè template foreground theo output có bằng chứng đủ (trang sản phẩm chính thức
/// worshiptools.com mục "Multiple Outputs" + FAQ, và guide bên thứ ba trích dẫn bài docs
/// "Override Foreground Template"); TUY NHIÊN nhãn UI chính xác "Override Foreground
/// Template" trong app WorshipTools KHÔNG xác minh được ở thời điểm này, nên chuỗi đó
/// KHÔNG được dùng làm nhãn UI chính thức của app mình — UI dùng từ ngữ trung lập
/// ("Template override" / "Không ghi đè"). Tên property code
/// <see cref="ForegroundTemplateIdOverride"/> là tên kỹ thuật nội bộ, không phải nhãn UI.
/// </remarks>
public class OutputConfig
{
    /// <summary>
    /// Stable id of this output. Migration-generated ids are fixed constants
    /// (see <see cref="Services.OutputMigration"/>); user-added outputs get a GUID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name, e.g. "Main Audience", "Stage Display", "Livestream".
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Target display (0-based index). -1 = not bound to a display.
    /// </summary>
    [JsonPropertyName("displayIndex")]
    public int DisplayIndex { get; set; } = -1;

    /// <summary>
    /// Role of this output. Reuses <see cref="ScreenPurpose"/>.
    /// </summary>
    [JsonPropertyName("purpose")]
    public ScreenPurpose Purpose { get; set; } = ScreenPurpose.Audience;

    /// <summary>
    /// Optional per-output foreground template override (a <see cref="SlideTemplate"/> id).
    /// Null/empty = no override: the resolver falls through to the item's own template,
    /// then the default (see <see cref="Services.OutputTemplateResolver"/>).
    /// Applies to Audience outputs; Stage outputs use <see cref="ServiceOrder.StageTemplateId"/>
    /// of the item instead (pre-T15 stage behavior is preserved).
    /// </summary>
    [JsonPropertyName("foregroundTemplateIdOverride")]
    public string? ForegroundTemplateIdOverride { get; set; }
}
