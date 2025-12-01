namespace Ddns.Domain;

/// <summary>
/// A device's current network location in the fleet IP inventory. Immutable;
/// re-registering a device replaces the whole record (last-write-wins on
/// <see cref="LastSeen"/>).
/// </summary>
public sealed record DeviceRegistration
{
    /// <summary>Canonical device identity (see <see cref="IDeviceIdentityResolver"/>).</summary>
    public required string DeviceId { get; init; }

    /// <summary>Normalized IPv4 or IPv6 address the device reported.</summary>
    public required string IpAddress { get; init; }

    /// <summary>When the registration was accepted (server clock, UTC).</summary>
    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>
    /// How the device identity was established: <c>"request"</c> in v1 (caller-supplied
    /// route value), <c>"client-cert"</c> once mutual-TLS lands. Recorded so the
    /// inventory is auditable across the auth transition.
    /// </summary>
    public required string DeviceIdentitySource { get; init; }
}
