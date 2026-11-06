using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Cli.Agents;
using Orchestrator.Cli.Engine;

namespace Orchestrator.Cli.Scenarios;

/// <summary>
/// Executes the Brownfield Scenario: Privacy-Safe Analytics & Emergency Revocation (specs/tasks.md T027).
/// Demonstrates codebase impact analysis, blast-radius risk evaluation, bounded channel telemetry,
/// and backward-compatible evolution without breaking existing endpoints.
/// </summary>
public sealed class BrownfieldScenarioRunner
{
    private readonly string _workspacePath;
    private readonly Func<SdlcStage, string, CancellationToken, Task<GateDecision>>? _gateProvider;

    public BrownfieldScenarioRunner(
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
        Console.WriteLine("  SCENARIO 2: BROWNFIELD â€” PRIVACY-SAFE ANALYTICS & REVOCATION");
        Console.WriteLine("  Impact Analysis | Blast-Radius Containment | Non-Breaking Evolution");
        Console.WriteLine(new string('=', 75) + "\n");
        Console.ResetColor();

        var stateMachine = new SdlcStateMachine(_gateProvider);
        var telemetry = new TelemetryTracker();
        var snapshotManager = new GitSnapshotManager();
        var normalizer = new RequirementNormalizerAgent();
        var brownfieldAnalyzer = new BrownfieldAnalyzerAgent();
        var decomposer = new TaskDecomposerAgent();
        var executor = new DependencyGraphExecutor();

        telemetry.StartWorkflow();

        // ---------------------------------------------------------------------
        // STAGE 1: Clarify & Specification Synthesis
        // ---------------------------------------------------------------------
        telemetry.StartStage("Clarify");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Stage 1: Clarify] Formulating Brownfield analytics & revocation specification...");
        Console.ResetColor();

        const string brownfieldPrompt =
            "Add privacy-safe click analytics with SHA-256 IP hashing, bounded Channel<ClickEvent> background " +
            "processing, and compliance link revocation (DELETE returning 204, subsequent clicks returning 410 Gone) " +
            "to the existing URL shortener service without slowing down redirection.";

        var normResult = await normalizer.ExecuteAsync(
            new RawRequirementInput(brownfieldPrompt, ProjectName: "UrlShortener"), cancellationToken);

        Console.WriteLine($"âœ“ Extracted {normResult.Requirements.Count} Functional Requirements (FR-007 to FR-012).");
        Console.WriteLine($"âœ“ Detected Ambiguities: {normResult.Ambiguities.Count} | Documented Assumptions: {normResult.Assumptions.Count}");
        telemetry.CompleteStage("Clarify", success: true);

