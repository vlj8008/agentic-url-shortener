using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Cli.Agents;
using Orchestrator.Cli.Engine;
using Xunit;

namespace UrlShortener.UnitTests;

/// <summary>
/// Unit tests for the SDLC Orchestrator State Machine, DAG Execution, Dynamic Re-Planning,
/// Bounded Retries, Automatic Rollback, and MTTR Telemetry (specs/tasks.md T028).
/// </summary>
public sealed class OrchestratorStateMachineTests
{
    #region Non-Linear DAG Ordering & Cycle Validation

    [Fact]
    public void BuildWaves_ValidNonLinearGraph_PartitionsIntoCorrectParallelAndSequentialWaves()
    {
        // Arrange: 2 parallel root tasks, 1 sequential barrier task, and 2 parallel downstream tasks
        var tasks = new List<TaskNode>
        {
            new() { Id = "T1", Description = "Scaffold API", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T2", Description = "Scaffold CLI", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T3", Description = "Barrier 1: Build All", Prerequisites = new[] { "T1", "T2" }, CanRunInParallel = false },
            new() { Id = "T4", Description = "Domain Models", Prerequisites = new[] { "T3" }, CanRunInParallel = true },
            new() { Id = "T5", Description = "Anti-SSRF Perimeter", Prerequisites = new[] { "T3" }, CanRunInParallel = true }
        };

        // Act
        var waves = DependencyGraphExecutor.BuildWaves(tasks);

        // Assert
        Assert.NotNull(waves);
        Assert.Equal(3, waves.Count);

        // Wave 1: Parallel roots (T1, T2)
        Assert.Equal(1, waves[0].WaveNumber);
        Assert.True(waves[0].IsParallel);
        Assert.Equal(2, waves[0].Tasks.Count);
        Assert.Contains(waves[0].Tasks, t => t.Id == "T1");
        Assert.Contains(waves[0].Tasks, t => t.Id == "T2");

        // Wave 2: Synchronization Barrier (T3)
        Assert.Equal(2, waves[1].WaveNumber);
        Assert.False(waves[1].IsParallel);
        var singleTask = Assert.Single(waves[1].Tasks);
        Assert.Equal("T3", singleTask.Id);

        // Wave 3: Downstream Parallel Tasks (T4, T5)
        Assert.Equal(3, waves[2].WaveNumber);
        Assert.True(waves[2].IsParallel);
        Assert.Equal(2, waves[2].Tasks.Count);
        Assert.Contains(waves[2].Tasks, t => t.Id == "T4");
        Assert.Contains(waves[2].Tasks, t => t.Id == "T5");
    }

