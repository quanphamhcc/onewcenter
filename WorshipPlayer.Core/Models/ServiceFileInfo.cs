namespace WorshipPlayer.Core.Models;

/// <summary>
/// A scanned service-order file: its path, last-write timestamp (UTC), and the
/// deserialized <see cref="ServiceOrder"/>. Produced by
/// <see cref="Services.ServiceOrderManager.ListServiceFilesAsync"/> and consumed by
/// <see cref="Services.ServiceListingBuilder"/> to build the display rows.
/// </summary>
public sealed record ServiceFileInfo(string FilePath, DateTime LastWriteTimeUtc, ServiceOrder Order);
