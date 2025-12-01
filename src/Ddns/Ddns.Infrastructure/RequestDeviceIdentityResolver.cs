using Ddns.Domain;

namespace Ddns.Infrastructure;

/// <summary>
/// v1 identity resolver: the device is whoever the caller said it is in the
/// route. Auth is deferred, so this is intentionally trusting.
///
/// When mutual TLS is enabled, swap this for
/// <see cref="ClientCertificateDeviceIdentityResolver"/> in
/// <see cref="ServiceCollectionExtensions"/> — no endpoint, service, or storage
/// change. That swap is the entire migration.
/// </summary>
public sealed class RequestDeviceIdentityResolver : IDeviceIdentityResolver
{
    public DeviceIdentity Resolve(DeviceIdentityContext context)
    {
        var deviceId = context.RouteDeviceId?.Trim() ?? string.Empty;
        return new DeviceIdentity { DeviceId = deviceId, Source = "request" };
    }
}
