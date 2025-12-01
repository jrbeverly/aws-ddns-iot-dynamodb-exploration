namespace Ddns.Domain;

/// <summary>
/// Persistence boundary for the fleet IP inventory. The domain depends only on
/// this interface; the DynamoDB implementation lives in Ddns.Infrastructure
/// (dependency direction: specific → shared → external, core-principles.md #7).
/// </summary>
public interface IDeviceRegistrationRepository
{
    /// <summary>Insert or replace a device's registration (last-write-wins).</summary>
    Task UpsertAsync(DeviceRegistration registration, CancellationToken ct = default);

    /// <summary>The single device's registration, or <c>null</c> if never seen.</summary>
    Task<DeviceRegistration?> GetAsync(string deviceId, CancellationToken ct = default);

    /// <summary>
    /// Every device in the fleet — the reverse-lookup inventory. Ordered by
    /// device id for stable output.
    /// </summary>
    Task<IReadOnlyList<DeviceRegistration>> ListFleetAsync(CancellationToken ct = default);
}
