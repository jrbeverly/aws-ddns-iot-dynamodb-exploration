using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Ddns.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ddns.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the fleet IP inventory: DynamoDB client (real or DynamoDB Local),
    /// repository, the identity resolver, clock, and the domain service.
    ///
    /// When <c>Ddns:MutualTls:Enabled</c> is <c>true</c>,
    /// <see cref="ClientCertificateDeviceIdentityResolver"/> is registered;
    /// otherwise <see cref="RequestDeviceIdentityResolver"/> is the default.
    /// This swap is the entire mutual-TLS migration — no endpoint, service, or
    /// storage change.
    /// </summary>
    public static IServiceCollection AddDdnsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new DynamoDbOptions();
        configuration.GetSection(DynamoDbOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                // DynamoDB Local: explicit endpoint + dummy creds so the SDK
                // does not probe the (absent) instance credential chain.
                return new AmazonDynamoDBClient(
                    new BasicAWSCredentials("local", "local"),
                    new AmazonDynamoDBConfig
                    {
                        ServiceURL = options.ServiceUrl,
                        AuthenticationRegion = "us-west-2",
                    });
            }

            // AWS: region/credentials resolve from the Lambda execution role.
            return new AmazonDynamoDBClient();
        });

        services.AddSingleton<IDeviceRegistrationRepository, DynamoDbDeviceRegistrationRepository>();

        // Identity resolver: swap for ClientCertificateDeviceIdentityResolver
        // when mutual TLS is enabled (Ddns:MutualTls:Enabled = true).
        if (configuration.GetValue<bool>("Ddns:MutualTls:Enabled"))
        {
            services.AddSingleton<IDeviceIdentityResolver, ClientCertificateDeviceIdentityResolver>();
        }
        else
        {
            services.AddSingleton<IDeviceIdentityResolver, RequestDeviceIdentityResolver>();
        }

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<DeviceRegistrationService>();

        return services;
    }
}
