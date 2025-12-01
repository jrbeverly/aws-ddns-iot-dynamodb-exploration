namespace Ddns.Api.Contracts;

/// <summary>Body of <c>PUT /api/devices/{deviceId}/registration</c>.</summary>
public sealed record RegisterDeviceRequest(string Ip);

/// <summary>Public view of a device's place in the fleet IP inventory.</summary>
public sealed record DeviceRegistrationResponse(
    string DeviceId,
    string Ip,
    DateTimeOffset LastSeen,
    string IdentitySource);

/// <summary>Reverse-lookup payload: the whole fleet.</summary>
public sealed record FleetResponse(int Count, IReadOnlyList<DeviceRegistrationResponse> Devices);
