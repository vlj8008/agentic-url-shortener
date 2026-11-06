# Enterprise Agentic URL Shortener & SDLC Orchestrator

[![CI/CD Pipeline](https://github.com/vlj8008/agentic-url-shortener/actions/workflows/ci.yml/badge.svg)](https://github.com/vlj8008/agentic-url-shortener/actions/workflows/ci.yml)
[![Target Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/Tests-190%20Passed-brightgreen.svg)]()
[![Code Coverage](https://img.shields.io/badge/Coverage-≥80%25-success.svg)]()
[![TreatWarningsAsErrors](https://img.shields.io/badge/Warnings-0%20Errors%20%7C%200%20Warnings-blue.svg)]()
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

An enterprise-grade **Agentic Software Engineering System** and **Resilient URL Shortener** built with C# and .NET 10. Designed for mission-critical enterprise environments (such as financial services and high-compliance payment systems), this repository demonstrates end-to-end multi-agent orchestration, non-linear Directed Acyclic Graph (DAG) task execution, and automated verification under strict **Controlled Autonomy (Human-in-the-Loop)** governance.

---

## 🏛️ Executive Architecture Overview

The system is composed of two primary modules decoupled by clean architectural boundaries:

```
agentic-url-shortener/
├── src/
│   ├── UrlShortener.Api/         # High-throughput, resilient URL Shortener web service
│   │   ├── Domain/               # ShortUrl & ClickEvent entities, immutable DTO records
│   │   ├── Services/             # Base62 generator, Anti-SSRF validator, IP privacy hasher
│   │   ├── Infrastructure/       # EF Core DbContext, repository, Channel<ClickEvent> background worker
│   │   └── Endpoints/            # Minimal API route definitions & RFC 7807 error handling
│   └── Orchestrator.Cli/         # Standalone, domain-agnostic C# SDLC orchestration engine
│       ├── Engine/               # SdlcStateMachine, DependencyGraphExecutor, GitSnapshotManager, TelemetryTracker
│       ├── Agents/               # RequirementNormalizer, BrownfieldAnalyzer, TaskDecomposer, VerificationAndRollback
│       ├── Scenarios/            # Greenfield, Brownfield, Ambiguous, and WorkspaceReusable runners
│       └── Program.cs            # Zero-Config interactive evaluation CLI & workspace governance flags
├── tests/
│   ├── UrlShortener.UnitTests/   # Domain logic, Base62, and Orchestrator state machine tests (108 tests)
│   ├── UrlShortener.ContractTests/ # OpenAPI 3.1 schema and RFC 7807 contract compliance tests (13 tests)
│   └── UrlShortener.SecurityTests/ # Anti-SSRF perimeter, 429 rate limit, and alias collision tests (69 tests)
├── specs/                        # Spec-Driven Development (SDD) 4-artifact governance suite
│   ├── constitution.md           # Domain-specific security rules & architectural budgets
│   ├── spec.md                   # OpenAPI 3.1 executable contract & user stories
│   ├── plan.md                   # System architecture & Architectural Decision Records (ADRs)
│   └── tasks.md                  # Non-linear DAG task matrix with entry/exit barriers
├── Directory.Build.props         # Enforces Nullable, TreatWarningsAsErrors, and latest C# features
└── AgenticUrlShortener.sln       # Root solution uniting all source and test projects
```

---

## 📜 Architectural Decision Records (ADRs)

| ADR ID | Decision Summary | Rationale & Trade-Off |
| :--- | :--- | :--- |
| **`ADR-001`** | **Cryptographic Base62 7-Character Random Generation** vs. Sequential Auto-Increment IDs | Eliminates sequential ID enumeration attacks (**OWASP API1:2023 Broken Object Level Authorization**). Naive integer encoding allows attackers to scrape all active links; a 7-character CSPRNG Base62 token provides $62^7 \approx 3.52 \times 10^{12}$ combinations with bounded 3-attempt collision retry loops. |
| **`ADR-002`** | **HTTP `302 Found` (`Cache-Control: no-store`)** vs. `301 Moved Permanently` | `301 Moved Permanently` caches redirects permanently on client browsers, which completely blinds click analytics and prevents emergency revocation. `302 Found` with `Cache-Control: no-store, no-cache` guarantees every click hits the server for real-time compliance checks while serving hot lookups from `IMemoryCache` in $<50\text{ms}$. |
| **`ADR-003`** | **Non-Blocking `Channel<ClickEvent>` Background Service** vs. Synchronous DB Writes | Synchronous database persistence inside redirect routes adds disk I/O contention and risks violating the sub-50ms p99 latency budget. Using a high-speed bounded `Channel<ClickEvent>` allows `GET /{shortCode}` to enqueue analytics events in $<1\text{ms}$ while `ClickAnalyticsBackgroundService` persists events asynchronously. |
| **`ADR-004`** | **Non-Linear State Machine & Parallel Wave DAG** vs. Naive Linear Chaining | Naive linear chaining fails to parallelize independent tasks and cannot dynamically re-plan upon human rejection. Our state machine executes independent tasks in concurrent waves, synchronizes at strict compiler/test barriers, and supports dynamic downstream re-planning upon gate rejection. |

---

## 🛡️ Enterprise Security & OWASP API Perimeter

1. **Anti-SSRF (Server-Side Request Forgery) Perimeter (`OWASP API7:2023`)**:
   - Rejects non-HTTPS schemes (`http:`, `javascript:`, `file:`, `data:`).
   - Validates hostname syntax and resolves DNS targets to verify destination IP addresses do not route to loopback (`127.0.0.0/8`, `::1`), RFC 1918 private subnets (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), or link-local cloud metadata endpoints (`169.254.169.254`).
   - Prevents self-referencing redirection loops.

2. **Abuse & Denial-of-Service Mitigation (`OWASP API4:2023`)**:
   - Built-in ASP.NET Core `PartitionedRateLimiter` with fixed-window policies:
     - **URL Creation**: 10 requests per minute per IP.
     - **Redirection**: 100 requests per minute per IP.
   - Returns standard HTTP `429 Too Many Requests` with a `Retry-After: 60` header and RFC 7807 `ProblemDetails`.

3. **Custom Alias & Expiration Guardrails**:
   - Validates custom alias length and characters using regex: `^[a-zA-Z0-9_-]{4,32}$`.
   - Reserved-word blocklist: `api`, `health`, `swagger`, `admin`, `shorten`, `metric`.
   - Returns HTTP `409 Conflict` on alias collisions.
   - Enforces future UTC expiration timestamps (`400 Bad Request` on past dates; `410 Gone` on expired or revoked links).

4. **Privacy-Preserving Analytics (GDPR / CCPA Safe)**:
   - Client IP addresses are never stored in plaintext.
   - Hashed using **HMAC SHA-256** with an application-level salt before persistence, producing an audit-safe, 64-character pseudonymized string for unique visitor tracking.

5. **Standardized RFC 7807 Error Handling**:
   - Every exception and error response returns a structured `ProblemDetails` payload including status code, title, detail, request instance, and a correlation `traceId`.

---

## 🤖 Standalone Agentic SDLC Orchestrator CLI (`Orchestrator.Cli`)

The solution includes a standalone, domain-agnostic C# Orchestrator CLI implementing **Controlled Autonomy**:

```mermaid
flowchart TD
    PreFlight["Pre-Flight Sync"] --> Clarify["Stage 1: Clarify (RequirementNormalizerAgent)"]
    Clarify --> Gate1{"Checkpoint: Gate 1 (Specification Approval)"}
    Gate1 -- "[A]pprove" --> Analyze["Stage 2: Analyze (BrownfieldAnalyzerAgent)"]
    Gate1 -- "[R]eject" --> Clarify
    Gate1 -- "[S]afe-Stop" --> SafeStopped["Safe-Stopped"]
    Analyze --> Plan["Stage 3: Plan & Decompose (TaskDecomposerAgent)"]
    Plan --> Gate2{"Checkpoint: Gate 2 (Architecture & Task Approval)"}
    Gate2 -- "[A]pprove" --> Snapshot["Stage 4: Pre-Execution Git Snapshot"]
    Gate2 -- "[R]eject" --> Plan
    Gate2 -- "[S]afe-Stop" --> SafeStopped
    Snapshot --> Execute["Stage 5: Parallel DAG Wave Execution"]
    Execute --> Verify["Stage 6: Automated Verification (VerificationAndRollbackAgent)"]
    Verify -- "Pass" --> Gate3{"Checkpoint: Gate 3 (Release Approval)"}
    Verify -- "Fail (<= 3 retries)" --> Execute
    Verify -- "Fail (> 3 retries)" --> Rollback["Automatic Git Rollback"] --> SafeStopped
    Gate3 -- "[A]pprove" --> Completed["Stage 7: Release Completed & Audit Telemetry"]
    Gate3 -- "[R]eject" --> Plan
    Gate3 -- "[S]afe-Stop" --> SafeStopped
```

### Scenario Walkthroughs

1. **Greenfield Scenario (`US1` — Priority P1)**:
   - Autonomous synthesis of the Core URL Shortener MVP from raw intent.
   - Normalizes requirements into `FR-001`–`FR-006`, prompts for Gate 1 approval, plans a 5-wave non-linear task DAG, creates a pre-execution snapshot, executes parallel tasks, runs automated verification, and secures Gate 3 sign-off.

2. **Brownfield Scenario (`US2` — Priority P2)**:
   - Context-aware enhancement of the existing codebase.
   - Uses `BrownfieldAnalyzerAgent` to scan C# source code, calculate blast radius, identify affected endpoints (`POST /api/v1/urls`, `GET /{shortCode}`) and files (`UrlShortenerService.cs`, `UrlEndpoints.cs`), and execute backward-compatible analytics without schema breakage.

3. **Ambiguous Requirements Scenario (`US3` — Priority P2)**:
   - Ingests a vague stakeholder prompt (*"Let users pick custom names, track clicks, and prevent spam"*).
   - `RequirementNormalizerAgent` detects missing boundary rules (alias length regex, reserved keywords, collision handling, rate limit thresholds, expiration semantics), synthesizes normalized requirements, prompts for human clarification, and demonstrates dynamic downstream re-planning on `[R]eject`.

4. **Workspace Reusable Runner (`US4` — Domain-Agnostic CLI Flags)**:
   - `--workspace <path>`: Targets any repository in the local workspace.
   - `--init`: Scaffolds the standard 4-artifact Spec-Driven Development directory (`specs/constitution.md`, `specs/spec.md`, `specs/plan.md`, `specs/tasks.md`).
   - `--verify`: Executes pre-push verification gates (`dotnet build` and `dotnet test`) enforcing the **Zero-Broken-Commits Rule**.

---

## 📊 Automated Verification & Quality Matrix

| Test Suite | Focus Area | Total Tests | Passed | Failed | Duration | Coverage |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: |
| **`UrlShortener.UnitTests`** | Base62 tokenization, IP privacy hashing, SDLC state machine, DAG cycle detection, dynamic re-planning, bounded retries ($\le 3$), MTTR telemetry | 108 | 108 | 0 | 104 ms | $\ge 80\%$ |
| **`UrlShortener.ContractTests`** | OpenAPI 3.1 compliance, status codes (`201`, `302`, `200`, `204`, `404`, `410`), RFC 7807 ProblemDetails schemas, response headers | 13 | 13 | 0 | 1.0 s | 100% |
| **`UrlShortener.SecurityTests`** | Anti-SSRF perimeter (loopback, private subnets, cloud metadata `169.254.169.254`), rate limiting (`429`), alias collisions (`409`), link expiration | 69 | 69 | 0 | 1.0 s | 100% |
| **Total** | **Full Solution Verification** | **190** | **190** | **0** | **~2.1 s** | **$\ge 80\%$** |

### Code Coverage Audit Summary (`SC-002`)
- **Core Domain & Services**:
  - `UrlShortenerService`: **93.3%** line coverage
  - `CachedUrlMapping`: **100.0%** line coverage
  - `ResolveDestinationAsync`: **85.2%** line coverage
  - `RevokeAsync`: **85.7%** line coverage
  - `GetMetadataAsync`: **90.0%** line coverage
- **Security & Cryptography**:
  - `Base62CodeGenerator`: **100.0%** line coverage
  - `IpPrivacyHasher` (SHA-256): **100.0%** line coverage
  - `UrlSecurityValidator` (Anti-SSRF): **77.0%–100.0%** line coverage
  - `UrlValidationResult`: **90.9%** line coverage
- **Infrastructure & Middleware**:
  - `EfUrlRepository`: **100.0%** line coverage
  - `GlobalProblemDetailsExceptionHandler`: **100.0%** line coverage
  - `ShortUrl` & `ClickEvent` Entities: **87.5%** line coverage
  - Contract DTOs: **100.0%** line coverage
- **Orchestrator Engine**:
  - `DependencyGraphExecutor`: **91.5%** line coverage
  - `TelemetryTracker`: **100.0%** line coverage
  - `TaskNode` & `ExecutionWave`: **100.0%** line coverage
  - `SdlcStateMachine`: **77.6%–93.3%** line coverage

---

## ⚡ Quick Start & Reviewer Evaluation Guide

Designed for **zero-friction reviewer onboarding**, this prototype requires only the standard [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). No external API keys, paid cloud accounts, or Docker containers are required.

### 1. Build the Entire Solution
```bash
dotnet build AgenticUrlShortener.sln
```
*Builds cleanly with **0 errors and 0 warnings** under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.*

### 2. Run the Full Test Suite
```bash
dotnet test AgenticUrlShortener.sln
```
*Executes all 190 unit, contract, and security tests in ~2.1 seconds.*

### 3. Launch the Interactive Orchestrator CLI
```bash
dotnet run --project src/Orchestrator.Cli
```
Presents the interactive **Zero-Config Evaluation Menu**:
```text
===========================================================================
    ENTERPRISE AGENTIC SDLC ORCHESTRATOR & GOVERNANCE ENGINE
    Controlled Autonomy | Spec-Driven Development | Non-Linear DAG
===========================================================================
  Interactive Zero-Config Evaluation Mode (No Cloud Keys Required):

    [1] Greenfield Scenario   — Core URL Shortener MVP (US1)
    [2] Brownfield Scenario   — Privacy Analytics & Revocation (US2)
    [3] Ambiguous Scenario    — Intent Normalization & Dynamic Re-Planning (US3)
    [4] Workspace Governance  — Scaffold specs/ or Run Pre-Push Verification
    [5] Exit
```

### 4. CLI Reusable Governance Flags
```bash
# Scaffold standard Spec-Driven Development specs/ directory in any workspace
dotnet run --project src/Orchestrator.Cli -- --init --workspace "C:\path\to\repo"

# Run pre-push verification gates (clean build + 100% passing tests)
dotnet run --project src/Orchestrator.Cli -- --verify --workspace "C:\path\to\repo"

# Execute a scenario directly in headless mode
dotnet run --project src/Orchestrator.Cli -- --scenario greenfield
```

---

## ⚖️ Assumptions, Limitations & Production Evolution

| Domain | Current Local Implementation | Enterprise Production Architecture |
| :--- | :--- | :--- |
| **Data Persistence** | EF Core with SQLite / InMemory provider for zero-friction evaluation. | Managed PostgreSQL / Azure SQL with read replicas, partitioned `ClickEvents` tables, and automatic row-level archiving. |
| **Caching Layer** | ASP.NET Core `IMemoryCache` (in-process) for sub-50ms lookups. | Distributed Redis cluster with multi-region replication and Redis invalidation on link revocation (`DELETE`). |
| **Rate Limiting** | In-process `PartitionedRateLimiter` partitioned by client IP. | Distributed token bucket via API Gateway (Azure API Management / AWS API Gateway) or Redis rate limiting. |
| **Analytics Ingestion** | Local in-process `Channel<ClickEvent>` background queue. | High-throughput streaming broker (Apache Kafka or AWS Kinesis) feeding a real-time analytics warehouse (Snowflake / BigQuery). |

---

## 👩‍💻 Author

**Vicky Lee Jones**  
*C# .NET Solutions Developer & AI Agentic Systems Engineer*  
*Specializing in High-Reliability Enterprise Systems, Spec-Driven Development & Controlled Autonomous Agent Orchestration*
