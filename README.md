# Agentic Software Engineering System — URL Shortener

An enterprise-grade **Agentic Software Engineering System** prototype demonstrating autonomous, spec-driven development of a resilient URL Shortener service under strict human-in-the-loop governance.

Designed for high-reliability enterprise environments (such as financial services), this repository showcases an end-to-end multi-agent orchestration lifecycle with non-linear execution, policy guardrails, and audit-grade traceability.

---

## 🎯 Architectural Highlights

### 1. Spec-Driven Development (SDD) & Contract-First
* **Constitutional Governance**: All downstream agents are bounded by a non-negotiable `constitution.md` setting architectural, security, and quality thresholds.
* **Formal API Contracts**: Every capability is modeled and locked down in OpenAPI 3.1 specifications prior to code generation.
* **Automated Contract Verification**: Generated implementations are continuously validated against the specification to eliminate model drift.

### 2. Enterprise API Security
* **Anti-SSRF (Server-Side Request Forgery)**: Rejection of private IP ranges, loopback addresses (`127.0.0.1`, `localhost`), link-local metadata endpoints (`169.254.169.254`), and dangerous protocols (`javascript:`, `file:`).
* **Anti-Enumeration (ID Security)**: Cryptographically secure Base62 randomized keys preventing sequential dictionary scraping attacks.
* **Abuse & DoS Mitigation**: Built-in rate limiting and throttling returning standard HTTP `429 Too Many Requests`.
* **Zero-Trust Input Validation**: Multi-stage URI scheme validation and sanitization.

### 3. Controlled Autonomy (Human-in-the-Loop)
* **Explicit Entry/Exit Gates**:
  * **Gate 1**: Human sign-off on Requirements & OpenAPI Specification.
  * **Gate 2**: Human sign-off on Architectural Plan & Task Sequencing.
  * **Gate 3**: Human sign-off on Test Suite & Production Release Readiness.
* **Resilience & Safe Stops**: Bounded retry loops (max 3), automated test-failure rollbacks, and complete decision lineage logs.

---

## 📂 Repository Layout

```text
├── specs/
│   ├── constitution.md    # Non-negotiable security guardrails & SDLC rules
│   ├── spec.md            # OpenAPI 3.1 contract and data schemas
│   ├── plan.md            # System architecture, state machine & dependency graph
│   └── tasks.md           # Discrete, testable engineering tasks
├── src/
│   ├── UrlShortener.Api/  # Production C# / .NET 9 Minimal API service
│   └── Orchestrator.Cli/  # Runnable C# SDLC Workflow Orchestrator (State Machine)
├── tests/
│   ├── UrlShortener.UnitTests/        # Domain logic and hashing tests
│   ├── UrlShortener.ContractTests/    # OpenAPI schema validation tests
│   └── UrlShortener.SecurityTests/    # SSRF, rate limit, and abuse tests
└── README.md
```

---

## ⚡ Quick Start & Setup Instructions

Designed with **zero-friction reviewer onboarding**, this prototype can be built and evaluated out of the box using only the standard [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or .NET 8). No third-party paid API keys, cloud subscriptions, or Docker containers are required.

### 1. Clone the Repository
```bash
git clone https://github.com/vlj8008/agentic-url-shortener.git
cd agentic-url-shortener
```

### 2. Run Automated Test Suite
Verify that all unit, contract, and API security tests pass locally:
```bash
dotnet test
```

### 3. Run the Agentic Workflow Orchestrator
Execute the runnable C# orchestration CLI to interactively evaluate the SDLC scenarios:
```bash
dotnet run --project src/Orchestrator.Cli
```

---

## 🎛️ Dual-Mode Execution Architecture

The C# Orchestrator is architected with a decoupled provider pattern supporting two operational modes:

### Mode 1: Zero-Config Evaluation Mode (Default / Recommended for Reviewers)
* **Zero External Dependencies**: Operates 100% locally with zero external network calls or credentials required.
* **Stateful Simulation**: Executes the full non-linear state machine across all 3 scenarios (Greenfield, Brownfield, Ambiguous), runs real local `dotnet build` and `dotnet test` verification, pauses for interactive human approval gates (`[A]pprove`, `[R]eject`, `[S]afe-stop`), and demonstrates bounded self-correction retries (max 3) and rollbacks.
* **Audit-Grade Telemetry**: Records and displays decision lineage, MTTR (Mean Time to Recovery), and reliability metrics upon completion.

### Mode 2: Live AI Provider Mode (Optional)
* For live dynamic LLM generation, set your preferred provider key via environment variables:
  ```bash
  # Azure OpenAI
  set AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
  set AZURE_OPENAI_API_KEY=your_key_here

  # Or Google Gemini / OpenAI
  set GEMINI_API_KEY=your_gemini_key_here
  set OPENAI_API_KEY=your_openai_key_here
  ```
* When credentials are detected, the orchestrator dynamically routes prompts to live model endpoints using `Microsoft.Extensions.AI` abstractions.

---

## 🚀 SDLC Execution Scenarios

1. **Greenfield Execution**: End-to-end autonomous synthesis of the core URL shortening and redirection engine from scratch.
2. **Brownfield Execution**: Context-aware refactoring and feature enhancement on existing codebases without introducing regression.
3. **Ambiguous Requirements**: Automated ambiguity detection, intent normalization, and interactive human clarification before execution.

---

## 👩‍💻 Author

**Vicky Lee Jones**  
*C# .NET Solutions Developer & AI Agentic Systems Engineer*
