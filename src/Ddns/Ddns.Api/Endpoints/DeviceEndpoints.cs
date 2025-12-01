using Amazon.Lambda.APIGatewayEvents;
using Ddns.Api.Contracts;
using Ddns.Domain;

namespace Ddns.Api.Endpoints;

/// <summary>
/// The fleet IP inventory surface. The <see cref="DeviceIdentityContext"/>
/// built here carries both the route value and any client certificate subject,
/// so the registered <see cref="IDeviceIdentityResolver"/> can decide identity
/// without the endpoint knowing which resolver is active.
/// </summary>
public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/devices");

        // "Hey, this is my IP" — register/refresh a device's location.
        devices.MapPut("/{deviceId}/registration", async (
            string deviceId,
            RegisterDeviceRequest request,
            HttpContext httpContext,
            DeviceRegistrationService service,
            CancellationToken ct) =>
        {
            try
            {
                var identityContext = new DeviceIdentityContext
                {
                    RouteDeviceId = deviceId,
                    ClientCertificateSubject = ExtractClientCertSubject(httpContext),
                };
                var registration = await service.RegisterAsync(identityContext, request.Ip, ct);
                return Results.Ok(ToResponse(registration));
            }
            catch (DomainValidationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        // Reverse lookup: one device.
        devices.MapGet("/{deviceId}", async (
            string deviceId,
            DeviceRegistrationService service,
            CancellationToken ct) =>
        {
            try
            {
                var registration = await service.GetAsync(deviceId, ct);
                return registration is null
                    ? Results.NotFound()
                    : Results.Ok(ToResponse(registration));
            }
            catch (DomainValidationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        // Reverse lookup: the whole fleet.
        devices.MapGet("/", async (DeviceRegistrationService service, CancellationToken ct) =>
        {
            var fleet = await service.ListFleetAsync(ct);
            return Results.Ok(new FleetResponse(
                fleet.Count,
                fleet.Select(ToResponse).ToList()));
        });

        return app;
    }

    /// <summary>
    /// Extracts the client certificate subject from the request so the identity
    /// resolver can use it when mTLS is active. Returns <c>null</c> when no
    /// client certificate is present (open/v1 path).
    /// </summary>
    private static string? ExtractClientCertSubject(HttpContext httpContext)
    {
        // API Gateway HTTP API mTLS: the Lambda hosting library stores the
        // original event in HttpContext.Items. The validated client certificate
        // subject is available through the Lambda request context.
        if (httpContext.Items.TryGetValue("LambdaRequestObject", out var obj)
            && obj is APIGatewayHttpApiV2ProxyRequest lambdaRequest)
        {
            var subject = lambdaRequest.RequestContext?.Authentication?.ClientCert?.SubjectDN;
            if (!string.IsNullOrWhiteSpace(subject))
                return subject;
        }

        // Standard ASP.NET Core client certificate (local Kestrel or REST API
        // Gateway mTLS where the hosting library populates the connection).
        return httpContext.Connection.ClientCertificate?.Subject;
    }

    private static DeviceRegistrationResponse ToResponse(DeviceRegistration r)
        => new(r.DeviceId, r.IpAddress, r.LastSeen, r.DeviceIdentitySource);
}
