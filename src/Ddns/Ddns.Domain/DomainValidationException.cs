namespace Ddns.Domain;

/// <summary>
/// Thrown when an inbound registration fails domain validation. The API layer
/// translates this into a 400 with the message — fail fast, clear errors
/// (core-principles.md #10).
/// </summary>
public sealed class DomainValidationException(string message) : Exception(message);
