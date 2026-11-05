using System;
using System.Security.Cryptography;

namespace UrlShortener.Api.Services;

/// <summary>
/// Cryptographically secure, human-friendly Base58 short code generator (specs/tasks.md T010).
/// Eliminates visually ambiguous characters (0, O, I, l) and utilizes CSPRNG to prevent code guessing.
/// </summary>
public sealed class Base58CodeGenerator : IShortCodeGenerator
{
    // Using a string constant is highly optimized for RandomNumberGenerator.GetString
    private const string Base58Characters = 
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>
    /// Generates a secure, human-friendly Base58 short code of the specified length.
    /// </summary>
    /// <param name="length">Length of the short code (default 7).</param>
    /// <returns>7-character Base58 string.</returns>
    public string Generate(int length = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        
        // Built-in .NET 8+ method guarantees cryptographic randomness and zero modulo bias
        return RandomNumberGenerator.GetString(Base58Characters, length);
    }
}
