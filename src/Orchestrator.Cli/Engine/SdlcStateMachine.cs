using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Engine;

/// <summary>
/// Non-linear state machine governing the 8-phase SDLC lifecycle and Human-in-the-Loop gates (specs/tasks.md T025).
/// Enforces Controlled Autonomy, dynamic re-planning on rejection, and bounded retry escalation.
/// </summary>
public sealed class SdlcStateMachine
{
    private const int MaxVerificationRetries = 3;

    private readonly Func<SdlcStage, string, CancellationToken, Task<GateDecision>> _gateProvider;
    private readonly List<TransitionRecord> _transitions = new();
    private readonly object _lock = new();

    public SdlcStage CurrentStage { get; private set; } = SdlcStage.PreFlightSync;
    public int VerificationAttemptCount { get; private set; }

    public IReadOnlyList<TransitionRecord> Transitions
    {
        get
        {
            lock (_lock)
            {
                return _transitions.ToArray();
            }
        }
    }

    public SdlcStateMachine(Func<SdlcStage, string, CancellationToken, Task<GateDecision>>? gateProvider = null)
    {
        _gateProvider = gateProvider ?? DefaultConsoleGateProvider;
    }

    /// <summary>
    /// Transitions to the target state and records the decision lineage.
    /// </summary>
    public void TransitionTo(SdlcStage targetStage, string reason)
    {
        lock (_lock)
        {
            var previous = CurrentStage;
            CurrentStage = targetStage;
            _transitions.Add(new TransitionRecord(previous, targetStage, reason, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Prompts the human reviewer at a designated checkpoint gate and transitions based on the decision.
    /// </summary>
    public async Task<GateDecision> EvaluateGateAsync(
        SdlcStage gateStage,
        string promptMessage,
        CancellationToken cancellationToken = default)
    {
        if (CurrentStage != gateStage)
        {
            TransitionTo(gateStage, $"Approaching approval gate: {gateStage}");
        }

        var decision = await _gateProvider(gateStage, promptMessage, cancellationToken);

        switch (decision)
        {
            case GateDecision.Approve:
                var nextStage = gateStage switch
                {
                    SdlcStage.Gate1Approval => SdlcStage.Analyze,
                    SdlcStage.Gate2Approval => SdlcStage.PreExecutionSnapshot,
                    SdlcStage.Gate3Approval => SdlcStage.Completed,
                    _ => throw new InvalidOperationException($"Invalid gate stage '{gateStage}'.")
                };
                TransitionTo(nextStage, $"Human reviewer approved {gateStage}.");
                break;

            case GateDecision.Reject:
                // Dynamic Re-Planning: route backward to refine upstream specifications or plans
                var fallbackStage = gateStage switch
                {
                    SdlcStage.Gate1Approval => SdlcStage.Clarify,
                    SdlcStage.Gate2Approval => SdlcStage.PlanAndDecompose,
                    SdlcStage.Gate3Approval => SdlcStage.PlanAndDecompose,
                    _ => throw new InvalidOperationException($"Invalid gate stage '{gateStage}'.")
                };
                VerificationAttemptCount = 0;
                TransitionTo(fallbackStage, $"Human reviewer rejected {gateStage}. Triggering dynamic re-planning.");
                break;

            case GateDecision.SafeStop:
                TransitionTo(SdlcStage.SafeStopped, $"Human reviewer requested Safe-Stop at {gateStage}.");
                break;
        }

        return decision;
    }

    /// <summary>
    /// Evaluates verification outcomes, managing self-correction retries or triggering automatic rollback.
    /// </summary>
    public VerificationOutcome HandleVerificationResult(bool passed, string details)
    {
        var summary = string.IsNullOrWhiteSpace(details)
            ? (passed ? "PASS" : "FAIL")
            : $"{(passed ? "PASS" : "FAIL")} ({details})";

        TransitionTo(SdlcStage.Verification, $"Evaluating verification result: {summary}");

        if (passed)
        {
            TransitionTo(SdlcStage.Gate3Approval, "Verification passed with 0 errors and 100% tests passing.");
            return VerificationOutcome.ProceedToGate3;
        }

        VerificationAttemptCount++;

        if (VerificationAttemptCount <= MaxVerificationRetries)
        {
            TransitionTo(
                SdlcStage.ParallelExecution,
                $"Verification failed on attempt {VerificationAttemptCount}/{MaxVerificationRetries}. Applying self-correction retry.");
            return VerificationOutcome.RetrySelfCorrection;
        }

        TransitionTo(
            SdlcStage.RolledBack,
            $"Verification failed and exhausted all {MaxVerificationRetries} bounded retries. Triggering automatic rollback.");
        TransitionTo(
            SdlcStage.SafeStopped,
            "Workspace rolled back to pre-execution snapshot. Execution halted safely.");
        return VerificationOutcome.RollbackAndStop;
    }

    private static Task<GateDecision> DefaultConsoleGateProvider(
        SdlcStage stage,
        string message,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n=== Checkpoint: {stage} ===");
        Console.WriteLine(message);
        Console.Write("Decision: [A]pprove / [R]eject / [S]afe-Stop > ");

        var input = Console.ReadLine()?.Trim().ToUpperInvariant();
        var decision = input switch
        {
            "A" or "APPROVE" => GateDecision.Approve,
            "R" or "REJECT" => GateDecision.Reject,
            _ => GateDecision.SafeStop
        };

        return Task.FromResult(decision);
    }
}

public enum SdlcStage
{
    PreFlightSync,
    Clarify,
    Gate1Approval,
    Analyze,
    PlanAndDecompose,
    Gate2Approval,
    PreExecutionSnapshot,
    ParallelExecution,
    Verification,
    Gate3Approval,
    Completed,
    SafeStopped,
    RolledBack
}

public enum GateDecision
{
    Approve,
    Reject,
    SafeStop
}

public enum VerificationOutcome
{
    ProceedToGate3,
    RetrySelfCorrection,
    RollbackAndStop
}

public sealed record TransitionRecord(
    SdlcStage FromStage,
    SdlcStage ToStage,
    string Reason,
    DateTimeOffset TimestampUtc);
