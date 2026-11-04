# Domain Constitution Addendum — Enterprise URL Shortener

**Parent Constitution**: Inherits all 7 Universal Principles from [`specs/ENGINEERING_CONSTITUTION.md`](./ENGINEERING_CONSTITUTION.md)  
**Domain Scope**: URL Shortener Service (`UrlShortener.Api`) & Agentic SDLC Orchestrator (`Orchestrator.Cli`)

---

## 🔗 Constitutional Inheritance

This project is governed by a **Two-Tiered Constitutional Architecture**:
1. **Layer 1 — Universal Engineering & Agent Governance**: Defined in [`ENGINEERING_CONSTITUTION.md`](./ENGINEERING_CONSTITUTION.md), enforcing transparent agent explanations, human-in-the-loop checkpoints, non-linear dependency graphs, bounded retries (max 3) with automatic rollback, OWASP API Security Top 10 baselines, SOLID architecture, and modern .NET resiliency.
2. **Layer 2 — URL Shortener Domain Rules (This Document)**: Defines the specific cryptographic, redirection, rate-limiting, latency, and privacy rules required for an enterprise financial-grade URL shortening service.

---

## 🛡️ Domain-Specific Rules & Guardrails

### Domain Rule 1: Cryptographic Base62 Short Code Generation (Anti-Enumeration)
* **Alphabet**: Short codes must use the standard Base62 character set (`0-9`, `a-z`, `A-Z`) to remain URL-safe without percent-encoding.
* **Key Length & Entropy**: Default generated short codes must be **7 characters** in length ($62^7 \approx 3.52 \times 10^{12}$ unique combinations), generated via `System.Security.Cryptography.RandomNumberGenerator` rather than predictable `System.Random` or sequential database IDs.
* **Collision Resolution**: If a generated short code collides with an existing active record, the service must retry generation up to a bounded limit of **3 attempts** before returning a standardized `503 Service Unavailable` `ProblemDetails` response.
* **Custom Aliases**: Optional user-supplied custom aliases must be validated against a strict regex (`^[a-zA-Z0-9_-]{4,32}$`) and checked against a reserved-word blocklist (`api`, `health`, `admin`, `swagger`, `metrics`). If a requested custom alias already exists, the service must immediately return `409 Conflict` with an RFC 7807 `ProblemDetails` response rather than retrying random generation.