    [Fact]
    public void ValidateGraph_MissingPrerequisite_ThrowsInvalidOperationException()
    {
        // Arrange: Task references a prerequisite ID that does not exist in the graph
        var tasks = new List<TaskNode>
        {
            new() { Id = "T1", Description = "Scaffold Project", Prerequisites = Array.Empty<string>() },
            new() { Id = "T2", Description = "Build Project", Prerequisites = new[] { "NON_EXISTENT_ID" } }
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => DependencyGraphExecutor.ValidateGraph(tasks));
        Assert.Contains("missing prerequisite", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NON_EXISTENT_ID", ex.Message);
    }

    [Fact]
    public void ValidateGraph_DirectCircularDependency_ThrowsInvalidOperationException()
    {
        // Arrange: A -> B -> A cycle
        var tasks = new List<TaskNode>
        {
            new() { Id = "A", Description = "Task A", Prerequisites = new[] { "B" } },
            new() { Id = "B", Description = "Task B", Prerequisites = new[] { "A" } }
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => DependencyGraphExecutor.ValidateGraph(tasks));
        Assert.Contains("circular dependency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateGraph_MultiNodeCircularDependency_ThrowsInvalidOperationException()
    {
        // Arrange: A -> B -> C -> A cycle
        var tasks = new List<TaskNode>
        {
            new() { Id = "A", Description = "Task A", Prerequisites = new[] { "C" } },
            new() { Id = "B", Description = "Task B", Prerequisites = new[] { "A" } },
            new() { Id = "C", Description = "Task C", Prerequisites = new[] { "B" } }
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => DependencyGraphExecutor.ValidateGraph(tasks));
        Assert.Contains("circular dependency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_SequentialWavesWithBarrierFailure_HaltsAtFailedBarrier()
    {
        // Arrange
        var executor = new DependencyGraphExecutor();
        var tasks = new List<TaskNode>
        {
            new() { Id = "T1", Description = "Parallel Step", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T2", Description = "Barrier Step (Fails)", Prerequisites = new[] { "T1" }, CanRunInParallel = false },
            new() { Id = "T3", Description = "Downstream Step (Never Reached)", Prerequisites = new[] { "T2" }, CanRunInParallel = true }
        };

        var executedTaskIds = new List<string>();

        Task<bool> MockTaskRunner(TaskNode task, CancellationToken ct)
        {
            executedTaskIds.Add(task.Id);
            if (task.Id == "T2")
            {
                return Task.FromResult(false); // Simulate barrier failure
            }

            return Task.FromResult(true);
        }

        // Act
        var result = await executor.ExecuteAsync(tasks, MockTaskRunner, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("T1", executedTaskIds);
        Assert.Contains("T2", executedTaskIds);
        Assert.DoesNotContain("T3", executedTaskIds); // T3 must not execute due to barrier failure
    }

    #endregion

    #region Human Approval Gates & Dynamic Re-Planning

    [Theory]
    [InlineData(SdlcStage.Gate1Approval, SdlcStage.Analyze)]
    [InlineData(SdlcStage.Gate2Approval, SdlcStage.PreExecutionSnapshot)]
    [InlineData(SdlcStage.Gate3Approval, SdlcStage.Completed)]
    public async Task EvaluateGateAsync_Approved_TransitionsToDesignatedNextStage(SdlcStage gateStage, SdlcStage expectedNext)
    {
        // Arrange
        var stateMachine = new SdlcStateMachine((stage, prompt, ct) => Task.FromResult(GateDecision.Approve));

        // Act
        var decision = await stateMachine.EvaluateGateAsync(gateStage, "Review checkpoint specification");

        // Assert
        Assert.Equal(GateDecision.Approve, decision);
        Assert.Equal(expectedNext, stateMachine.CurrentStage);
    }

    [Theory]
    [InlineData(SdlcStage.Gate1Approval, SdlcStage.Clarify)]
    [InlineData(SdlcStage.Gate2Approval, SdlcStage.PlanAndDecompose)]
    [InlineData(SdlcStage.Gate3Approval, SdlcStage.PlanAndDecompose)]
    public async Task EvaluateGateAsync_Rejected_RoutesBackwardForDynamicReplanning(SdlcStage gateStage, SdlcStage expectedFallback)
    {
        // Arrange
        var stateMachine = new SdlcStateMachine((stage, prompt, ct) => Task.FromResult(GateDecision.Reject));

        // Act
        var decision = await stateMachine.EvaluateGateAsync(gateStage, "Reject checkpoint to trigger replan");

        // Assert
        Assert.Equal(GateDecision.Reject, decision);
        Assert.Equal(expectedFallback, stateMachine.CurrentStage);
        Assert.Equal(0, stateMachine.VerificationAttemptCount);
    }

    [Fact]
    public async Task EvaluateGateAsync_SafeStop_TransitionsToSafeStopped()
    {
        // Arrange
        var stateMachine = new SdlcStateMachine((stage, prompt, ct) => Task.FromResult(GateDecision.SafeStop));

        // Act
        var decision = await stateMachine.EvaluateGateAsync(SdlcStage.Gate2Approval, "Stop pipeline execution");

        // Assert
        Assert.Equal(GateDecision.SafeStop, decision);
        Assert.Equal(SdlcStage.SafeStopped, stateMachine.CurrentStage);
    }

    [Fact]
    public void TaskDecomposerAgent_ReplanDownstream_PrunesCompletedTasksAndInjectsRefinementTask()
    {
        // Arrange
        var decomposer = new TaskDecomposerAgent();
        var initialTasks = new List<TaskNode>
        {
            new() { Id = "T001", Description = "Scaffold Repo", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T002", Description = "Configure CI/CD", Prerequisites = Array.Empty<string>(), CanRunInParallel = true },
            new() { Id = "T003", Description = "Core Domain Logic", Prerequisites = new[] { "T001", "T002" }, CanRunInParallel = false },
            new() { Id = "T004", Description = "API Endpoints", Prerequisites = new[] { "T003" }, CanRunInParallel = true }
        };

        var completedTasks = new[] { "T001", "T002" };

        // Act: Reject T003 due to missing security rules
        var replannedTasks = decomposer.ReplanDownstream(
            initialTasks,
            completedTasks,
            rejectedTaskIdOrGate: "T003",
            rejectionReason: "Add Anti-SSRF Perimeter Protection");

        // Assert:
        Assert.NotNull(replannedTasks);

        // 1. Completed tasks (T001, T002) should not appear in the replanned task list
        Assert.DoesNotContain(replannedTasks, t => t.Id == "T001");
        Assert.DoesNotContain(replannedTasks, t => t.Id == "T002");

        // 2. Refinement task must be injected as the root of the replanned graph
        var refinementTask = replannedTasks.FirstOrDefault(t => t.Id == "REPLAN-T003");
        Assert.NotNull(refinementTask);
        Assert.Empty(refinementTask.Prerequisites);
        Assert.Contains("Anti-SSRF Perimeter Protection", refinementTask.Description);

        // 3. T003 and T004 should remain, with completed prerequisites pruned and refinement task chained
        var refinedT003 = replannedTasks.FirstOrDefault(t => t.Id == "T003");
        Assert.NotNull(refinedT003);
        Assert.Contains(refinementTask.Id, refinedT003.Prerequisites);
        Assert.DoesNotContain("T001", refinedT003.Prerequisites);
        Assert.DoesNotContain("T002", refinedT003.Prerequisites);

        var replannedT004 = replannedTasks.FirstOrDefault(t => t.Id == "T004");
        Assert.NotNull(replannedT004);
        Assert.Contains("T003", replannedT004.Prerequisites);

        // 4. Validate that the replanned graph passes DAG cycle and prerequisite validation
        DependencyGraphExecutor.ValidateGraph(replannedTasks);
        var replannedWaves = DependencyGraphExecutor.BuildWaves(replannedTasks);
        Assert.True(replannedWaves.Count >= 2);
    }

    #endregion

    #region Bounded Retries & Automatic Rollback

    [Fact]
    public void HandleVerificationResult_Successful_TransitionsToGate3()
    {
        // Arrange
        var stateMachine = new SdlcStateMachine();

        // Act
        var outcome = stateMachine.HandleVerificationResult(passed: true, details: "All 3 test suites passed 100%");

        // Assert
        Assert.Equal(VerificationOutcome.ProceedToGate3, outcome);
        Assert.Equal(SdlcStage.Gate3Approval, stateMachine.CurrentStage);
        Assert.Equal(0, stateMachine.VerificationAttemptCount);
    }

    [Fact]
    public void HandleVerificationResult_BoundedRetries_AllowsUpToThreeSelfCorrections()
    {
        // Arrange
        var stateMachine = new SdlcStateMachine();

        // Attempt 1: First failure triggers retry 1/3
        var outcome1 = stateMachine.HandleVerificationResult(passed: false, details: "CS0246 compilation error");
        Assert.Equal(VerificationOutcome.RetrySelfCorrection, outcome1);
        Assert.Equal(1, stateMachine.VerificationAttemptCount);
        Assert.Equal(SdlcStage.ParallelExecution, stateMachine.CurrentStage);

        // Attempt 2: Second failure triggers retry 2/3
        var outcome2 = stateMachine.HandleVerificationResult(passed: false, details: "Unit test assertion failed");
        Assert.Equal(VerificationOutcome.RetrySelfCorrection, outcome2);
        Assert.Equal(2, stateMachine.VerificationAttemptCount);
        Assert.Equal(SdlcStage.ParallelExecution, stateMachine.CurrentStage);

        // Attempt 3: Third failure triggers retry 3/3 (last allowed attempt)
        var outcome3 = stateMachine.HandleVerificationResult(passed: false, details: "Contract schema mismatch");
        Assert.Equal(VerificationOutcome.RetrySelfCorrection, outcome3);
        Assert.Equal(3, stateMachine.VerificationAttemptCount);
        Assert.Equal(SdlcStage.ParallelExecution, stateMachine.CurrentStage);
    }

    [Fact]
    public void HandleVerificationResult_ExhaustedRetries_TriggersAutomaticRollbackAndSafeStop()
    {
        // Arrange
        var stateMachine = new SdlcStateMachine();

        // Fail 3 times (attempts 1, 2, 3)
        stateMachine.HandleVerificationResult(passed: false, details: "Error 1");
        stateMachine.HandleVerificationResult(passed: false, details: "Error 2");
        stateMachine.HandleVerificationResult(passed: false, details: "Error 3");
        Assert.Equal(3, stateMachine.VerificationAttemptCount);

        // Act: 4th failure exhausts all bounded retries
        var terminalOutcome = stateMachine.HandleVerificationResult(passed: false, details: "Terminal failure 4");

        // Assert: Must trigger Rollback and SafeStop
        Assert.Equal(VerificationOutcome.RollbackAndStop, terminalOutcome);
        Assert.Equal(4, stateMachine.VerificationAttemptCount);
        Assert.Equal(SdlcStage.SafeStopped, stateMachine.CurrentStage);

        // Verify transition audit lineage includes RolledBack before SafeStopped
        var transitions = stateMachine.Transitions;
        Assert.Contains(transitions, t => t.ToStage == SdlcStage.RolledBack);
        Assert.Contains(transitions, t => t.ToStage == SdlcStage.SafeStopped);
    }

    #endregion

    #region MTTR Telemetry & Audit Reporting

    [Fact]
    public void TelemetryTracker_TaskOutcomesAndSuccessRate_CalculatedAccurately()
    {
        // Arrange
        var tracker = new TelemetryTracker();
        tracker.StartWorkflow();

        // Act: 8 successes and 2 failures (80% success rate)
        for (var i = 1; i <= 8; i++)
        {
            tracker.RecordTaskExecution($"TASK-SUCC-{i}", success: true);
        }
        tracker.RecordTaskExecution("TASK-FAIL-1", success: false);
        tracker.RecordTaskExecution("TASK-FAIL-2", success: false);

        tracker.StopWorkflow();
        var report = tracker.GenerateReport();

        // Assert
        Assert.Equal(10, report.TotalTasks);
        Assert.Equal(8, report.SuccessfulTasks);
        Assert.Equal(2, report.FailedTasks);
        Assert.Equal(80.0, report.SuccessRatePercentage, precision: 2);
    }

    [Fact]
    public void TelemetryTracker_CalculateMttrSeconds_AveragesRecoveredDurations()
    {
        // Arrange
        var tracker = new TelemetryTracker();

        // Record 2 recovered retries: 10s and 20s -> average MTTR should be 15s
        tracker.RecordRetry("Verification", attemptNumber: 1, duration: TimeSpan.FromSeconds(10), recovered: true);
        tracker.RecordRetry("Verification", attemptNumber: 2, duration: TimeSpan.FromSeconds(20), recovered: true);

        // Act
        var mttr = tracker.CalculateMttrSeconds();

        // Assert
        Assert.Equal(15.0, mttr, precision: 2);
    }

    [Fact]
    public void TelemetryTracker_CalculateMttrSeconds_WithRollback_AveragesAllRetries()
    {
        // Arrange
        var tracker = new TelemetryTracker();

        // Record unrecovered retries leading to rollback
        tracker.RecordRetry("Verification", attemptNumber: 1, duration: TimeSpan.FromSeconds(12), recovered: false);
        tracker.RecordRetry("Verification", attemptNumber: 2, duration: TimeSpan.FromSeconds(18), recovered: false);
        tracker.RecordRollback();

        // Act
        var mttr = tracker.CalculateMttrSeconds();

        // Assert
        Assert.Equal(15.0, mttr, precision: 2);
    }

    [Fact]
    public void TelemetryTracker_EmptyState_ReturnsZeroMttrAndReportWithoutCrashing()
    {
        // Arrange
        var tracker = new TelemetryTracker();

        // Act
        var mttr = tracker.CalculateMttrSeconds();
        var report = tracker.GenerateReport();

        // Assert
        Assert.Equal(0.0, mttr);
        Assert.Equal(0, report.TotalTasks);
        Assert.Equal(0.0, report.SuccessRatePercentage);
        Assert.Equal(0, report.RetryCount);
        Assert.Equal(0, report.RollbackCount);
    }

    [Fact]
    public void TelemetryTracker_GenerateReport_IncludesStageDurationsAndRollbackMetrics()
    {
        // Arrange
        var tracker = new TelemetryTracker();
        tracker.StartWorkflow();

        tracker.StartStage("Clarify");
        Thread.Sleep(10);
        tracker.CompleteStage("Clarify", success: true);

        tracker.StartStage("Verification");
        tracker.RecordRetry("Verification", attemptNumber: 1, duration: TimeSpan.FromSeconds(5), recovered: false);
        tracker.RecordRollback();
        tracker.CompleteStage("Verification", success: false);

        tracker.StopWorkflow();

        // Act
        var report = tracker.GenerateReport();

        // Assert
        Assert.True(report.TotalWorkflowDuration > TimeSpan.Zero);
        Assert.Equal(1, report.RetryCount);
        Assert.Equal(1, report.RollbackCount);
        Assert.True(report.StageDurations.ContainsKey("Clarify"));
        Assert.True(report.StageDurations.ContainsKey("Verification"));
    }

    #endregion
}
