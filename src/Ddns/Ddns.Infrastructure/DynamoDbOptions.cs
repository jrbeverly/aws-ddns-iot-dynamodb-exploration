namespace Ddns.Infrastructure;

/// <summary>
/// DynamoDB binding for the service. Config section <c>Ddns:DynamoDb</c>.
/// </summary>
public sealed class DynamoDbOptions
{
    public const string SectionName = "Ddns:DynamoDb";

    /// <summary>Single-table name. Supplied by Terraform via env in AWS.</summary>
    public string TableName { get; set; } = "ddns";

    /// <summary>
    /// Override endpoint for DynamoDB Local (e.g. <c>http://localhost:8000</c>).
    /// Empty/unset in AWS so the SDK resolves the real regional endpoint.
    /// </summary>
    public string? ServiceUrl { get; set; }
}
