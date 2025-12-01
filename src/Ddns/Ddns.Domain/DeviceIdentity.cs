namespace Ddns.Domain;

/// <summary>
/// Everything known about an inbound registration that could establish *who*
/// the caller is. v1 uses the route value; under mTLS the
/// <see cref="ClientCertificateSubject"/> is populated from the API Gateway
/// request context. The resolver — not the endpoint — decides which wins.
/// </summary>
public sealed record DeviceIdentityContext
{
    /// <summary>The <c>{deviceId}</c> path segment the caller addressed.</summary>
    public string? RouteDeviceId { get; init; }

    /// <summary>
    /// Subject (CN/SAN) of a validated client certificate, when mutual TLS is in
    /// effect. When mTLS is not active, or the endpoint is running locally
    /// without a client certificate, this is <c>null</c>.
    /// </summary>
    public string? ClientCertificateSubject { get; init; }
}

/// <summary>The resolved, trusted identity a registration is recorded against.</summary>
public sealed record DeviceIdentity
{
    public required string DeviceId { get; init; }

    /// <summary><c>"request"</c> or <c>"client-cert"</c>.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// The single seam that decides a device's identity. The endpoint and storage
/// never change when auth lands — only the registered implementation of this
/// interface does.
///
/// <c>RequestDeviceIdentityResolver</c> trusts the route value.
/// <c>ClientCertificateDeviceIdentityResolver</c> reads
/// <see cref="DeviceIdentityContext.ClientCertificateSubject"/>.
/// </summary>
public interface IDeviceIdentityResolver
{
    DeviceIdentity Resolve(DeviceIdentityContext context);
}
