using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Engine;

/// <summary>
/// Executes tasks as a non-linear Directed Acyclic Graph (DAG) with wave-based parallelization
/// and synchronization barriers (specs/tasks.md T025).
/// </summary>
public sealed class DependencyGraphExecutor
{
    /// <summary>
    /// Validates the DAG for missing prerequisites and cycles, throwing InvalidOperationException if invalid.
    /// </summary>
    public static void ValidateGraph(IEnumerable<TaskNode> tasks)
    {
        var taskList = tasks.ToList();
        var taskMap = taskList.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        // 1. Check for missing prerequisite IDs
        foreach (var task in taskList)
        {
            foreach (var prereqId in task.Prerequisites)
            {
                if (!taskMap.ContainsKey(prereqId))
                {
                    throw new InvalidOperationException(
                        $"Task '{task.Id}' has missing prerequisite '{prereqId}'. Prerequisite does not exist in graph.");
                }
            }
        }

        // 2. Cycle detection using Depth-First Search (DFS) with visited state tracking
        var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=Unvisited, 1=Visiting, 2=Visited

        foreach (var task in taskList)
        {
            if (!visited.ContainsKey(task.Id))
            {
                if (HasCycleDfs(task.Id, taskMap, visited, new List<string>()))
                {
                    throw new InvalidOperationException("Circular dependency detected in task dependency graph.");
                }
            }
        }
    }

    private static bool HasCycleDfs(
        string currentId,
        Dictionary<string, TaskNode> taskMap,
        Dictionary<string, int> visited,
        List<string> currentPath)
    {
        visited[currentId] = 1; // Visiting (in current DFS stack)
        currentPath.Add(currentId);

        if (taskMap.TryGetValue(currentId, out var currentTask))
        {
            foreach (var prereqId in currentTask.Prerequisites)
            {
                if (visited.TryGetValue(prereqId, out var state))
                {
                    if (state == 1)
                    {
                        var cyclePath = string.Join(" -> ", currentPath.Concat([prereqId]));
                        throw new InvalidOperationException($"Circular dependency detected: {cyclePath}");
                    }
                }
                else
                {
                    if (HasCycleDfs(prereqId, taskMap, visited, currentPath))
                    {
                        return true;
                    }
                }
            }
        }

        visited[currentId] = 2; // Visited
        currentPath.RemoveAt(currentPath.Count - 1);
        return false;
    }

    /// <summary>
    /// Partitions tasks into sequential waves of parallel or single tasks respecting DAG dependencies.
    /// </summary>
    public static List<ExecutionWave> BuildWaves(IEnumerable<TaskNode> tasks)
    {
        ValidateGraph(tasks);

        var remainingTasks = tasks.Select(t => t.Clone()).ToList();
        var completedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waves = new List<ExecutionWave>();
        var waveNumber = 1;

        while (remainingTasks.Count > 0)
        {
            // Find all tasks whose prerequisites are completely satisfied
            var readyTasks = remainingTasks
                .Where(t => t.Prerequisites.All(p => completedIds.Contains(p)))
                .ToList();

            if (readyTasks.Count == 0)
            {
                throw new InvalidOperationException("Deadlock or unresolved dependencies detected during wave construction.");
            }

            // Group into parallel tasks vs sequential tasks
            var parallelTasks = readyTasks.Where(t => t.CanRunInParallel).ToList();
            if (parallelTasks.Count > 0)
            {
                waves.Add(new ExecutionWave(waveNumber++, parallelTasks, IsParallel: true));
                foreach (var pt in parallelTasks)
                {
                    completedIds.Add(pt.Id);
                    remainingTasks.Remove(pt);
                }
            }
            else
            {
                // Take one sequential task
                var sequentialTask = readyTasks.First();
                waves.Add(new ExecutionWave(waveNumber++, new List<TaskNode> { sequentialTask }, IsParallel: false));
                completedIds.Add(sequentialTask.Id);
                remainingTasks.Remove(sequentialTask);
            }
        }

        return waves;
    }

    /// <summary>
    /// Executes the task graph wave-by-wave, awaiting synchronization barriers between waves.
    /// </summary>
    public async Task<GraphExecutionResult> ExecuteAsync(
        IEnumerable<TaskNode> tasks,
        Func<TaskNode, CancellationToken, Task<bool>> taskRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskRunner);

        var waves = BuildWaves(tasks);
        var executedWaves = new List<ExecutedWaveResult>();
        var allTaskResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var wave in waves)
        {
            var waveResults = new List<TaskExecutionOutcome>();

            if (wave.IsParallel && wave.Tasks.Count > 1)
            {
                // Execute parallel wave concurrently via Task.WhenAll
                var runningTasks = wave.Tasks.Select(async task =>
                {
                    var success = await taskRunner(task, cancellationToken);
                    return new TaskExecutionOutcome(task.Id, task.Description, success);
                });

                var results = await Task.WhenAll(runningTasks);
                waveResults.AddRange(results);
            }
            else
            {
                // Execute sequentially
                foreach (var task in wave.Tasks)
                {
                    var success = await taskRunner(task, cancellationToken);
                    waveResults.Add(new TaskExecutionOutcome(task.Id, task.Description, success));
                }
            }

            executedWaves.Add(new ExecutedWaveResult(wave.WaveNumber, waveResults, wave.IsParallel));

            // Record outcomes into completed tracking
            foreach (var outcome in waveResults)
            {
                allTaskResults[outcome.TaskId] = outcome.Success;
                if (!outcome.Success)
                {
                    // Fail-fast at wave synchronization barrier
                    return new GraphExecutionResult(false, executedWaves, allTaskResults);
                }
            }
        }

        return new GraphExecutionResult(true, executedWaves, allTaskResults);
    }
}

public sealed class TaskNode
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Prerequisites { get; init; } = Array.Empty<string>();
    public bool CanRunInParallel { get; init; } = true;
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;

    public TaskNode Clone() => new()
    {
        Id = Id,
        Description = Description,
        Prerequisites = Prerequisites.ToArray(),
        CanRunInParallel = CanRunInParallel,
        Status = Status
    };
}

public enum TaskExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}

public sealed record ExecutionWave(int WaveNumber, IReadOnlyList<TaskNode> Tasks, bool IsParallel);

public sealed record TaskExecutionOutcome(string TaskId, string Description, bool Success);

public sealed record ExecutedWaveResult(int WaveNumber, IReadOnlyList<TaskExecutionOutcome> TaskOutcomes, bool WasParallel);

public sealed record GraphExecutionResult(
    bool Success,
    IReadOnlyList<ExecutedWaveResult> Waves,
    IReadOnlyDictionary<string, bool> TaskResults);
