using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ddns.IntegrationTests;

/// <summary>
/// Hosts the real API (WebApplicationFactory) wired to a DynamoDB Local instance
/// stood up by <c>tests/Ddns/local-dynamodb/up.sh</c>. The harness exports the
/// endpoint + table name; this fixture creates the single table (PK/SK + GSI1)
/// so table provisioning is exercised by code, not by shell.
///
/// <see cref="IAsyncLifetime"/> is implemented explicitly because
/// <see cref="WebApplicationFactory{T}"/> already exposes a (differently-typed)
/// <c>DisposeAsync</c>.
/// </summary>
public sealed class DdnsAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _serviceUrl =
        Environment.GetEnvironmentVariable("Ddns__DynamoDb__ServiceUrl")
        ?? "http://127.0.0.1:8000";

    private readonly string _tableName =
        Environment.GetEnvironmentVariable("Ddns__DynamoDb__TableName")
        ?? "ddns-itest";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ddns:DynamoDb:ServiceUrl"] = _serviceUrl,
                ["Ddns:DynamoDb:TableName"] = _tableName,
            }));
    }

    private IAmazonDynamoDB NewAdminClient() => new AmazonDynamoDBClient(
        new BasicAWSCredentials("local", "local"),
        new AmazonDynamoDBConfig { ServiceURL = _serviceUrl, AuthenticationRegion = "us-west-2" });

    async Task IAsyncLifetime.InitializeAsync()
    {
        using var ddb = NewAdminClient();

        var existing = await ddb.ListTablesAsync();
        if (existing.TableNames.Contains(_tableName))
        {
            return;
        }

        await ddb.CreateTableAsync(new CreateTableRequest
        {
            TableName = _tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new("PK", ScalarAttributeType.S),
                new("SK", ScalarAttributeType.S),
                new("GSI1PK", ScalarAttributeType.S),
                new("GSI1SK", ScalarAttributeType.S),
            ],
            KeySchema =
            [
                new("PK", KeyType.HASH),
                new("SK", KeyType.RANGE),
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = "GSI1",
                    KeySchema =
                    [
                        new("GSI1PK", KeyType.HASH),
                        new("GSI1SK", KeyType.RANGE),
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                },
            ],
        });

        // DynamoDB Local creates synchronously, but poll to be safe.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var desc = await ddb.DescribeTableAsync(_tableName);
            if (desc.Table.TableStatus == TableStatus.ACTIVE)
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException($"Table {_tableName} did not become ACTIVE.");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            using var ddb = NewAdminClient();
            await ddb.DeleteTableAsync(_tableName);
        }
        catch
        {
            // Best effort: the container is torn down by down.sh regardless.
        }
    }
}
