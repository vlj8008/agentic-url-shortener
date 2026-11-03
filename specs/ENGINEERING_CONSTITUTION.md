# Universal Engineering Constitution & Agent Governance Standards

**Master Location**: `C:\Users\vicky\source\repos\ENGINEERING_CONSTITUTION.md`  
**Repository Mirror**: `specs/ENGINEERING_CONSTITUTION.md`  
**Scope**: Universal baseline for all software engineering projects, C# / .NET solutions, and autonomous agent workflows.

---

## 🏛️ Purpose & Philosophy

This Constitution establishes the non-negotiable engineering principles, security baselines, and governance policies governing both human developers and autonomous AI agents. 

Our core philosophy is **Controlled Autonomy**: AI agents execute multi-step engineering tasks with precision and speed, while the human engineer retains absolute ownership, oversight, and final quality control.

---

## 📜 Core Constitutional Principles

### Principle 1: Transparent Agent Governance ("Explain Before Acting")
* **Zero Black-Box Output**: No AI agent shall generate or modify production code without first explaining its rationale in plain, accessible English.
* **Standard Proposal Structure**: Every agent proposal must include:
  1. **What**: The exact file, endpoint, class, or dependency being added or changed.
  2. **Why**: The architectural, business, or security justification.
  3. **How It Works**: A plain-English walkthrough of the underlying logic.
  4. **Interview Talking Point**: The enterprise-grade rationale suitable for high-level technical interviews.

### Principle 2: Human-in-the-Loop Checkpoints (Controlled Autonomy), Dependency Graphs & CI/CD Gates
* **Explicit Lifecycle Gates**: Every development workflow must enforce explicit approval checkpoints:
  * **Gate 1 (Specification Approval)**: Human signs off on requirements, resolved ambiguities, edge cases, and the OpenAPI contract before any code is generated.
  * **Gate 2 (Architecture & Task Approval)**: Human signs off on component design, dependency graph, entry/exit gates, and data flow.
  * **Gate 3 (Verification & Release Approval)**: Human reviews automated test runs, security scans, and code diffs before production merge.
* **Non-Linear Dependency Graphs & Dynamic Re-Planning**:
  * Tasks must be structured as an explicit dependency graph (defining sequential prerequisites vs. parallelizable tasks with synchronization barriers), rather than naive linear task chaining.
  * Every stage must enforce strict **Entry Gates** (preconditions required to start) and **Exit Gates** (validation criteria required to complete).
  * **Dynamic Re-Planning**: Whenever upstream requirements, specifications, or human feedback change, the system must dynamically re-plan downstream tasks while preserving cross-stage context and governance.
* **Bounded Retries, Automatic Rollback & Safe Stops**:
  * Prior to modifying code, agents must establish a clean pre-execution checkpoint (snapshot).
  * If automated build, test, or policy checks fail, agents are granted a maximum of **3 bounded self-correction retries**.
  * If checks still fail after 3 attempts, the orchestrator must automatically **roll back** the codebase to the pre-execution checkpoint, trigger a **Safe-Stop**, and escalate to the human engineer.
* **Pre-Push Verification Gates (Zero-Broken-Commits Rule)**:
  * Prior to executing any `git push` to a remote repository (such as GitHub), the agent must locally run and verify two mandatory criteria:
    1. **Clean Compilation**: The project must build with zero errors (`dotnet build`).
    2. **100% Passing Tests**: All automated unit, contract, and integration tests must run and pass with zero failures (`dotnet test`).
  * Pushing broken builds, compiler warnings-as-errors, or failing test suites to remote branches is strictly prohibited.
* **Automated CI/CD Enforcement & Pull Request Gates (GitHub Actions)**:
  * Every repository must maintain a GitHub Actions workflow (`.github/workflows/ci.yml`) that automatically builds the solution and executes the entire test suite on every `push` and `pull_request` to ensure continuous build verification.
  * Agents working on major features must use feature branches and Pull Requests, enabling GitHub Actions to validate code in an isolated cloud runner before the human engineer conducts final review and merges into `main`.

### Principle 3: Spec-Driven Development, Ambiguity Resolution & Brownfield Reasoning
* **Ambiguity Detection & Intent Normalization**:
  * Agents must never silently guess when requirements are vague, incomplete, or contradictory.
  * Before drafting a specification, the agent must identify ambiguities, explicitly document defensible assumptions, or pause to request human clarification.
* **Brownfield Codebase Reasoning (Impact Analysis)**:
  * Before modifying any existing ("brownfield") codebase, the agent must first analyze and document all impacted modules, services, APIs, database schemas, and data flows to prevent regressions.
