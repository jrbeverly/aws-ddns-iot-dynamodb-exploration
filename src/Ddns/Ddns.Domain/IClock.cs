namespace Ddns.Domain;

/// <summary>
/// Abstracts "now" so registration timestamps are deterministic under test.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production clock: wall-clock UTC.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
