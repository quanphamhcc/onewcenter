using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Cloud;

namespace WorshipPlayer.Core.Services.Cloud;

/// <summary>
/// Interface for cloud service plan API operations.
/// Allows fetching, creating, and syncing service plans between
/// the cloud planning tool and the desktop app.
/// </summary>
public interface IServicePlanApi
{
    /// <summary>
    /// Get all upcoming service plans from the cloud.
    /// </summary>
    Task<List<ServicePlanDto>> GetUpcomingPlansAsync();

    /// <summary>
    /// Get a specific service plan by ID.
    /// </summary>
    Task<ServicePlanDto?> GetPlanAsync(string planId);

    /// <summary>
    /// Download a cloud plan and convert it to a local ServiceOrder.
    /// This downloads any referenced songs and sets up local paths.
    /// </summary>
    Task<ServiceOrder> SyncPlanAsync(string planId);

    /// <summary>
    /// Create a new service plan in the cloud.
    /// </summary>
    Task<ServicePlanDto> CreatePlanAsync(ServicePlanDto plan);

    /// <summary>
    /// Update an existing service plan in the cloud.
    /// </summary>
    Task<ServicePlanDto> UpdatePlanAsync(ServicePlanDto plan);
}
