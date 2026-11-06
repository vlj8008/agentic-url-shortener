using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Engine;

/// <summary>
/// Pre-execution Git snapshot checkpointing and automated workspace rollback manager (specs/tasks.md T025).
/// Enforces Principle 2: Safe-Stop & Automatic Rollback with non-destructive defaults.
/// </summary>
public sealed class GitSnapshotManager
{
    private readonly List<string> _commandLog = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> CommandLog
    {
        get
        {
            lock (_lock)
            {
                return _commandLog.ToArray();
            }
        }
    }

    /// <summary>
    /// Captures a snapshot of the current Git repository state before modifying code.
    /// In dryRun mode, performs read-only checks or returns a deterministic simulated checkpoint.
    /// </summary>
    public async Task<GitSnapshot> CaptureSnapshotAsync(
        string workspacePath,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        LogCommand($"[CaptureSnapshot] Workspace: {workspacePath} (dryRun={dryRun})");

        if (dryRun)
        {
            // Read-only inspection if directory exists, else fallback to safe simulated SHA
            if (Directory.Exists(workspacePath))
            {
                var revParseResult = await RunProcessAsync("git", "rev-parse HEAD", workspacePath, cancellationToken);
                var sha = revParseResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(revParseResult.Output)
                    ? revParseResult.Output.Trim()
                    : "snapshot-dryrun-checkpoint";

                var statusResult = await RunProcessAsync("git", "status --porcelain", workspacePath, cancellationToken);
                var isDirty = !string.IsNullOrWhiteSpace(statusResult.Output);

                return new GitSnapshot(sha, isDirty, DateTimeOffset.UtcNow, IsSimulated: true);
            }

            return new GitSnapshot("snapshot-simulated-head", false, DateTimeOffset.UtcNow, IsSimulated: true);
        }

        if (!Directory.Exists(workspacePath))
        {
            throw new DirectoryNotFoundException($"Workspace path '{workspacePath}' does not exist.");
        }

        var headCommitResult = await RunProcessAsync("git", "rev-parse HEAD", workspacePath, cancellationToken);
        if (headCommitResult.ExitCode != 0 || string.IsNullOrWhiteSpace(headCommitResult.Output))
        {
            throw new InvalidOperationException(
                $"Failed to capture Git HEAD snapshot in '{workspacePath}': {headCommitResult.Error}");
        }

        var headSha = headCommitResult.Output.Trim();

        var workTreeStatusResult = await RunProcessAsync("git", "status --porcelain", workspacePath, cancellationToken);
        var hasUncommittedChanges = !string.IsNullOrWhiteSpace(workTreeStatusResult.Output);

        return new GitSnapshot(headSha, hasUncommittedChanges, DateTimeOffset.UtcNow, IsSimulated: false);
    }

    /// <summary>
    /// Executes rollback to the captured snapshot.
    /// CRITICAL SAFETY: If dryRun is true, simulates the reset/clean operations without altering disk files.
    /// </summary>
    public async Task<bool> RollbackAsync(
        string workspacePath,
        GitSnapshot snapshot,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        LogCommand($"[Rollback] Reverting workspace '{workspacePath}' to snapshot '{snapshot.CommitSha}' (dryRun={dryRun})");

        if (dryRun || snapshot.IsSimulated)
        {
            LogCommand($"[SIMULATED] git reset --hard {snapshot.CommitSha}");
            LogCommand("[SIMULATED] git clean -fd");
            return true;
        }

        if (!Directory.Exists(workspacePath) || string.IsNullOrWhiteSpace(snapshot.CommitSha))
        {
            return false;
        }

        // Live hard reset back to the snapshot commit
        LogCommand($"[LIVE] git reset --hard {snapshot.CommitSha}");
        var resetResult = await RunProcessAsync("git", $"reset --hard {snapshot.CommitSha}", workspacePath, cancellationToken);
        if (resetResult.ExitCode != 0)
        {
            return false;
        }

        // Live clean untracked files and directories added during execution
        LogCommand("[LIVE] git clean -fd");
        var cleanResult = await RunProcessAsync("git", "clean -fd", workspacePath, cancellationToken);
        return cleanResult.ExitCode == 0;
    }

    private void LogCommand(string message)
    {
        lock (_lock)
        {
            _commandLog.Add($"[{DateTimeOffset.UtcNow:O}] {message}");
        }
    }

    /// <summary>
    /// Executes a system process asynchronously with standard output/error capture and bounded execution timeout.
    /// </summary>
    public static async Task<GitCommandResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15)); // Bounded 15s timeout

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);

            var output = await outputTask;
            var error = await errorTask;

            return new GitCommandResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return new GitCommandResult(-1, string.Empty, "Process timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            return new GitCommandResult(-1, string.Empty, ex.Message);
        }
    }
}

public sealed record GitSnapshot(
    string CommitSha,
    bool IsDirty,
    DateTimeOffset Timestamp,
    bool IsSimulated);

public sealed record GitCommandResult(int ExitCode, string Output, string Error);
