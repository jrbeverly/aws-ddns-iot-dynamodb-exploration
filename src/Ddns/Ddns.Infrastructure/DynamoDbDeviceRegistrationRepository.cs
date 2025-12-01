using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Ddns.Domain;

namespace Ddns.Infrastructure;

/// <summary>
/// Single-table store for the fleet IP inventory (dynamodb.md: access-pattern
/// driven, type-prefixed keys, on-demand billing).
///
/// <code>
/// PK = DEVICE#{deviceId}   SK = REGISTRATION          → get one device
/// GSI1PK = FLEET           GSI1SK = DEVICE#{deviceId}  → list whole fleet (one Query, no Scan)
/// </code>
/// </summary>
public sealed class DynamoDbDeviceRegistrationRepository(
    IAmazonDynamoDB client,
    DynamoDbOptions options)
    : IDeviceRegistrationRepository
{
    private const string Sk = "REGISTRATION";
    private const string FleetPartition = "FLEET";
    private const string Gsi1Name = "GSI1";
    private const string EntityType = "DeviceRegistration";

    private static string DevicePk(string deviceId) => $"DEVICE#{deviceId}";
    private static string FleetSortKey(string deviceId) => $"DEVICE#{deviceId}";

    public async Task UpsertAsync(DeviceRegistration registration, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new(DevicePk(registration.DeviceId)),
            ["SK"] = new(Sk),
            ["GSI1PK"] = new(FleetPartition),
            ["GSI1SK"] = new(FleetSortKey(registration.DeviceId)),
            ["EntityType"] = new(EntityType),
            ["DeviceId"] = new(registration.DeviceId),
            ["IpAddress"] = new(registration.IpAddress),
            ["LastSeen"] = new(registration.LastSeen.ToString("O")),
            ["DeviceIdentitySource"] = new(registration.DeviceIdentitySource),
        };

        await client.PutItemAsync(
            new PutItemRequest { TableName = options.TableName, Item = item },
            ct);
    }

    public async Task<DeviceRegistration?> GetAsync(string deviceId, CancellationToken ct = default)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new(DevicePk(deviceId)),
                    ["SK"] = new(Sk),
                },
            },
            ct);

        return response is { IsItemSet: true } ? MapItem(response.Item) : null;
    }

    public async Task<IReadOnlyList<DeviceRegistration>> ListFleetAsync(CancellationToken ct = default)
    {
        var results = new List<DeviceRegistration>();
        Dictionary<string, AttributeValue>? exclusiveStartKey = null;

        do
        {
            var response = await client.QueryAsync(
                new QueryRequest
                {
                    TableName = options.TableName,
                    IndexName = Gsi1Name,
                    KeyConditionExpression = "GSI1PK = :fleet",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":fleet"] = new(FleetPartition),
                    },
                    ExclusiveStartKey = exclusiveStartKey,
                },
                ct);

            results.AddRange(response.Items.Select(MapItem));
            exclusiveStartKey = response.LastEvaluatedKey is { Count: > 0 }
                ? response.LastEvaluatedKey
                : null;
        }
        while (exclusiveStartKey is not null);

        // GSI1SK is DEVICE#{id}; Query already returns it sorted, but be explicit.
        return results.OrderBy(r => r.DeviceId, StringComparer.Ordinal).ToList();
    }

    private static DeviceRegistration MapItem(IReadOnlyDictionary<string, AttributeValue> item) => new()
    {
        DeviceId = item["DeviceId"].S,
        IpAddress = item["IpAddress"].S,
        LastSeen = DateTimeOffset.Parse(
            item["LastSeen"].S,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        DeviceIdentitySource = item["DeviceIdentitySource"].S,
    };
}
