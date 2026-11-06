using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Agents;

/// <summary>
/// Specialized agent for requirement clarification, ambiguity detection, and formal contract synthesis (specs/tasks.md T026).
/// Enforces Principle 3: Ambiguity Detection &amp; Intent Normalization (simulating /speckit.clarify).
/// </summary>
public sealed class RequirementNormalizerAgent : IAgentStage<RawRequirementInput, RequirementNormalizationResult>
{
    public string Name => "RequirementNormalizerAgent";

    private static readonly string[] AmbiguousKeywords =
    [
        "fast", "scalable", "custom short link without rules", "unspecified rate limit",
        "missing expiration", "simple alias", "unbounded", "flexible redirect", "asap"
    ];

    public Task<RequirementNormalizationResult> ExecuteAsync(
        RawRequirementInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Prompt);

        var prompt = input.Prompt;
        var ambiguities = new List<string>();
        var assumptions = new List<string>();
        var requirements = new List<NormalizedRequirement>();
        var userStories = new List<string>();

        // 1. Detect ambiguous phrasing or underspecified requirements
        foreach (var keyword in AmbiguousKeywords)
        {
            if (prompt.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                ambiguities.Add($"Detected vague/underspecified concept '{keyword}'. Requires explicit boundaries.");
            }
        }

        // 2. Synthesize baseline functional requirements (FR-001 - FR-016) based on detected domain capabilities
        var hasShortening = prompt.Contains("shorten", StringComparison.OrdinalIgnoreCase) ||
                            prompt.Contains("create", StringComparison.OrdinalIgnoreCase) ||
                            prompt.Contains("url", StringComparison.OrdinalIgnoreCase);

        var hasCustomAlias = prompt.Contains("alias", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("slug", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("custom", StringComparison.OrdinalIgnoreCase);

        var hasExpiration = prompt.Contains("expire", StringComparison.OrdinalIgnoreCase) ||
                            prompt.Contains("expiration", StringComparison.OrdinalIgnoreCase) ||
                            prompt.Contains("ttl", StringComparison.OrdinalIgnoreCase);

        var hasRateLimiting = prompt.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
                              prompt.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                              prompt.Contains("throttle", StringComparison.OrdinalIgnoreCase);

        var hasAnalytics = prompt.Contains("analytic", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("click", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("metric", StringComparison.OrdinalIgnoreCase);

        if (hasShortening)
        {
            userStories.Add("US1 (Greenfield MVP: Core URL Shortening, Anti-SSRF & Redirection)");
            requirements.Add(new NormalizedRequirement(
                "FR-001",
                "Cryptographic Base62 Short URL Creation",
                "POST /api/v1/urls creates a 7-character Base62 token using CSPRNG returning 201 Created.",
                "Core Shortening"));

            requirements.Add(new NormalizedRequirement(
                "FR-002",
                "Anti-SSRF Perimeter Validation",
                "Rejects private/loopback/cloud metadata IP ranges (RFC 1918/3927/4193/4291) returning 400 Bad Request.",
                "Security"));

            requirements.Add(new NormalizedRequirement(
                "FR-003",
                "Sub-50ms Cached Redirection",
                "GET /{shortCode} resolves target URL and issues HTTP 302 Found with Cache-Control: no-store.",
                "Redirection"));

            assumptions.Add("Assumed cryptographic Base62 token length is exactly 7 characters (3.52 trillion combinations).");
            assumptions.Add("Assumed HTTP 302 Found with Cache-Control: no-store, no-cache to preserve server analytics and instant revocation.");
        }

        if (hasCustomAlias)
        {
            userStories.Add("US3 (Ambiguous Requirements: Custom Aliases)");
            requirements.Add(new NormalizedRequirement(
                "FR-004",
                "Custom Alias Regex & Reserved-Word Blocklist",
                "Enforces ^[a-zA-Z0-9_-]{4,32}$ and blocks reserved words (api, health, admin, swagger, metrics) returning 400 Bad Request.",
                "Validation"));

            requirements.Add(new NormalizedRequirement(
                "FR-005",
                "Custom Alias Conflict Prevention",
                "Duplicate custom alias returns HTTP 409 Conflict with RFC 7807 ProblemDetails.",
                "Conflict Handling"));

            assumptions.Add("Assumed custom alias regex format ^[a-zA-Z0-9_-]{4,32}$ and reserved words blocklist.");
            assumptions.Add("Assumed duplicate alias requests return HTTP 409 Conflict with detail 'Custom alias is already in use.'");
        }

        if (hasExpiration)
        {
            if (!userStories.Contains("US3 (Ambiguous Requirements: Custom Aliases)"))
            {
                userStories.Add("US3 (Ambiguous Requirements: Expiration & Rate Limiting)");
            }

            requirements.Add(new NormalizedRequirement(
                "FR-006",
                "UTC Expiration & 410 Gone",
                "Optional expiresAt in UTC; past timestamp returns 400 Bad Request on creation; expired link returns 410 Gone.",
                "Lifecycle"));

            assumptions.Add("Assumed expired link redirection returns HTTP 410 Gone with 'This shortened URL has expired.'");
        }

        if (hasRateLimiting)
        {
            requirements.Add(new NormalizedRequirement(
                "FR-007",
                "Fixed Window IP Rate Limiting",
                "Enforces 10 req/min for creation and 100 req/min for redirect per client IP, returning 429 Too Many Requests with Retry-After: 60.",
                "Resilience"));

            assumptions.Add("Assumed fixed-window rate limit policy: 10 creation/min and 100 redirect/min per client IP.");
        }

        if (hasAnalytics)
        {
            userStories.Add("US2 (Brownfield: Privacy-Safe Analytics & Revocation)");
            requirements.Add(new NormalizedRequirement(
                "FR-008",
                "Privacy-Safe Click Analytics",
                "Asynchronously records click events with SHA-256 hashed IP (daily salt) via Channel<ClickEvent>.",
                "Analytics"));

            assumptions.Add("Assumed zero raw IP storage in analytics tables; IP is SHA-256 hashed with rotating daily salt.");
        }

        var requiresClarification = ambiguities.Count > 0;
        var summary = $"Normalized {requirements.Count} functional requirements across {userStories.Count} user stories with {assumptions.Count} architectural assumptions.";

        return Task.FromResult(new RequirementNormalizationResult(
            summary,
            ambiguities,
            assumptions,
            requirements,
            userStories,
            requiresClarification));
    }
}

public sealed record RawRequirementInput(string Prompt, string? ProjectName = null);

public sealed record RequirementNormalizationResult(
    string Summary,
    IReadOnlyList<string> Ambiguities,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<NormalizedRequirement> Requirements,
    IReadOnlyList<string> TargetUserStories,
    bool RequiresClarification);

public sealed record NormalizedRequirement(
    string Id,
    string Title,
    string Description,
    string Category);
