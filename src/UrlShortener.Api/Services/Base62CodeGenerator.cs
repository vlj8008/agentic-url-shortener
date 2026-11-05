namespace UrlShortener.Api.Services;

/// <summary>
/// High-throughput short code generator utilizing Base58 character set and System.Random.Shared (specs/tasks.md T010).
/// Eliminates visually ambiguous characters (0, O, I, l) to prevent user transcription errors,
/// and maximizes CPU generation throughput via zero-allocation string.Create and Random.Shared.GetItems.
/// </summary>
public sealed class Base62CodeGenerator : IBase62CodeGenerator
{
    /// <summary>
    /// Base58 character set (excludes 0, O, I, l to prevent visual confusion).
    /// </summary>
    private static readonly char[] Base58Characters = 
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".ToCharArray();

    /// <summary>
    /// Generates a high-speed, human-friendly Base58 short code of the specified length.
    /// </summary>
    /// <param name="length">Length of the short code (default 7).</param>
    /// <returns>7-character Base58 string.</returns>
    public string Generate(int length = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        return string.Create(length, Base58Characters, static (span, chars) =>
        {
            Random.Shared.GetItems(chars.AsSpan(), span);
        });
    }
}
