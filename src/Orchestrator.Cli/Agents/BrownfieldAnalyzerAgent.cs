using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Agents;

/// <summary>
/// Specialized agent for codebase inspection, existing endpoint mapping, and blast-radius impact analysis (specs/tasks.md T026).
/// Enforces Principle 3: Brownfield Codebase Reasoning &amp; Impact Analysis.
/// </summary>
public sealed class BrownfieldAnalyzerAgent : IAgentStage<BrownfieldAnalysisInput, BrownfieldImpactAnalysis>
{
    public string Name => "BrownfieldAnalyzerAgent";

    private static readonly Regex RoutePattern = new(
        @"(?:app\.|endpoints\.)Map(?:Get|Post|Put|Delete)\s*\(\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<BrownfieldImpactAnalysis> ExecuteAsync(
        BrownfieldAnalysisInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var workspacePath = input.WorkspacePath;
        var proposedEndpoints = input.ProposedEndpoints ?? Array.Empty<string>();

        // Handle missing or uninitialized directory gracefully (Greenfield scenario)
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return Task.FromResult(new BrownfieldImpactAnalysis(
                IsGreenfield: true,
                DiscoveredFilesCount: 0,
                DiscoveredEndpoints: Array.Empty<string>(),
                ImpactedFiles: Array.Empty<string>(),
                RiskLevel: "Low",
                Recommendations: ["No existing codebase found. Proceed with standard Greenfield scaffolding."]));
        }

        // Scan .cs files excluding bin/ and obj/ build artifacts (Safeguard 3)
        var allSourceFiles = Directory.EnumerateFiles(workspacePath, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var normalized = path.Replace('\\', '/');
                return !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (allSourceFiles.Count == 0)
        {
            return Task.FromResult(new BrownfieldImpactAnalysis(
                IsGreenfield: true,
                DiscoveredFilesCount: 0,
                DiscoveredEndpoints: Array.Empty<string>(),
                ImpactedFiles: Array.Empty<string>(),
                RiskLevel: "Low",
                Recommendations: ["Empty workspace. No existing endpoints detected; safe for initial scaffolding."]));
        }

        var discoveredEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var impactedFiles = new List<string>();

        foreach (var file in allSourceFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var matches = RoutePattern.Matches(content);
                var hasRouteMatch = false;

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var route = match.Groups[1].Value;
                        discoveredEndpoints.Add(route);
                        hasRouteMatch = true;
                    }
                }

                if (hasRouteMatch ||
                    file.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("DbContext.cs", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("Validator.cs", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("Endpoints.cs", StringComparison.OrdinalIgnoreCase))
                {
                    impactedFiles.Add(Path.GetRelativePath(workspacePath, file));
                }
            }
            catch (IOException)
            {
                // File locked or inaccessible; continue inspection
            }
        }

        // Normalize proposed endpoints by stripping optional leading HTTP verbs (GET, POST, PUT, DELETE, PATCH)
        var normalizedProposed = proposedEndpoints
            .Select(ep => Regex.Replace(ep.Trim(), @"^(?:GET|POST|PUT|DELETE|PATCH)\s+", "", RegexOptions.IgnoreCase))
            .ToList();

        // Blast-Radius & Risk Level Assessment
        var overlappingEndpoints = normalizedProposed.Intersect(discoveredEndpoints, StringComparer.OrdinalIgnoreCase).ToList();
        string riskLevel;
        var recommendations = new List<string>();

        if (overlappingEndpoints.Count > 0)
        {
            riskLevel = "High";
            recommendations.Add($"Potential route collision detected for endpoints: {string.Join(", ", overlappingEndpoints)}. Ensure backward compatibility.");
        }
        else if (discoveredEndpoints.Count > 0)
        {
            riskLevel = "Medium";
            recommendations.Add("Existing API detected. New endpoints must be added without modifying existing contract response schemas.");
        }
        else
        {
            riskLevel = "Low";
            recommendations.Add("No route conflicts detected with existing codebase.");
        }

        recommendations.Add("Maintain 100% test pass rate across existing test suites before and after modifications.");

        return Task.FromResult(new BrownfieldImpactAnalysis(
            IsGreenfield: false,
            DiscoveredFilesCount: allSourceFiles.Count,
            DiscoveredEndpoints: discoveredEndpoints.OrderBy(e => e).ToList(),
            ImpactedFiles: impactedFiles,
            RiskLevel: riskLevel,
            Recommendations: recommendations));
    }
}

public sealed record BrownfieldAnalysisInput(
    string WorkspacePath,
    IReadOnlyList<string>? ProposedEndpoints = null);

public sealed record BrownfieldImpactAnalysis(
    bool IsGreenfield,
    int DiscoveredFilesCount,
    IReadOnlyList<string> DiscoveredEndpoints,
    IReadOnlyList<string> ImpactedFiles,
    string RiskLevel,
    IReadOnlyList<string> Recommendations);
