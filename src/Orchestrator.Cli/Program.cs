using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Cli.Scenarios;

namespace Orchestrator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var workspacePath = Environment.CurrentDirectory;
        var initRequested = false;
        var verifyRequested = false;
        string? scenarioName = null;

        // Parse CLI arguments
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                return 0;
            }

            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                workspacePath = Path.GetFullPath(args[++i]);
            }
            else if (arg.Equals("--init", StringComparison.OrdinalIgnoreCase))
            {
                initRequested = true;
            }
            else if (arg.Equals("--verify", StringComparison.OrdinalIgnoreCase))
            {
                verifyRequested = true;
            }
            else if (arg.Equals("--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                scenarioName = args[++i];
            }
        }

        // Handle direct reusable flags
        if (initRequested || verifyRequested)
        {
            var runner = new WorkspaceReusableRunner(workspacePath);
            if (initRequested)
            {
                var initExit = await runner.ScaffoldWorkspaceAsync();
                if (initExit != 0) return initExit;
            }

            if (verifyRequested)
            {
                return await runner.VerifyWorkspaceAsync();
            }

            return 0;
        }

        // Handle direct headless scenario execution
        if (!string.IsNullOrWhiteSpace(scenarioName))
        {
            return await ExecuteDirectScenarioAsync(scenarioName, workspacePath);
        }

        // Default: Interactive Zero-Config Evaluation Mode
        return await RunInteractiveEvaluationModeAsync(workspacePath);
    }

    private static async Task<int> RunInteractiveEvaluationModeAsync(string workspacePath)
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n" + new string('=', 75));
            Console.WriteLine("    ENTERPRISE AGENTIC SDLC ORCHESTRATOR & GOVERNANCE ENGINE");
            Console.WriteLine("    Controlled Autonomy | Spec-Driven Development | Non-Linear DAG");
            Console.WriteLine(new string('=', 75));
            Console.ResetColor();

            Console.WriteLine("  Interactive Zero-Config Evaluation Mode (No Cloud Keys Required):\n");
            Console.WriteLine("    [1] Greenfield Scenario   â€” Core URL Shortener MVP (US1)");
            Console.WriteLine("    [2] Brownfield Scenario   â€” Privacy Analytics & Revocation (US2)");
            Console.WriteLine("    [3] Ambiguous Scenario    â€” Intent Normalization & Dynamic Re-Planning (US3)");
            Console.WriteLine("    [4] Workspace Governance  â€” Scaffold specs/ or Run Pre-Push Verification");
            Console.WriteLine("    [5] Exit\n");

            Console.Write("  Select an option [1-5] > ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    var greenfield = new GreenfieldScenarioRunner(workspacePath);
                    await greenfield.RunAsync();
                    break;

                case "2":
                    var brownfield = new BrownfieldScenarioRunner(workspacePath);
                    await brownfield.RunAsync();
                    break;

                case "3":
                    var ambiguous = new AmbiguousScenarioRunner(workspacePath);
                    await ambiguous.RunAsync();
                    break;

                case "4":
                    await RunWorkspaceGovernanceMenuAsync(workspacePath);
                    break;

                case "5":
                case "exit":
                case "q":
                    Console.WriteLine("\nExiting Enterprise SDLC Orchestrator. Goodbye!");
                    return 0;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Invalid selection. Please choose 1, 2, 3, 4, or 5.");
                    Console.ResetColor();
                    break;
            }
        }
    }

    private static async Task RunWorkspaceGovernanceMenuAsync(string workspacePath)
    {
        Console.WriteLine("\n--- Workspace Governance Options ---");
        Console.WriteLine("  [1] Scaffold Standard 4-Artifact specs/ Directory (--init)");
        Console.WriteLine("  [2] Run Pre-Push Verification Gates (--verify)");
        Console.WriteLine("  [3] Return to Main Menu");
        Console.Write("Choice [1-3] > ");

        var choice = Console.ReadLine()?.Trim();
        var runner = new WorkspaceReusableRunner(workspacePath);

        switch (choice)
        {
            case "1":
                await runner.ScaffoldWorkspaceAsync();
                break;
            case "2":
                await runner.VerifyWorkspaceAsync();
                break;
            default:
                break;
        }
    }

    private static async Task<int> ExecuteDirectScenarioAsync(string scenario, string workspacePath)
    {
        return scenario.ToLowerInvariant() switch
        {
            "greenfield" or "us1" => await new GreenfieldScenarioRunner(workspacePath).RunAsync(),
            "brownfield" or "us2" => await new BrownfieldScenarioRunner(workspacePath).RunAsync(),
            "ambiguous" or "us3" => await new AmbiguousScenarioRunner(workspacePath).RunAsync(),
            _ => await HandleUnknownScenarioAsync(scenario)
        };
    }

    private static Task<int> HandleUnknownScenarioAsync(string scenario)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown scenario '{scenario}'. Valid options: greenfield, brownfield, ambiguous.");
        Console.ResetColor();
        return Task.FromResult(1);
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
Enterprise Agentic SDLC Orchestrator CLI (Orchestrator.Cli)
Enforces Controlled Autonomy, Spec-Driven Development (SDD), and Non-Linear DAG Execution.

USAGE:
  dotnet run --project src/Orchestrator.Cli [OPTIONS]

OPTIONS:
  --scenario <name>     Directly run a scenario: greenfield, brownfield, ambiguous
  --workspace <path>    Target directory for workspace operations (default: current directory)
  --init                Scaffold the standard 4-artifact specs/ directory in the workspace
  --verify              Run pre-push verification gates (dotnet build & test)
  --help, -h            Show this help and usage documentation

ZERO-CONFIG EVALUATION MODE:
  Run without arguments to launch the interactive terminal menu for evaluation.
");
    }
}
