namespace UrlShortener.Api.Services;

/// <summary>
/// Generates cryptographically secure Base62 alphanumeric short codes (specs/tasks.md T009 & T010).
/// </summary>
public interface IBase62CodeGenerator
{
    /// <summary>
    /// Generates a random Base62 string using RandomNumberGenerator.
    /// </summary>
    /// <param name="length">Length of the generated code (default 7).</param>
    /// <returns>A cryptographically random Base62 string.</returns>
    string Generate(int length = 7);
}
