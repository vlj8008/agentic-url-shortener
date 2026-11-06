using System;
using Xunit;
using UrlShortener.Api.Services;

namespace UrlShortener.UnitTests;

/// <summary>
/// Unit tests for User Story 3 (specs/tasks.md T022): Custom aliases validation,
/// reserved-word blocklists, case-insensitivity, and link expiration timestamps.
/// </summary>
public class UserStory3Tests
{
    private readonly UrlSecurityValidator _validator = new();

    // =========================================================================
    // 1. Custom Alias Format & Length Validation (T022)
    // =========================================================================

    [Theory]
    [InlineData("my-link_123")]                   // Standard valid characters (alphanumeric, hyphens, underscores)
    [InlineData("abcd")]                          // Exact minimum boundary (4 characters)
    [InlineData("12345678901234567890123456789012")] // Exact maximum boundary (32 characters)
    public void ValidateCustomAlias_ValidAliases_ReturnsSuccess(string alias)
    {
        var result = _validator.ValidateCustomAlias(alias);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("abc")]                               // Too short (3 characters)
    [InlineData("123456789012345678901234567890123")] // Too long (33 characters)
    [InlineData("bad alias")]                         // Contains whitespace
    [InlineData("bad/alias")]                         // Contains path traversal slash
    [InlineData("bad@alias!")]                        // Special characters
    [InlineData("link🔥")]                            // Unicode / Emoji
    [InlineData("café")]                              // Accented characters
    [InlineData("")]                                  // Empty string
    public void ValidateCustomAlias_InvalidRegexFormat_ReturnsFailure(string alias)
    {
        var result = _validator.ValidateCustomAlias(alias);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ValidateCustomAlias_NullOrWhitespace_ReturnsFailure(string? alias)
    {
        var result = _validator.ValidateCustomAlias(alias!);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    // =========================================================================
    // 2. Reserved Words & Case-Insensitivity (T022)
    // =========================================================================

    [Theory]
    [InlineData("api")]
    [InlineData("API")]     // Route collision risk: ASP.NET routing is case-insensitive
    [InlineData("health")]
    [InlineData("Admin")]
    [InlineData("swagger")]
    [InlineData("METRICS")]
    public void ValidateCustomAlias_ReservedWords_ReturnsFailure(string reservedWord)
    {
        var result = _validator.ValidateCustomAlias(reservedWord);

        Assert.False(result.IsValid);
        Assert.Contains("reserved", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("api-v1")]
    [InlineData("admin-docs")]
    [InlineData("swagger-test")]
    [InlineData("metrics_dashboard")]
    public void ValidateCustomAlias_ReservedWordAsSubstring_ReturnsSuccess(string alias)
    {
        // Validates that exact matching is used rather than broad .Contains() over-blocking
        var result = _validator.ValidateCustomAlias(alias);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    // =========================================================================
    // 3. Expiration Timestamp Validation (UTC) (T022)
    // =========================================================================

    [Fact]
    public void ValidateExpiration_NullExpiration_ReturnsSuccess()
    {
        // Null expiresAt means permanent link (valid)
        var result = _validator.ValidateExpiration((DateTimeOffset?)null);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateExpiration_FutureUtcDate_ReturnsSuccess()
    {
        var futureDate = DateTimeOffset.UtcNow.AddDays(7);

        var result = _validator.ValidateExpiration(futureDate);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateExpiration_PastDate_ReturnsFailure()
    {
        var pastDate = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = _validator.ValidateExpiration(pastDate);

        Assert.False(result.IsValid);
        Assert.Contains("future", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
