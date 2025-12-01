using System.Net;
using System.Net.Sockets;

namespace Ddns.Domain;

/// <summary>
/// Application service for the fleet IP inventory: resolve identity, validate,
/// persist, look up. All business rules live here so they are testable without
/// DynamoDB or HTTP.
/// </summary>
public sealed class DeviceRegistrationService(
    IDeviceRegistrationRepository repository,
    IDeviceIdentityResolver identityResolver,
    IClock clock)
{
    /// <summary>
    /// Records "this device is at this IP now". <paramref name="identityContext"/>
    /// is resolved through the identity seam (v1: the route value).
    /// </summary>
    /// <exception cref="DomainValidationException">Invalid device id or IP.</exception>
    public async Task<DeviceRegistration> RegisterAsync(
        DeviceIdentityContext identityContext,
        string? reportedIp,
        CancellationToken ct = default)
    {
        var identity = identityResolver.Resolve(identityContext);
        var deviceId = ValidateDeviceId(identity.DeviceId);
        var normalizedIp = NormalizeIp(reportedIp);

        var registration = new DeviceRegistration
        {
            DeviceId = deviceId,
            IpAddress = normalizedIp,
            LastSeen = clock.UtcNow,
            DeviceIdentitySource = identity.Source,
        };

        await repository.UpsertAsync(registration, ct);
        return registration;
    }

    public Task<DeviceRegistration?> GetAsync(string deviceId, CancellationToken ct = default)
        => repository.GetAsync(ValidateDeviceId(deviceId), ct);

    public Task<IReadOnlyList<DeviceRegistration>> ListFleetAsync(CancellationToken ct = default)
        => repository.ListFleetAsync(ct);

    /// <summary>
    /// Device identity is a stable GUID. Accepts standard GUID string
    /// representations and normalizes to canonical lowercase-hyphenated form
    /// so the inventory never stores two spellings of the same identity.
    /// </summary>
    private static string ValidateDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)
            || !Guid.TryParse(deviceId.Trim(), out var guid)
            || guid == Guid.Empty)
        {
            throw new DomainValidationException("Device id must be a valid GUID.");
        }

        return guid.ToString("D");
    }

    /// <summary>
    /// Accepts IPv4/IPv6, rejects anything else, and returns the canonical
    /// textual form so the inventory never stores two spellings of one address.
    /// </summary>
    private static string NormalizeIp(string? reportedIp)
    {
        if (string.IsNullOrWhiteSpace(reportedIp)
            || !IPAddress.TryParse(reportedIp.Trim(), out var parsed)
            || parsed.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            throw new DomainValidationException($"'{reportedIp}' is not a valid IPv4 or IPv6 address.");
        }

        return parsed.ToString();
    }
}
