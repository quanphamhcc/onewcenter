namespace WorshipPlayer.Core.Models;

/// <summary>
/// Purely presentational row for the services list. Carries display strings and the
/// file path only — no sort-only fields (sorting happens in
/// <see cref="Services.ServiceListingBuilder"/> before projection), no WPF types, and
/// no change notification. The host passes these straight to the view.
/// </summary>
public sealed class ServiceListRow
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DateDisplay { get; set; } = string.Empty;
    public string LastModifiedDisplay { get; set; } = string.Empty;
    public string LastModifiedBy { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
