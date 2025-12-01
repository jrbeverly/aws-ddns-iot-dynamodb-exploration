using Ddns.Domain;

namespace Ddns.Infrastructure;

/// <summary>
/// mTLS identity resolver: derives device identity from the validated client
/// certificate subject. Expects the CN to be a stable GUID. Register this
/// instead of <see cref="RequestDeviceIdentityResolver"/> in
/// <see cref="ServiceCollectionExtensions"/> to switch the identity model.
/// No endpoint, service, or storage change is needed.
/// </summary>
public sealed class ClientCertificateDeviceIdentityResolver : IDeviceIdentityResolver
{
    public DeviceIdentity Resolve(DeviceIdentityContext context)
    {
        var subject = context.ClientCertificateSubject;
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainValidationException(
                "Client certificate subject is required when mutual TLS is enabled.");
        }

        var cn = ExtractCommonName(subject);
        if (string.IsNullOrWhiteSpace(cn)
            || !Guid.TryParse(cn.Trim(), out var guid)
            || guid == Guid.Empty)
        {
            throw new DomainValidationException(
                $"Client certificate CN '{cn}' is not a valid device GUID.");
        }

        return new DeviceIdentity
        {
            DeviceId = guid.ToString("D"),
            Source = "client-cert",
        };
    }

    /// <summary>
    /// Extracts the CN from a certificate subject DN. Returns the whole string
    /// if no <c>CN=</c> prefix is found, making the resolver tolerant of raw
    /// identity values during prototyping.
    /// </summary>
    private static string? ExtractCommonName(string subject)
    {
        foreach (var part in subject.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[3..];
            }
        }

        return subject;
    }
}
