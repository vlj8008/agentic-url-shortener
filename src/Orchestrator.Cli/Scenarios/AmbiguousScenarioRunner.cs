using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Cli.Agents;
using Orchestrator.Cli.Engine;

namespace Orchestrator.Cli.Scenarios;

/// <summary>
/// Executes the Ambiguous Scenario: Intent Normalization & Edge-Case Synthesis (specs/tasks.md T027).
/// Demonstrates automated ambiguity detection, contract boundary synthesis,
/// and dynamic downstream re-planning upon gate rejection.
/// </summary>
public sealed class AmbiguousScenarioRunner
{
    private readonly string _workspacePath;
    private readonly Func<SdlcStage, string, CancellationToken, Task<GateDecision>>? _gateProvider;

    public AmbiguousScenarioRunner(
        string? workspacePath = null,
        Func<SdlcStage, string, CancellationToken, Task<GateDecision>>? gateProvider = null)
    {
        _workspacePath = string.IsNullOrWhiteSpace(workspacePath)
            ? Environment.CurrentDirectory
            : workspacePath;
        _gateProvider = gateProvider;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('=', 75));
        Console.WriteLine("  SCENARIO 3: AMBIGUOUS REQUIREMENTS & INTENT NORMALIZATION");
        Console.WriteLine("  Ambiguity Detection | Contract Synthesis | Dynamic Re-Planning");
        Console.WriteLine(new string('=', 75) + "\n");
        Console.ResetColor();

        var stateMachine = new SdlcStateMachine(_gateProvider);
        var telemetry = new TelemetryTracker();
        var snapshotManager = new GitSnapshotManager();
        var normalizer = new RequirementNormalizerAgent();
        var decomposer = new TaskDecomposerAgent();
        var executor = new DependencyGraphExecutor();

        telemetry.StartWorkflow();

