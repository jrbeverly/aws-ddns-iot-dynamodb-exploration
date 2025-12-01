using System.Collections.Concurrent;
using Ddns.Domain;

namespace Ddns.UnitTests;

/// <summary>Fast in-memory stand-in so domain rules are tested without DynamoDB.</summary>
internal sealed class InMemoryDeviceRegistrationRepository : IDeviceRegistrationRepository
{
    private readonly ConcurrentDictionary<string, DeviceRegistration> _store = new();

    public Task UpsertAsync(DeviceRegistration registration, CancellationToken ct = default)
    {
        _store[registration.DeviceId] = registration;
        return Task.CompletedTask;
    }

    public Task<DeviceRegistration?> GetAsync(string deviceId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(deviceId));

    public Task<IReadOnlyList<DeviceRegistration>> ListFleetAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DeviceRegistration>>(
            _store.Values.OrderBy(r => r.DeviceId, StringComparer.Ordinal).ToList());
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
