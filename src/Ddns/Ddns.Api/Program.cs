using Amazon.Lambda.AspNetCoreServer.Hosting;
using Ddns.Api.Endpoints;
using Ddns.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDdnsInfrastructure(builder.Configuration);

// Runs as a normal Kestrel app locally (and under WebApplicationFactory in the
// integration tests) and as an API Gateway HTTP API Lambda in AWS — same code.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

var api = app.MapGroup("/api");
api.MapDeviceEndpoints();
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so Ddns.IntegrationTests can host the real app via WebApplicationFactory.
public partial class Program;
