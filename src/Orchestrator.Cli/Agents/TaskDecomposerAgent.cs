using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Cli.Engine;

namespace Orchestrator.Cli.Agents;

/// <summary>
/// Specialized agent for non-linear dependency graph decomposition and dynamic downstream re-planning (specs/tasks.md T026).
/// Enforces Principle 2: Non-Linear Dependency Graphs &amp; Dynamic Re-Planning.
/// </summary>
public sealed class TaskDecomposerAgent : IAgentStage<DecompositionInput, DecompositionResult>
{
    public string Name => "TaskDecomposerAgent";

    public Task<DecompositionResult> ExecuteAsync(
        DecompositionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tasks = new List<TaskNode>();

        // Wave 1: Foundation Scaffolding (Parallel)
        tasks.Add(CreateTask("T001", "Scaffold Solution and Directory Layout", Array.Empty<string>(), canRunInParallel: true));
        tasks.Add(CreateTask("T002", "Configure CI/CD Workflow (.github/workflows/ci.yml)", Array.Empty<string>(), canRunInParallel: true));
        tasks.Add(CreateTask("T003", "Initialize Test Projects (Unit, Contract, Security)", Array.Empty<string>(), canRunInParallel: true));

        // Wave 2: Architectural Exit Barrier 1 (Sequential)
        tasks.Add(CreateTask("T004", "Foundation Verification Barrier 1", ["T001", "T002", "T003"], canRunInParallel: false));

        // Wave 3: Core Implementation Tracks (Parallel)
        tasks.Add(CreateTask("T005", "Implement Domain Entities and DbContext", ["T004"], canRunInParallel: true));
        tasks.Add(CreateTask("T006", "Implement Cryptographic Base62 Generator", ["T004"], canRunInParallel: true));
        tasks.Add(CreateTask("T007", "Implement Anti-SSRF Perimeter Validator", ["T004"], canRunInParallel: true));

        // Wave 4: Integration Barrier (Sequential)
        tasks.Add(CreateTask("T008", "Wire Minimal API Endpoints and Middleware", ["T005", "T006", "T007"], canRunInParallel: false));

        if (input.IncludeVerificationTask)
        {
            // Wave 5: Comprehensive Automated Verification (Sequential Barrier)
            tasks.Add(CreateTask("T009", "Execute Verification Gate (Build & Test Suites)", ["T008"], canRunInParallel: false));
        }

        // Validate graph structure and compute exact wave count
        var waves = DependencyGraphExecutor.BuildWaves(tasks);

        var parallelCount = tasks.Count(t => t.CanRunInParallel);
        var sequentialCount = tasks.Count - parallelCount;

        return Task.FromResult(new DecompositionResult(
            tasks,
            WaveCount: waves.Count,
            ParallelTaskCount: parallelCount,
            SequentialTaskCount: sequentialCount));
    }

    /// <summary>
    /// Dynamically recalculates downstream tasks following a gate or task rejection ([R]eject).
    /// Enforces Safeguard 2: Prunes completed task IDs from prerequisites and validates the resulting DAG.
    /// </summary>
    public IReadOnlyList<TaskNode> ReplanDownstream(
        IReadOnlyList<TaskNode> currentTasks,
        IReadOnlyCollection<string> completedTaskIds,
        string rejectedTaskIdOrGate,
        string rejectionReason)
    {
        ArgumentNullException.ThrowIfNull(currentTasks);
        completedTaskIds ??= Array.Empty<string>();

        // Identify downstream/remaining tasks that are not yet marked completed
        var remainingTasks = currentTasks
            .Where(t => !completedTaskIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Create a refinement task addressing the rejection reason
        var refinementTaskId = $"REPLAN-{rejectedTaskIdOrGate}";
        var refinementTask = CreateTask(
            refinementTaskId,
            $"Dynamic Re-Planning: Address rejection of '{rejectedTaskIdOrGate}' ({rejectionReason})",
            prerequisites: Array.Empty<string>(),
            canRunInParallel: false);

        var replannedList = new List<TaskNode> { refinementTask };
        var remainingTaskIds = new HashSet<string>(remainingTasks.Select(t => t.Id), StringComparer.OrdinalIgnoreCase)
        {
            refinementTaskId
        };

        foreach (var task in remainingTasks)
        {
            // Prune any prerequisites that were already completed (and omitted from remainingTasks)
            var validPrerequisites = task.Prerequisites
                .Where(p => remainingTaskIds.Contains(p))
                .ToList();

            // Any root task of the remaining subgraph (or the rejected task itself) must wait on the refinement task
            if (validPrerequisites.Count == 0 ||
                string.Equals(task.Id, rejectedTaskIdOrGate, StringComparison.OrdinalIgnoreCase) ||
                (task.Prerequisites.Contains(rejectedTaskIdOrGate, StringComparer.OrdinalIgnoreCase) &&
                 !validPrerequisites.Contains(rejectedTaskIdOrGate, StringComparer.OrdinalIgnoreCase)))
            {
                if (!validPrerequisites.Contains(refinementTaskId, StringComparer.OrdinalIgnoreCase))
                {
                    validPrerequisites.Add(refinementTaskId);
                }
            }

            var description = string.Equals(task.Id, rejectedTaskIdOrGate, StringComparison.OrdinalIgnoreCase)
                ? $"{task.Description} (Refined)"
                : task.Description;

            replannedList.Add(CreateTask(
                task.Id,
                description,
                validPrerequisites,
                canRunInParallel: task.CanRunInParallel));
        }

        // Validate the re-planned graph to guarantee zero missing prerequisites and zero cycles (Safeguard 2)
        DependencyGraphExecutor.ValidateGraph(replannedList);

        return replannedList;
    }

    private static TaskNode CreateTask(
        string id,
        string description,
        IReadOnlyList<string>? prerequisites = null,
        bool canRunInParallel = true) => new()
    {
        Id = id,
        Description = description,
        Prerequisites = prerequisites ?? Array.Empty<string>(),
        CanRunInParallel = canRunInParallel
    };
}

public sealed record DecompositionInput(
    IReadOnlyList<NormalizedRequirement>? Requirements = null,
    bool IncludeVerificationTask = true);

public sealed record DecompositionResult(
    IReadOnlyList<TaskNode> Tasks,
    int WaveCount,
    int ParallelTaskCount,
    int SequentialTaskCount);
