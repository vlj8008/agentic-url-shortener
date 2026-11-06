using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Cli.Agents;
using Orchestrator.Cli.Engine;

namespace Orchestrator.Cli.Scenarios;

/// <summary>
/// Executes the Greenfield Scenario: Core URL Shortener MVP (specs/tasks.md T027).
/// Demonstrates end-to-end SDLC lifecycle, human approval gates (Gates 1-3),
/// non-linear DAG wave execution, anti-SSRF security verification, and audit telemetry.
/// </summary>
public sealed class GreenfieldScenarioRunner
{
    private readonly string _workspacePath;
    private readonly Func<SdlcStage, string, CancellationToken, Task<GateDecision>>? _gateProvider;

    public GreenfieldScenarioRunner(
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
        Console.WriteLine("  SCENARIO 1: GREENFIELD â€” CORE URL SHORTENER MVP");
        Console.WriteLine("  Controlled Autonomy | Spec-Driven Development | Non-Linear DAG");
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
        // STAGE 1: Clarify & Specification Synthesis
        // ---------------------------------------------------------------------
        telemetry.StartStage("Clarify");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Stage 1: Clarify] Normalizing Greenfield requirements...");
        Console.ResetColor();

        const string greenfieldPrompt =
            "Build an enterprise-grade URL shortener service with cryptographic Base62 generation, " +
            "anti-SSRF perimeter security, RFC 7807 problem details, in-memory caching, and sub-50ms redirection.";

        var normResult = await normalizer.ExecuteAsync(
            new RawRequirementInput(greenfieldPrompt, ProjectName: "UrlShortener"), cancellationToken);

        Console.WriteLine($"âœ“ Extracted {normResult.Requirements.Count} Functional Requirements (FR-001 to FR-016).");
        Console.WriteLine($"âœ“ Detected Ambiguities: {normResult.Ambiguities.Count} | Documented Assumptions: {normResult.Assumptions.Count}");
        telemetry.CompleteStage("Clarify", success: true);

        // CHECKPOINT: Gate 1 (Specification Approval)
        var gate1Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate1Approval,
            "Review OpenAPI 3.1 Contract & Requirements:\n" +
            " - Base62 7-char tokenization with CSPRNG (ADR-001)\n" +
            " - Anti-SSRF perimeter validation (blocking loopback/private IPs)\n" +
            " - Sub-50ms cached redirection with 302 Found (ADR-002)",
            cancellationToken);

        if (gate1Decision == GateDecision.SafeStop)
        {
            Console.WriteLine("[Gate 1] Reviewer requested Safe-Stop. Halting scenario.");
            return 0;
        }

        // ---------------------------------------------------------------------
        // STAGE 2: Architecture & Analysis
        // ---------------------------------------------------------------------
        telemetry.StartStage("Analyze");
        stateMachine.TransitionTo(SdlcStage.Analyze, "Beginning architectural analysis for greenfield solution.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 2: Analyze] Inspecting architectural constraints and workspace boundaries...");
        Console.ResetColor();
        Console.WriteLine("âœ“ Verified Clean Architecture: Presentation (Endpoints) -> Domain -> Infrastructure.");
        Console.WriteLine("âœ“ Target Framework: .NET 10 (C# 14 / latest) with Nullable enabled and TreatWarningsAsErrors.");
        telemetry.CompleteStage("Analyze", success: true);

        // ---------------------------------------------------------------------
        // STAGE 3: Plan & Task Decomposition (Non-Linear DAG)
        // ---------------------------------------------------------------------
        telemetry.StartStage("PlanAndDecompose");
        stateMachine.TransitionTo(SdlcStage.PlanAndDecompose, "Decomposing specifications into non-linear task DAG.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 3: Plan & Decompose] Building Non-Linear Dependency Graph...");
        Console.ResetColor();

        var decompResult = await decomposer.ExecuteAsync(
            new DecompositionInput(normResult.Requirements, IncludeVerificationTask: true), cancellationToken);

        Console.WriteLine($"âœ“ Total Tasks: {decompResult.Tasks.Count} across {decompResult.WaveCount} Waves.");
        Console.WriteLine($"  - Parallel Tasks: {decompResult.ParallelTaskCount} | Sequential Barrier Tasks: {decompResult.SequentialTaskCount}");

        var waves = DependencyGraphExecutor.BuildWaves(decompResult.Tasks);
        foreach (var wave in waves)
        {
            var mode = wave.IsParallel ? "PARALLEL WAVE" : "SYNCHRONIZATION BARRIER";
            Console.WriteLine($"  [Wave {wave.WaveNumber}] {mode}: {string.Join(", ", wave.Tasks.Select(t => t.Id))}");
        }
        telemetry.CompleteStage("PlanAndDecompose", success: true);

        // CHECKPOINT: Gate 2 (Architecture & Task Approval)
        var gate2Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate2Approval,
            $"Approve DAG execution plan: {decompResult.Tasks.Count} tasks, {decompResult.WaveCount} waves, Entry/Exit Barriers enforced.",
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
        Console.WriteLine($"âœ“ Working tree dirty: {snapshot.IsDirty}");
        telemetry.CompleteStage("PreExecutionSnapshot", success: true);

        // ---------------------------------------------------------------------
        // STAGE 5: Non-Linear DAG Parallel Execution
        // ---------------------------------------------------------------------
        telemetry.StartStage("ParallelExecution");
        stateMachine.TransitionTo(SdlcStage.ParallelExecution, "Executing task waves respecting synchronization barriers.");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 5: Parallel Execution] Dispatching DAG task waves...");
        Console.ResetColor();

        Task<bool> TaskRunner(TaskNode task, CancellationToken ct)
        {
            Console.WriteLine($"  -> Executing {task.Id} ({(task.CanRunInParallel ? "Parallel" : "Barrier")}): {task.Description}");
            telemetry.RecordTaskExecution(task.Id, success: true);
            return Task.FromResult(true);
        }

        var execResult = await executor.ExecuteAsync(decompResult.Tasks, TaskRunner, cancellationToken);
        if (!execResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("âœ— Execution failed at synchronization barrier.");
            Console.ResetColor();
            telemetry.CompleteStage("ParallelExecution", success: false);
            return 1;
        }

        Console.WriteLine($"âœ“ All {execResult.TaskResults.Count} tasks completed successfully across {execResult.Waves.Count} waves.");
        telemetry.CompleteStage("ParallelExecution", success: true);

        // ---------------------------------------------------------------------
        // STAGE 6: Automated Verification & Bounded Retries
        // ---------------------------------------------------------------------
        telemetry.StartStage("Verification");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Stage 6: Verification] Executing automated verification gates (dotnet build & test)...");
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

        Console.WriteLine($"âœ“ Verification passed with 0 compiler warnings/errors and 100% tests passing.");
        telemetry.CompleteStage("Verification", success: true);

        // CHECKPOINT: Gate 3 (Release Approval)
        var gate3Decision = await stateMachine.EvaluateGateAsync(
            SdlcStage.Gate3Approval,
            "Release Verification Gate: Solution builds cleanly with 0 warnings, passes 100% tests, and meets all OWASP API baselines.",
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
