using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Cli.Agents;

/// <summary>
/// Common contract for specialized SDLC pipeline agent stages (specs/tasks.md T026, specs/plan.md ADR-004).
/// Enforces Principle 1 (Transparent Agent Governance) across discrete execution stages.
/// </summary>
public interface IAgentStage<in TInput, TOutput>
{
    string Name { get; }

    Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}
