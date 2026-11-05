namespace UrlShortener.Api.Domain.Exceptions;

/// <summary>
/// Thrown when short code generation exhausts its bounded retry attempts due to hash collisions.
/// Maps to HTTP 503 Service Unavailable (specs/tasks.md T012).
/// </summary>
public sealed class ShortCodeCollisionException : Exception
{
    public ShortCodeCollisionException(string message) : base(message)
    {
    }

    public ShortCodeCollisionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when attempting to resolve a short code that has expired or been revoked.
/// Maps to HTTP 410 Gone (specs/tasks.md T012, T019).
/// </summary>
public sealed class UrlGoneException : Exception
{
    public UrlGoneException(string message) : base(message)
    {
    }

    public UrlGoneException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