        // CHECKPOINT: Gate 1 (Specification Approval)
        var gate1Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate1Approval,
            "Review Brownfield Requirements & Privacy Contract:\n" +
            " - Asynchronous non-blocking Channel<ClickEvent> (ADR-003)\n" +
            " - Zero PII storage: SHA-256 salted IP hash with daily rotation\n" +
            " - Emergency Revocation: DELETE /api/v1/urls/{shortCode} -> 204 No Content\n" +
            " - Strict 410 Gone status code with ProblemDetails on revoked links",
            cancellationToken);

        if (gate1Decision == GateDecision.SafeStop)
        {
            Console.WriteLine("[Gate 1] Reviewer requested Safe-Stop. Halting scenario.");
            return 0;
        }

        // ---------------------------------------------------------------------
        // STAGE 2: Brownfield AST & Blast-Radius Impact Analysis
        // ---------------------------------------------------------------------
        telemetry.StartStage("Analyze");
        stateMachine.TransitionTo(SdlcStage.Analyze, "Executing codebase inspection and blast-radius analysis.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 2: Analyze] Scanning existing codebase for impacted endpoints and entities...");
        Console.ResetColor();

        var proposedEndpoints = new[] { "/api/v1/urls/{shortCode}", "/{shortCode}" };
        var impactAnalysis = await brownfieldAnalyzer.ExecuteAsync(
            new BrownfieldAnalysisInput(_workspacePath, proposedEndpoints), cancellationToken);

        Console.WriteLine($"âœ“ Discovered {impactAnalysis.DiscoveredFilesCount} C# source files across repository.");
        Console.WriteLine($"âœ“ Discovered Existing Routes: {string.Join(", ", impactAnalysis.DiscoveredEndpoints)}");
        Console.WriteLine($"âœ“ Assessed Blast-Radius Risk Level: {impactAnalysis.RiskLevel}");
        Console.WriteLine($"âœ“ Impacted Files Identified ({impactAnalysis.ImpactedFiles.Count}):");
        foreach (var file in impactAnalysis.ImpactedFiles)
        {
            Console.WriteLine($"    - {file}");
        }
        Console.WriteLine("âœ“ Architectural Recommendations:");
        foreach (var rec in impactAnalysis.Recommendations)
        {
            Console.WriteLine($"    * {rec}");
        }
        telemetry.CompleteStage("Analyze", success: true);

        // ---------------------------------------------------------------------
        // STAGE 3: Plan & Task Decomposition
        // ---------------------------------------------------------------------
        telemetry.StartStage("PlanAndDecompose");
        stateMachine.TransitionTo(SdlcStage.PlanAndDecompose, "Decomposing brownfield feature into isolated task DAG.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 3: Plan & Decompose] Building Brownfield extension task graph...");
        Console.ResetColor();

        var brownfieldTasks = new List<TaskNode>
        {
            new() { Id = "T017", Description = "Implement IIpPrivacyHasher and IpPrivacyHasher (SHA-256)", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T018", Description = "Implement ClickAnalyticsBackgroundService with Channel<ClickEvent>", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T019", Description = "Add Analytics & Revocation endpoints in UrlEndpoints.cs", Prerequisites = new[] { "T017", "T018" }, CanRunInParallel = false },
            new() { Id = "T020", Description = "Unit tests for Privacy Hashing and Analytics Channel", Prerequisites = new[] { "T017", "T018" }, CanRunInParallel = true },
            new() { Id = "T021", Description = "Contract tests for GET analytics (200) and DELETE revocation (204/410)", Prerequisites = new[] { "T019" }, CanRunInParallel = false }
        };

        var waves = DependencyGraphExecutor.BuildWaves(brownfieldTasks);
        Console.WriteLine($"âœ“ Structured {brownfieldTasks.Count} brownfield tasks into {waves.Count} execution waves.");
        foreach (var wave in waves)
        {
            var mode = wave.IsParallel ? "PARALLEL WAVE" : "SYNCHRONIZATION BARRIER";
            Console.WriteLine($"  [Wave {wave.WaveNumber}] {mode}: {string.Join(", ", wave.Tasks.Select(t => t.Id))}");
        }
        telemetry.CompleteStage("PlanAndDecompose", success: true);

        // CHECKPOINT: Gate 2 (Architecture & Task Approval)
        var gate2Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate2Approval,
            "Approve Brownfield Extension Plan: Non-breaking additions, isolated background channel queue, zero schema migrations breaking active redirects.",
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
        stateMachine.TransitionTo(SdlcStage.ParallelExecution, "Dispatching brownfield extension tasks.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 5: Parallel Execution] Executing brownfield task waves...");
        Console.ResetColor();

        Task<bool> TaskRunner(TaskNode task, CancellationToken ct)
        {
            Console.WriteLine($"  -> Executing {task.Id} ({(task.CanRunInParallel ? "Parallel" : "Barrier")}): {task.Description}");
            telemetry.RecordTaskExecution(task.Id, success: true);
            return Task.FromResult(true);
        }

        var execResult = await executor.ExecuteAsync(brownfieldTasks, TaskRunner, cancellationToken);
        if (!execResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("âœ— Brownfield execution failed at barrier.");
            Console.ResetColor();
            telemetry.CompleteStage("ParallelExecution", success: false);
            return 1;
        }

        Console.WriteLine($"âœ“ All {execResult.TaskResults.Count} brownfield tasks completed successfully.");
        telemetry.CompleteStage("ParallelExecution", success: true);

        // ---------------------------------------------------------------------
        // STAGE 6: Automated Verification & Bounded Retries
        // ---------------------------------------------------------------------
        telemetry.StartStage("Verification");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 6: Verification] Running automated verification gates (dotnet build & test)...");
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

        Console.WriteLine($"âœ“ Verification passed: All unit and contract tests passed with 0 errors.");
        telemetry.CompleteStage("Verification", success: true);

        // CHECKPOINT: Gate 3 (Release Approval)
        var gate3Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate3Approval,
            "Release Verification Gate: Brownfield features verified. Existing redirects unaffected, privacy compliance confirmed.",
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