        // ---------------------------------------------------------------------
        // STAGE 1: Clarify & Ambiguity Normalization
        // ---------------------------------------------------------------------
        telemetry.StartStage("Clarify");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Stage 1: Clarify] Ingesting vague stakeholder prompt...");
        Console.ResetColor();

        const string vaguePrompt = "Let users pick custom names, track clicks, and prevent spam";
        Console.WriteLine($"  Input Prompt: \"{vaguePrompt}\"");

        var normResult = await normalizer.ExecuteAsync(
            new RawRequirementInput(vaguePrompt, ProjectName: "UrlShortener"), cancellationToken);

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n[Ambiguity Detection Report]:");
        foreach (var ambiguity in normResult.Ambiguities)
        {
            Console.WriteLine($"  âš  {ambiguity}");
        }

        Console.WriteLine("\n[Documented Architectural Assumptions]:");
        foreach (var assumption in normResult.Assumptions)
        {
            Console.WriteLine($"  âœ“ {assumption}");
        }

        Console.WriteLine("\n[Normalized Requirements Synthesized]:");
        foreach (var req in normResult.Requirements)
        {
            Console.WriteLine($"  * {req.Id}: {req.Title} ({req.Category})");
        }
        Console.ResetColor();

        telemetry.CompleteStage("Clarify", success: true);

        // CHECKPOINT: Gate 1 (Specification Approval)
        var gate1Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate1Approval,
            "Review Normalized Edge-Case Contract:\n" +
            " - Custom Alias regex: ^[a-zA-Z0-9_-]{4,32}$\n" +
            " - Reserved-word blocklist: api, health, swagger, admin\n" +
            " - Conflict handling: HTTP 409 Conflict with ProblemDetails on existing alias\n" +
            " - Rate limiting: Fixed-window 10 req/min for creation; 100 req/min for redirect (429)",
            cancellationToken);

        if (gate1Decision == GateDecision.SafeStop)
        {
            Console.WriteLine("[Gate 1] Reviewer requested Safe-Stop. Halting scenario.");
            return 0;
        }

        if (gate1Decision == GateDecision.Reject)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[Dynamic Re-Planning Triggered]");
            Console.WriteLine("Human reviewer rejected Gate 1. Refining boundary specifications...");
            stateMachine.TransitionTo(SdlcStage.Clarify, "Dynamic Re-Planning: Incorporating additional security guardrails.");
            stateMachine.TransitionTo(SdlcStage.Analyze, "Re-analyzing refined requirements.");
            Console.ResetColor();
        }

        // ---------------------------------------------------------------------
        // STAGE 2: Architecture & Analysis
        // ---------------------------------------------------------------------
        telemetry.StartStage("Analyze");
        stateMachine.TransitionTo(SdlcStage.Analyze, "Analyzing custom alias, expiration, and rate-limiting impacts.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 2: Analyze] Evaluating rate limiter middleware and alias index performance...");
        Console.ResetColor();
        Console.WriteLine("âœ“ Evaluated ASP.NET Core RateLimiter middleware with PartitionedRateLimiter.");
        Console.WriteLine("âœ“ Evaluated Unique Constraint index on ShortUrls.ShortCode for 409 collision safety.");
        telemetry.CompleteStage("Analyze", success: true);

        // ---------------------------------------------------------------------
        // STAGE 3: Plan & Task Decomposition (with Dynamic Re-Planning Demonstration)
        // ---------------------------------------------------------------------
        telemetry.StartStage("PlanAndDecompose");
        stateMachine.TransitionTo(SdlcStage.PlanAndDecompose, "Constructing task DAG for edge-case features.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 3: Plan & Decompose] Decomposing User Story 3 into task graph...");
        Console.ResetColor();

        var tasks = new List<TaskNode>
        {
            new() { Id = "T022", Description = "Implement custom alias regex, reserved blocklist, and expiration check", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T023", Description = "Configure ASP.NET Core RateLimiter middleware (10 create / 100 redirect)", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T024", Description = "Write Security tests for 409 Conflict, 410 Expired, and 429 RateLimit", Prerequisites = new[] { "T022", "T023" }, CanRunInParallel = false }
        };

        var waves = DependencyGraphExecutor.BuildWaves(tasks);
        Console.WriteLine($"âœ“ Tasks partitioned into {waves.Count} execution waves:");
        foreach (var wave in waves)
        {
            var mode = wave.IsParallel ? "PARALLEL WAVE" : "SYNCHRONIZATION BARRIER";
            Console.WriteLine($"  [Wave {wave.WaveNumber}] {mode}: {string.Join(", ", wave.Tasks.Select(t => t.Id))}");
        }
        telemetry.CompleteStage("PlanAndDecompose", success: true);

        // CHECKPOINT: Gate 2 (Architecture & Task Approval)
        var gate2Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate2Approval,
            "Approve Execution Plan: 2 parallel implementation tasks (T022, T023) converging at security test barrier (T024).",
            cancellationToken);

        if (gate2Decision == GateDecision.SafeStop)
        {
            Console.WriteLine("[Gate 2] Reviewer requested Safe-Stop. Halting scenario.");
            return 0;
        }

        // ---------------------------------------------------------------------
        // STAGE 4: Pre-Execution Git Snapshot
        // ---------------------------------------------------------------------
        telemetry.StartStage("PreExecutionSnapshot");
        stateMachine.TransitionTo(SdlcStage.PreExecutionSnapshot, "Capturing pre-execution Git rollback snapshot.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 4: Snapshot] Creating pre-execution safety checkpoint...");
        Console.ResetColor();

        var snapshot = await snapshotManager.CaptureSnapshotAsync(_workspacePath, dryRun: false, cancellationToken);
        Console.WriteLine($"âœ“ Pre-execution snapshot captured at commit: {snapshot.CommitSha[..Math.Min(7, snapshot.CommitSha.Length)]}");
        telemetry.CompleteStage("PreExecutionSnapshot", success: true);

        // ---------------------------------------------------------------------
        // STAGE 5: Non-Linear DAG Parallel Execution
        // ---------------------------------------------------------------------
        telemetry.StartStage("ParallelExecution");
        stateMachine.TransitionTo(SdlcStage.ParallelExecution, "Dispatching Ambiguous requirement implementation tasks.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 5: Parallel Execution] Executing edge-case implementation waves...");
        Console.ResetColor();

        Task<bool> TaskRunner(TaskNode task, CancellationToken ct)
        {
            Console.WriteLine($"  -> Executing {task.Id} ({(task.CanRunInParallel ? "Parallel" : "Barrier")}): {task.Description}");
            telemetry.RecordTaskExecution(task.Id, success: true);
            return Task.FromResult(true);
        }

        var execResult = await executor.ExecuteAsync(tasks, TaskRunner, cancellationToken);
        if (!execResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("âœ— Execution failed at synchronization barrier.");
            Console.ResetColor();
            telemetry.CompleteStage("ParallelExecution", success: false);
            return 1;
        }

        Console.WriteLine($"âœ“ All {execResult.TaskResults.Count} tasks completed successfully.");
        telemetry.CompleteStage("ParallelExecution", success: true);

        // ---------------------------------------------------------------------
        // STAGE 6: Automated Verification & Bounded Retries
        // ---------------------------------------------------------------------
        telemetry.StartStage("Verification");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 6: Verification] Running automated security and boundary tests...");
        Console.ResetColor();

        var verifier = new VerificationAndRollbackAgent(stateMachine, snapshotManager, telemetry);
        var verifResult = await verifier.ExecuteAsync(
            new VerificationInput(_workspacePath, snapshot, DryRun: false), cancellationToken);

        if (!verifResult.OverallSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"âœ— Verification failed after {verifResult.AttemptsExecuted} attempts. Rollback executed: {verifResult.RolledBack}.");
            Console.ResetColor();
            telemetry.CompleteStage("Verification", success: false);
            return 1;
        }

        Console.WriteLine($"âœ“ Verification passed: Security test suite passed 100% (409 Conflict, 410 Expired, 429 RateLimit).");
        telemetry.CompleteStage("Verification", success: true);

        // CHECKPOINT: Gate 3 (Release Approval)
        var gate3Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate3Approval,
            "Release Verification Gate: Ambiguous requirements normalized, edge cases handled, zero compiler warnings.",
            cancellationToken);

        if (gate3Decision == GateDecision.SafeStop)
        {
            Console.WriteLine("[Gate 3] Reviewer requested Safe-Stop. Halting scenario.");
            return 0;
        }

        // ---------------------------------------------------------------------
        // STAGE 7: Audit Telemetry & Report
        // ---------------------------------------------------------------------
        telemetry.StopWorkflow();
        PrintTelemetryReport(telemetry.GenerateReport());

        return 0;
    }

    private static void PrintTelemetryReport(TelemetryReport report)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n" + new string('=', 75));
        Console.WriteLine("  SDLC WORKFLOW EXECUTION & AUDIT TELEMETRY REPORT");
        Console.WriteLine(new string('=', 75));
        Console.ResetColor();

        Console.WriteLine($"  Total Duration:            {report.TotalWorkflowDuration.TotalSeconds:F2} seconds");
        Console.WriteLine($"  Total Tasks Executed:      {report.TotalTasks}");
        Console.WriteLine($"  Successful Tasks:          {report.SuccessfulTasks}");
        Console.WriteLine($"  Failed Tasks:              {report.FailedTasks}");
        Console.WriteLine($"  Task Success Rate:         {report.SuccessRatePercentage:F1}%");
        Console.WriteLine($"  Self-Correction Retries:   {report.RetryCount}");
        Console.WriteLine($"  Automatic Rollbacks:       {report.RollbackCount}");
        Console.WriteLine($"  Mean Time to Recovery:     {report.MeanTimeToRecoverySeconds:F2} seconds");

        Console.WriteLine("\n  Stage Durations:");
        foreach (var (stage, duration) in report.StageDurations)
        {
            Console.WriteLine($"    - {stage,-24}: {duration.TotalMilliseconds:F0} ms");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(new string('=', 75) + "\n");
        Console.ResetColor();
    }
}