### Domain Rule 2: Destination URL Validation & Anti-SSRF Enforcement
* **Scheme Whitelist**: Destination URLs must use `https://` in production (with `http://` configurable only for local development/testing environments). Dangerous schemes (`javascript:`, `data:`, `file:`, `vbscript:`, `ftp:`) must be rejected immediately with `400 Bad Request`.
* **Maximum Length**: Destination URLs must not exceed **2,048 characters**.
* **Internal Network & Metadata Blocking (Anti-SSRF)**:
  * Destination hostnames must never resolve to loopback (`localhost`, `127.0.0.0/8`, `::1`), private RFC 1918 networks (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), or cloud link-local metadata endpoints (`169.254.169.254`).
  * Self-referencing loops (shortening a URL that already points to the URL shortener's own domain) must be blocked to prevent infinite redirect loops.

### Domain Rule 3: HTTP Redirection Semantics (`302 Found` vs. `301 Moved Permanently`)
* **Default Status Code (`302 Found`)**: Redirection endpoints (`GET /{shortCode}`) must return HTTP `302 Found` (Temporary Redirect) with `Cache-Control: no-store, no-cache` headers by default.
* **Architectural Rationale**: Returning `301 Moved Permanently` causes client browsers to permanently cache the redirect locally, bypassing our server on subsequent clicks and blinding enterprise click analytics and link revocation controls.
* **Expired or Disabled Links**: Links past their `ExpiresAt` UTC timestamp or marked inactive must return `410 Gone` (or `404 Not Found` if non-existent) using RFC 7807 `ProblemDetails`.

### Domain Rule 4: Endpoint Rate Limiting & Abuse Mitigation
* **Creation Endpoint (`POST /api/v1/urls`)**: Restricted to **10 requests per minute per client IP / API Key** (Sliding or Fixed Window).
* **Redirection Endpoint (`GET /{shortCode}`)**: Restricted to **100 requests per minute per client IP** to allow high read throughput while preventing denial-of-service floods.
* **Throttling Contract**: Exceeding rate limits must return HTTP `429 Too Many Requests`, a `Retry-After` header (in seconds), and an RFC 7807 `ProblemDetails` body.

### Domain Rule 5: Sub-50ms Redirect Latency & Privacy-Safe Asynchronous Analytics
* **Performance Budget**: Redirection (`GET /{shortCode}`) must target **sub-50ms p99 latency** by caching active short-code mappings in memory (`IMemoryCache`) and never blocking the HTTP redirect response on analytics database writes.
* **Non-Blocking Click Tracking**: Click events must be published to an in-memory bounded channel (`System.Threading.Channels.Channel<ClickEvent>`) or processed asynchronously so database latency never slows down user redirection.
* **Privacy & Compliance**: Client IP addresses must never be stored in raw plain text in analytics tables; they must be hashed (SHA-256 with salt) or truncated to preserve user privacy while still enabling unique-visitor estimation.

### Domain Rule 6: Reusable & Domain-Agnostic Orchestration Engine (`Orchestrator.Cli`)
* **Decoupled Architecture**: The core orchestration state machine, human approval gates (`[A]pprove / [R]eject / [S]afe-stop`), pre-execution Git snapshot/rollback manager, bounded retry runner ($\le 3$ attempts on `dotnet build`/`dotnet test` failures), and telemetry logger must be decoupled from the URL Shortener domain via clean abstractions and dependency injection.
* **Dual Purpose (Evaluation + Permanent Reusability)**:
  * **Default Mode**: When executed with default arguments (`dotnet run --project src/Orchestrator.Cli`), runs the three required SDLC evaluation scenarios (Greenfield, Brownfield, Ambiguous) out of the box with zero external dependencies.
  * **Portable Tooling Mode**: The CLI must support reusable commands and flags (such as `--workspace <path>`, `--init`, and `--verify`) allowing engineers to reuse the orchestrator across any greenfield or brownfield software repository and enterprise modernization workflow.

---

## 🚦 Project SDLC Orchestration Gates

In accordance with Principle 2 of [`ENGINEERING_CONSTITUTION.md`](./ENGINEERING_CONSTITUTION.md), the C# Agentic Orchestrator (`Orchestrator.Cli`) enforces three mandatory human checkpoints across all three required scenarios (**Greenfield**, **Brownfield**, and **Ambiguous**):

| Gate | SDLC Stage | Entry Criteria | Exit Criteria | Human Action Required |
| :--- | :--- | :--- | :--- | :--- |
| **Gate 1** | **Requirements & Specification** | Raw requirement provided; ambiguities identified. | `specs/spec.md` (OpenAPI 3.1) drafted; security rules verified against Constitution. | Human reviews resolved ambiguities & signs off on API contract (`[A]pprove / [R]eject / [S]afe-Stop`). |
| **Gate 2** | **Architecture & Task Decomposition** | Gate 1 approved; brownfield impact analysis completed (if applicable). | `specs/plan.md` & `specs/tasks.md` generated with explicit dependency graph & parallel/sequential paths. | Human reviews architectural trade-offs (ADRs) & approves task execution graph. |
| **Gate 3** | **Implementation, Verification & Release** | Gate 2 approved; pre-execution snapshot created; code & tests generated. | `dotnet build` (0 errors) and `dotnet test` (100% pass) verified within $\le 3$ bounded retries; reliability metrics logged. | Human reviews test report, telemetry metrics (MTTR, latency), and approves final release. |
