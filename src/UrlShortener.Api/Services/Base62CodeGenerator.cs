using System.Security.Cryptography;

namespace UrlShortener.Api.Services;

/// <summary>
/// Cryptographically secure Base62 random short code generator (specs/tasks.md T010).
/// Uses RandomNumberGenerator.GetString to guarantee uniform distribution without modulo bias.
/// </summary>
public sealed class Base62CodeGenerator : IBase62CodeGenerator
{
    private const string Base62Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// Generates a cryptographically random Base62 alphanumeric string of the specified length.
    /// </summary>
    /// <param name="length">Length of the short code (default 7).</param>
    /// <returns>7-character Base62 string.</returns>
    public string Generate(int length = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return RandomNumberGenerator.GetString(Base62Characters, length);
    }
}
