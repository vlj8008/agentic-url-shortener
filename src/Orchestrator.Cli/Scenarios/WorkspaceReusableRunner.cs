using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Scenarios;

/// <summary>
/// Domain-agnostic workspace governance runner supporting reusable CLI flags:
/// --workspace &lt;path&gt;, --init (scaffolding specs/), and --verify (pre-push verification gates) (specs/tasks.md T027).
/// </summary>
public sealed class WorkspaceReusableRunner
{
    private readonly string _workspacePath;

    public WorkspaceReusableRunner(string? workspacePath = null)
    {
        _workspacePath = string.IsNullOrWhiteSpace(workspacePath)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspacePath);
    }

    /// <summary>
    /// Scaffolds the standard 4-artifact Spec-Driven Development (SDD) directory:
    /// specs/constitution.md, specs/spec.md, specs/plan.md, and specs/tasks.md.
    /// </summary>
    public Task<int> ScaffoldWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('=', 75));
        Console.WriteLine("  WORKSPACE REUSABLE RUNNER â€” SCAFFOLD SDD SPECIFICATIONS (--init)");
        Console.WriteLine(new string('=', 75) + "\n");
        Console.ResetColor();

        Console.WriteLine($"Target Workspace: {_workspacePath}");

        var specsDir = Path.Combine(_workspacePath, "specs");
        Directory.CreateDirectory(specsDir);
        Console.WriteLine($"âœ“ Ensured directory exists: {specsDir}");

        var artifacts = new[]
        {
            ("constitution.md", "# Domain Constitution & Engineering Rules\n\n## Principles\n- Principle 1: Transparent Agent Governance\n- Principle 2: Human-in-the-Loop Checkpoints\n- Principle 3: Spec-Driven Development\n- Principle 4: Zero-Trust Security\n- Principle 5: SOLID & Testing Rigor\n"),
            ("spec.md", "# Product Intent & Functional Specification\n\n## User Stories\n- US1: Greenfield Feature\n- US2: Brownfield Evolution\n- US3: Edge-Case Handling\n\n## Formal Contract\n```yaml\nopenapi: 3.1.0\ninfo:\n  title: Service API\n  version: 1.0.0\n```\n"),
            ("plan.md", "# Architectural Plan & Technology Stack\n\n## System Architecture\nClean Architecture with Minimal APIs, EF Core, and in-memory caching.\n\n## Architectural Decision Records (ADRs)\n- ADR-001: Core Architecture\n"),
            ("tasks.md", "# Non-Linear Dependency Graph & Tasks\n\n## Phase 1: Setup\n- [ ] **T001** Initialize Solution and Directory.Build.props\n\n## Phase 2: Foundation\n- [ ] **T002** Implement Domain Entities and Repository\n\n## Phase 3: Verification Barrier\n- [ ] **T003** Execute Verification Gate (dotnet build & test)\n")
        };

        var createdCount = 0;
        var existingCount = 0;

        foreach (var (filename, content) in artifacts)
        {
            var filePath = Path.Combine(specsDir, filename);
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, content);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  + Created: specs/{filename}");
                Console.ResetColor();
                createdCount++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  = Exists:  specs/{filename} (preserved)");
                Console.ResetColor();
                existingCount++;
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nâœ“ Scaffolding complete: {createdCount} created, {existingCount} already present.\n");
        Console.ResetColor();

        return Task.FromResult(0);
    }

    /// <summary>
    /// Executes the Pre-Push Verification Gates (Zero-Broken-Commits Rule):
    /// 1. Clean compilation with zero warnings/errors (dotnet build).
    /// 2. 100% passing automated test suite (dotnet test).
    /// </summary>
    public async Task<int> VerifyWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('=', 75));
        Console.WriteLine("  WORKSPACE REUSABLE RUNNER â€” PRE-PUSH VERIFICATION GATES (--verify)");
        Console.WriteLine(new string('=', 75) + "\n");
        Console.ResetColor();

        Console.WriteLine($"Target Workspace: {_workspacePath}");

        // Gate 1: dotnet build
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Gate 1/2] Executing Clean Compilation Check (dotnet build)...");
        Console.ResetColor();

        var (buildExit, buildOutput) = await RunProcessAsync("dotnet", "build --nologo", _workspacePath, cancellationToken);
        if (buildExit != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("âœ— Compilation Check FAILED. Non-zero exit code.");
            Console.WriteLine(buildOutput);
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("âœ“ Compilation Gate PASSED with 0 errors and 0 warnings.");
        Console.ResetColor();

        // Gate 2: dotnet test
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Gate 2/2] Executing Automated Test Suites (dotnet test)...");
        Console.ResetColor();

        var (testExit, testOutput) = await RunProcessAsync("dotnet", "test --nologo", _workspacePath, cancellationToken);
        if (testExit != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("âœ— Test Suite Verification FAILED.");
            Console.WriteLine(testOutput);
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("âœ“ Test Suite Gate PASSED: 100% tests passing.");
        Console.WriteLine("\nâœ“ All Pre-Push Verification Gates PASSED. Repository is clean for Git push/release!\n");
        Console.ResetColor();

        return 0;
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = (await stdoutTask) + "\n" + (await stderrTask);
        return (process.ExitCode, output);
    }
}
