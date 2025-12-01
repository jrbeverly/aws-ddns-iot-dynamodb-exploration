using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Ddns.IntegrationTests;

/// <summary>
/// Full register → reverse-lookup loop against the real Minimal API and a real
/// DynamoDB (Local). This is the hermetic proof the service works; the AWS gate
/// runs the same shape against Terraform-provisioned infra.
/// </summary>
public sealed class DeviceApiIntegrationTests(DdnsAppFixture fixture) : IClassFixture<DdnsAppFixture>
{
    private sealed record RegistrationDto(string DeviceId, string Ip, DateTimeOffset LastSeen, string IdentitySource);
    private sealed record FleetDto(int Count, List<RegistrationDto> Devices);

    [Fact]
    public async Task Register_then_get_returns_the_registration()
    {
        var client = fixture.CreateClient();
        var deviceId = Guid.NewGuid().ToString("D");

        var put = await client.PutAsJsonAsync($"/api/devices/{deviceId}/registration", new { ip = "203.0.113.7" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var created = await put.Content.ReadFromJsonAsync<RegistrationDto>();
        Assert.Equal(deviceId, created!.DeviceId);
        Assert.Equal("203.0.113.7", created.Ip);
        Assert.Equal("request", created.IdentitySource);

        var get = await client.GetFromJsonAsync<RegistrationDto>($"/api/devices/{deviceId}");
        Assert.Equal("203.0.113.7", get!.Ip);
    }

    [Fact]
    public async Task Re_register_overwrites_ip()
    {
        var client = fixture.CreateClient();
        var deviceId = Guid.NewGuid().ToString("D");

        await client.PutAsJsonAsync($"/api/devices/{deviceId}/registration", new { ip = "198.51.100.1" });
        await client.PutAsJsonAsync($"/api/devices/{deviceId}/registration", new { ip = "198.51.100.2" });

        var get = await client.GetFromJsonAsync<RegistrationDto>($"/api/devices/{deviceId}");
        Assert.Equal("198.51.100.2", get!.Ip);
    }

    [Fact]
    public async Task Fleet_listing_includes_registered_device()
    {
        var client = fixture.CreateClient();
        var deviceId = Guid.NewGuid().ToString("D");
        await client.PutAsJsonAsync($"/api/devices/{deviceId}/registration", new { ip = "192.0.2.55" });

        var fleet = await client.GetFromJsonAsync<FleetDto>("/api/devices");

        Assert.NotNull(fleet);
        Assert.True(fleet!.Count >= 1);
        Assert.Contains(fleet.Devices, d => d.DeviceId == deviceId && d.Ip == "192.0.2.55");
    }

    [Fact]
    public async Task Unknown_device_returns_404()
    {
        var client = fixture.CreateClient();
        var response = await client.GetAsync($"/api/devices/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_ip_returns_400()
    {
        var client = fixture.CreateClient();
        var deviceId = Guid.NewGuid().ToString("D");

        var response = await client.PutAsJsonAsync(
            $"/api/devices/{deviceId}/registration", new { ip = "not-an-ip" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_guid_device_id_put_returns_400()
    {
        var client = fixture.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/devices/not-a-guid/registration", new { ip = "10.0.0.1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_guid_device_id_get_returns_400()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/devices/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_ok()
    {
        var client = fixture.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
