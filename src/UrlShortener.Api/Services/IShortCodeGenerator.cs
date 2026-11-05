namespace UrlShortener.Api.Services;

/// <summary>
/// Domain service abstraction for generating short code identifiers (specs/tasks.md T009 & T010).
/// Decouples code generation algorithms from consuming services.
/// </summary>
public interface IShortCodeGenerator
{
    /// <summary>
    /// Generates a random short code string of the specified length.
    /// </summary>
    /// <param name="length">Length of the generated code (default 7).</param>
    /// <returns>A random short code string.</returns>
    string Generate(int length = 7);
}
