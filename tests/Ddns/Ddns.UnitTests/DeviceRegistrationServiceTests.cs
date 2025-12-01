using Ddns.Domain;
using Ddns.Infrastructure;
using Xunit;

namespace Ddns.UnitTests;

public sealed class DeviceRegistrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly string DeviceA = "11111111-1111-1111-1111-111111111111";
    private static readonly string DeviceB = "22222222-2222-2222-2222-222222222222";

    private static DeviceRegistrationService NewService(out InMemoryDeviceRegistrationRepository repo)
    {
        repo = new InMemoryDeviceRegistrationRepository();
        return new DeviceRegistrationService(repo, new RequestDeviceIdentityResolver(), new FixedClock(Now));
    }

    private static DeviceRegistrationService NewServiceWithCertResolver(
        out InMemoryDeviceRegistrationRepository repo)
    {
        repo = new InMemoryDeviceRegistrationRepository();
        return new DeviceRegistrationService(
            repo, new ClientCertificateDeviceIdentityResolver(), new FixedClock(Now));
    }

    [Fact]
    public async Task Register_persists_normalized_ip_and_guid_with_clock_and_source()
    {
        var service = NewService(out _);

        var reg = await service.RegisterAsync(
            new DeviceIdentityContext { RouteDeviceId = DeviceA }, "  192.168.000.001  ");

        Assert.Equal(DeviceA, reg.DeviceId);
        Assert.Equal("192.168.0.1", reg.IpAddress);
        Assert.Equal(Now, reg.LastSeen);
        Assert.Equal("request", reg.DeviceIdentitySource);
    }

    [Fact]
    public async Task Register_accepts_ipv6_and_canonicalizes()
    {
        var service = NewService(out _);

        var reg = await service.RegisterAsync(
            new DeviceIdentityContext { RouteDeviceId = DeviceA }, "2001:0DB8:0000:0000:0000:0000:0000:0001");

        Assert.Equal("2001:db8::1", reg.IpAddress);
    }

    [Fact]
    public async Task Register_normalizes_guid_to_canonical_form()
    {
        var service = NewService(out _);

        // Guid.TryParse accepts uppercase, no-hyphens, and brace formats
        var reg = await service.RegisterAsync(
            new DeviceIdentityContext { RouteDeviceId = "{11111111-1111-1111-1111-111111111111}" },
            "10.0.0.1");

        Assert.Equal(DeviceA, reg.DeviceId);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("999.999.999.999")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Register_rejects_invalid_ip(string? ip)
    {
        var service = NewService(out _);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.RegisterAsync(new DeviceIdentityContext { RouteDeviceId = DeviceA }, ip));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("edge-01")]
    [InlineData("00000000-0000-0000-0000-000000000000")] // Guid.Empty
    public async Task Register_rejects_non_guid_device_id(string deviceId)
    {
        var service = NewService(out _);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.RegisterAsync(new DeviceIdentityContext { RouteDeviceId = deviceId }, "10.0.0.1"));
    }

    [Fact]
    public async Task Register_is_last_write_wins_per_device()
    {
        var service = NewService(out _);
        var ctx = new DeviceIdentityContext { RouteDeviceId = DeviceA };

        await service.RegisterAsync(ctx, "10.0.0.1");
        await service.RegisterAsync(ctx, "10.0.0.2");

        var current = await service.GetAsync(DeviceA);
        Assert.Equal("10.0.0.2", current!.IpAddress);
    }

    [Fact]
    public async Task ListFleet_returns_all_devices_ordered()
    {
        var service = NewService(out _);
        await service.RegisterAsync(new DeviceIdentityContext { RouteDeviceId = DeviceB }, "10.0.0.2");
        await service.RegisterAsync(new DeviceIdentityContext { RouteDeviceId = DeviceA }, "10.0.0.1");

        var fleet = await service.ListFleetAsync();

        Assert.Collection(fleet,
            d => Assert.Equal(DeviceA, d.DeviceId),
            d => Assert.Equal(DeviceB, d.DeviceId));
    }

    [Fact]
    public async Task Get_unknown_guid_device_returns_null()
    {
        var service = NewService(out _);
        Assert.Null(await service.GetAsync("33333333-3333-3333-3333-333333333333"));
    }

    [Fact]
    public async Task Get_rejects_non_guid_device_id()
    {
        var service = NewService(out _);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.GetAsync("not-a-guid"));
    }

    // --- mTLS / ClientCertificateDeviceIdentityResolver tests ---

    [Fact]
    public async Task CertResolver_derives_identity_from_cert_subject_with_cn_prefix()
    {
        var service = NewServiceWithCertResolver(out _);

        var reg = await service.RegisterAsync(
            new DeviceIdentityContext
            {
                RouteDeviceId = "11111111-1111-1111-1111-111111111111",
                ClientCertificateSubject = "CN=22222222-2222-2222-2222-222222222222",
            },
            "10.0.0.1");

        Assert.Equal("22222222-2222-2222-2222-222222222222", reg.DeviceId);
        Assert.Equal("client-cert", reg.DeviceIdentitySource);
    }

    [Fact]
    public async Task CertResolver_derives_identity_from_raw_subject_without_cn_prefix()
    {
        var service = NewServiceWithCertResolver(out _);

        var reg = await service.RegisterAsync(
            new DeviceIdentityContext
            {
                RouteDeviceId = "11111111-1111-1111-1111-111111111111",
                ClientCertificateSubject = "22222222-2222-2222-2222-222222222222",
            },
            "10.0.0.1");

        Assert.Equal("22222222-2222-2222-2222-222222222222", reg.DeviceId);
        Assert.Equal("client-cert", reg.DeviceIdentitySource);
    }

    [Fact]
    public async Task CertResolver_normalizes_guid_to_canonical_form()
    {
        var service = NewServiceWithCertResolver(out _);

        var reg = await service.RegisterAsync(
            new DeviceIdentityContext
            {
                ClientCertificateSubject = "CN={11111111-1111-1111-1111-111111111111}",
            },
            "10.0.0.1");

        Assert.Equal(DeviceA, reg.DeviceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CertResolver_rejects_missing_cert_subject(string? subject)
    {
        var service = NewServiceWithCertResolver(out _);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.RegisterAsync(
                new DeviceIdentityContext { ClientCertificateSubject = subject },
                "10.0.0.1"));
    }

    [Theory]
    [InlineData("CN=not-a-guid")]
    [InlineData("CN=edge-01")]
    [InlineData("CN=00000000-0000-0000-0000-000000000000")] // Guid.Empty
    public async Task CertResolver_rejects_non_guid_cert_subject(string subject)
    {
        var service = NewServiceWithCertResolver(out _);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.RegisterAsync(
                new DeviceIdentityContext { ClientCertificateSubject = subject },
                "10.0.0.1"));
    }

    [Fact]
    public async Task CertResolver_ignores_route_value_when_cert_subject_is_present()
    {
        var service = NewServiceWithCertResolver(out _);

        // The route value and cert subject differ; the cert resolver must use
        // the cert subject, not the route value.
        var reg = await service.RegisterAsync(
            new DeviceIdentityContext
            {
                RouteDeviceId = "33333333-3333-3333-3333-333333333333",
                ClientCertificateSubject = "CN=11111111-1111-1111-1111-111111111111",
            },
            "10.0.0.1");

        Assert.Equal(DeviceA, reg.DeviceId);
    }
}
