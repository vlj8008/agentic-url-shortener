using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Orchestrator.Cli.Engine;

/// <summary>
/// Audit-grade telemetry tracking for SDLC workflow execution (specs/tasks.md T025).
/// Captures Task Success Rates, Bounded Retries, Automatic Rollbacks, MTTR, and Stage Latencies.
/// </summary>
public sealed class TelemetryTracker
{
    private readonly Stopwatch _workflowStopwatch = new();
    private readonly ConcurrentDictionary<string, StageTelemetry> _stages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _taskOutcomes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RetryEvent> _retries = new();
    private readonly object _lock = new();

    private int _totalTasks;
    private int _successfulTasks;
    private int _failedTasks;
    private int _rollbackCount;

    public void StartWorkflow()
    {
        _workflowStopwatch.Restart();
    }

    public void StopWorkflow()
    {
        _workflowStopwatch.Stop();
    }

    public void StartStage(string stageName)
    {
        var telemetry = _stages.GetOrAdd(stageName, name => new StageTelemetry(name));
        telemetry.Start();
    }

    public void CompleteStage(string stageName, bool success = true)
    {
        if (_stages.TryGetValue(stageName, out var telemetry))
        {
            telemetry.Complete(success);
        }
    }

    public void RecordTaskExecution(string taskId, bool success)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        _taskOutcomes[taskId] = success;

        lock (_lock)
        {
            _totalTasks++;
            if (success)
            {
                _successfulTasks++;
            }
            else
            {
                _failedTasks++;
            }
        }
    }

    public void RecordRetry(string stageName, int attemptNumber, TimeSpan duration, bool recovered)
    {
        lock (_lock)
        {
            _retries.Add(new RetryEvent(stageName, attemptNumber, duration, recovered, DateTimeOffset.UtcNow));
        }
    }

    public void RecordRollback()
    {
        lock (_lock)
        {
            _rollbackCount++;
        }
    }

    /// <summary>
    /// Computes Mean Time To Recovery (MTTR) for self-corrected failures in seconds.
    /// </summary>
    public double CalculateMttrSeconds()
    {
        lock (_lock)
        {
            var recoveredRetries = _retries.Where(r => r.Recovered).ToList();
            if (recoveredRetries.Count > 0)
            {
                return recoveredRetries.Average(r => r.Duration.TotalSeconds);
            }

            if (_retries.Count > 0 && _rollbackCount > 0)
            {
                return _retries.Average(r => r.Duration.TotalSeconds);
            }

            return 0.0;
        }
    }

    public TelemetryReport GenerateReport()
    {
        lock (_lock)
        {
            var totalTasks = Math.Max(_totalTasks, 1);
            var successRate = (double)_successfulTasks / totalTasks * 100.0;
            var mttr = CalculateMttrSeconds();

            return new TelemetryReport(
                TotalWorkflowDuration: _workflowStopwatch.Elapsed,
                TotalTasks: _totalTasks,
                SuccessfulTasks: _successfulTasks,
                FailedTasks: _failedTasks,
                SuccessRatePercentage: successRate,
                RetryCount: _retries.Count,
                RollbackCount: _rollbackCount,
                MeanTimeToRecoverySeconds: mttr,
                StageDurations: _stages.ToDictionary(k => k.Key, v => v.Value.Duration));
        }
    }
}

public sealed class StageTelemetry
{
    private readonly Stopwatch _stopwatch = new();

    public string StageName { get; }
    public TimeSpan Duration => _stopwatch.Elapsed;
    public bool IsSuccess { get; private set; }

    public StageTelemetry(string stageName)
    {
        StageName = stageName;
    }

    public void Start() => _stopwatch.Start();

    public void Complete(bool success)
    {
        _stopwatch.Stop();
        IsSuccess = success;
    }
}

public sealed record RetryEvent(
    string StageName,
    int AttemptNumber,
    TimeSpan Duration,
    bool Recovered,
    DateTimeOffset Timestamp);

public sealed record TelemetryReport(
    TimeSpan TotalWorkflowDuration,
    int TotalTasks,
    int SuccessfulTasks,
    int FailedTasks,
    double SuccessRatePercentage,
    int RetryCount,
    int RollbackCount,
    double MeanTimeToRecoverySeconds,
    IReadOnlyDictionary<string, TimeSpan> StageDurations);