* **Contract Precedes Code**: Specifications (e.g., OpenAPI 3.1, JSON schemas, protocol definitions) must be drafted, reviewed, and finalized before writing implementation logic.
* **Contract Testing**: Every endpoint must have corresponding automated contract tests validating that actual request/response payloads, status codes, and headers strictly adhere to the specification.
* **Drift Prevention**: Implementation code must never deviate from the specification without an explicit, approved update to the contract first.

### Principle 4: Zero-Trust Security, OWASP API Baselines, Supply Chain & Secrets Hygiene
* **Zero Hardcoded Secrets**: Absolute prohibition against committing passwords, API keys, connection strings, or private tokens to source control. All sensitive data must use environment variables or local user secrets (`secrets.json`).
* **Strict Boundary Validation & Injection Prevention**: Every external input must be validated, type-checked, and sanitized at the application perimeter before being passed to domain logic. Never concatenate raw strings into SQL or command queries; always use parameterized queries.
* **OWASP API1 & API5 (Least Privilege & Resource Ownership Authorization)**:
  * Never expose sequential database auto-increment integers (`/1`, `/2`, `/3`) in public API routes or responses, preventing attackers from scraping resources via ID enumeration. Always use non-predictable identifiers (GUIDs, ULIDs, or cryptographically random tokens).
  * Enforce strict separation between **Authentication** (identity verification) and **Authorization** (permission verification). Every protected endpoint must verify explicit resource ownership or role claims under the principle of **Least Privilege** before reading, mutating, or deleting data.
* **OWASP API3 (Anti-Mass Assignment / Strict DTO Binding)**:
  * Never bind incoming HTTP requests or outgoing HTTP responses directly to internal database entities. Always use dedicated, immutable Request/Response **Data Transfer Objects (DTOs)** so attackers cannot overwrite restricted internal properties (such as roles, permissions, or audit timestamps) or leak sensitive columns.
* **OWASP API7 (Anti-SSRF / Server-Side Request Forgery Protection)**:
  * Any endpoint accepting external URLs, callbacks, or webhooks must strictly enforce `https://` schemes, reject dangerous schemes (`javascript:`, `file:`, `data:`), and block private, loopback, and cloud metadata IP ranges (`127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.169.254`, `::1`).
* **OWASP API8 (Transport Security, Strict CORS & Payload Size Limits)**:
  * Enforce encrypted transport (`HTTPS` / `TLS` and `HSTS`) in production environments.
  * Never use permissive wildcard CORS (`AllowAnyOrigin`) on authenticated endpoints; restrict Cross-Origin Resource Sharing to explicitly trusted origins.
  * Enforce strict request payload size limits at the API perimeter to prevent memory exhaustion attacks from oversized JSON bodies.
* **Supply-Chain Security & Dependency Minimalism**:
  * Always prioritize built-in .NET Base Class Library (BCL) features before introducing third-party NuGet or npm packages.
  * Human approval is required before installing any new external dependency, and all dependencies must be verified free of known vulnerabilities (`dotnet list package --vulnerable`).
* **Standardized Security Responses**: Standard HTTP status codes must be strictly enforced:
  * `400 Bad Request` for malformed or unvalidated input.
  * `401 Unauthorized` for missing or invalid authentication credentials.
  * `403 Forbidden` for authenticated users lacking authorization for the resource.
  * `429 Too Many Requests` when rate limits or throttling thresholds are exceeded.
* **Defensive Resilience**: Implement rate limiting, timeout handling, circuit breakers, and idempotency keys to protect services against distributed denial-of-service (DDoS) and cascade failures.

### Principle 5: Production-Grade Code Quality, SOLID & Testing Rigor
* **Mandatory Adherence to SOLID Principles**:
  * **Single Responsibility (SRP)**: Each class, service, and handler must have one, and only one, reason to change. Strictly decouple HTTP transport, input validation, core business logic, and persistence.
  * **Open/Closed (OCP)**: Modules must be open for extension but closed for modification. Leverage interfaces, strategy patterns, and extension points rather than cascading conditional blocks.
  * **Liskov Substitution (LSP)**: Derived types and implementations must be fully substitutable for their base abstractions without altering system correctness.
  * **Interface Segregation (ISP)**: Prefer fine-grained, cohesive, client-specific interfaces over bloated, monolithic interfaces.
  * **Dependency Inversion (DIP)**: High-level modules must never instantiate concrete dependencies directly. Always program to abstractions and inject dependencies via ASP.NET Core Dependency Injection.
* **Clean & Modular Architecture**: Code must follow clean separation of concerns (Presentation / API $\rightarrow$ Application / Domain $\rightarrow$ Infrastructure / Data).
* **Defensible Simplicity**: Avoid over-engineering. Prefer clean, modern, native framework features (e.g., ASP.NET Core Minimal APIs) over unnecessary third-party dependencies.
* **Testing Pyramid Threshold**:
  * **Unit Tests**: Fast, isolated tests covering business rules and edge cases.
  * **Integration / Contract Tests**: Validating database interactions, HTTP status codes, and schema adherence.
  * **Coverage Target**: Minimum **80% automated test coverage** across domain and core API endpoints.
* **Observability, Correlation IDs & Privacy-Safe Auditability**:
  * Implement structured logging (e.g., Serilog / OpenTelemetry) ensuring no Personally Identifiable Information (PII) or sensitive tokens are exposed in logs.
  * Attach a unique **Correlation ID (`TraceId`)** to every incoming HTTP request, propagate it across log entries, and return it in error responses (`ProblemDetails`) so production issues can be traced end-to-end without logging customer PII.

### Principle 6: Modern .NET Architecture, API Evolution & Resiliency Standards
* **Non-Blocking Asynchronous I/O**:
  * All I/O-bound operations (database queries, network calls, cache access) must use `async` / `await`. Synchronous blocking calls (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) are strictly prohibited to prevent thread pool starvation.
  * Every asynchronous service and endpoint method must accept and propagate a `CancellationToken` to avoid wasted compute when client connections drop.
* **Standardized RFC 7807 / 9457 Problem Details**:
  * Unhandled internal exceptions must never leak stack traces, SQL syntax, or internal framework details to external clients (OWASP Information Disclosure prevention).
  * All error responses must conform to the RFC 7807 `ProblemDetails` standard schema (`type`, `title`, `status`, `detail`, `instance`).
* **Null Safety & Immutability**:
  * Projects must enable `<Nullable>enable</Nullable>` to eliminate `NullReferenceException` bugs at compile time.
  * Use immutable C# `record` types with `init`-only properties for Data Transfer Objects (DTOs), requests, and responses to prevent accidental state mutation during transport.
* **API Versioning & Unbounded Resource Protection**:
  * Public APIs must include explicit versioning from inception (e.g., `/api/v1/...`) to guarantee backward compatibility.
  * Collection endpoints must never return unbounded lists. Mandatory pagination (`pageNumber` / `pageSize` or cursor pagination with strict upper bounds) is required to prevent memory exhaustion (OWASP API4:2023 Unrestricted Resource Consumption).
* **Cloud-Native Health Checks**:
  * Services must expose standard `/health` readiness and liveness endpoints (via ASP.NET Core Health Checks) to verify application and database connectivity.

### Principle 7: Data Access Hygiene, Git Standards, Telemetry & Decision Lineage (ADRs)
* **Database Performance & Safety**:
  * Read-only Entity Framework Core queries must use `.AsNoTracking()` to avoid unnecessary change-tracking memory overhead.
  * Avoid `N+1` query traps and always use version-controlled, backward-compatible database migrations.
* **Atomic & Conventional Commits**:
  * Commits must be small, atomic, and follow Conventional Commits (`feat:`, `fix:`, `security:`, `refactor:`, `test:`, `docs:`) so any single change can be cleanly audited or rolled back without side effects.
* **Architectural Decision Records (ADRs) & Final Engineering Summaries**:
  * Whenever a significant architectural or technical trade-off is made, the agent must record the **Context, Options Considered, Trade-offs, and Rationale** so decision lineage is preserved for audits and engineering reviews.
  * Every completed feature or workflow must produce a **Final Engineering Summary** documenting the implementation rationale, produced artifacts, risks/trade-offs, validation results, assumptions, and known limitations.
* **Agentic Observability & Reliability Telemetry**:
  * Orchestrated workflows must provide audit-grade traceability and track core reliability metrics across every run: **Task Success Rate**, **Retry & Rollback Frequency**, **Mean Time To Recovery (MTTR)** when self-correcting failed tests, and **End-to-End Stage Latency**.

---

## 🔄 Reusability Across Projects

Every new project created in `C:\Users\vicky\source\repos\` automatically inherits this Constitution. Project-specific requirements (such as specialized cryptographic algorithms, domain entities, or specific cloud infrastructure) are documented in the project's local [`specs/constitution.md`](./constitution.md) as an addendum to this master document.
