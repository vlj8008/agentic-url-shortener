# Feature Specification: Enterprise Agentic URL Shortener & SDLC Orchestrator

**Feature Branch**: `spec/spec-md`  
**Created**: 2026-11-03  
**Status**: Draft (Pending Gate 1 Sign-Off)  
**Input Source**: Charles Schwab Interview Assignment (`Build an Agentic Software Engineering System — URL Shortener`)  
**Parent Constitutions**:
- Universal Engineering Constitution: [`specs/ENGINEERING_CONSTITUTION.md`](./ENGINEERING_CONSTITUTION.md)
- Domain Constitution Addendum: [`specs/constitution.md`](./constitution.md)

---

## 🎯 Executive Summary & Resolved Ambiguities

This specification defines the functional requirements, user scenarios, edge cases, measurable success criteria, and formal OpenAPI 3.1 contract for the **Enterprise URL Shortener Web Service (`UrlShortener.Api`)** and its governing **Autonomous Agentic SDLC Orchestrator (`Orchestrator.Cli`)**.

In accordance with GitHub Spec Kit's `/speckit.clarify` phase and Principle 3 of `ENGINEERING_CONSTITUTION.md`, all vague and underspecified aspects of the raw assignment prompt have been interrogated and normalized into strict engineering rules below:

### Resolved Ambiguities Table

| Vague Prompt / Ambiguity | Identified Risk / Edge Case | Normalized Engineering Contract |
| :--- | :--- | :--- |
| *"Allow custom short names for links"* | Users could pick 1-character names, 500-character names, special characters, or system reserved words (`admin`, `health`, `api`), or request a name already claimed. | **Strict Regex & Blocklist**: Must match `^[a-zA-Z0-9_-]{4,32}$`. Reserved names (`api`, `health`, `admin`, `swagger`, `metrics`) are blocked with `400 Bad Request`. If the alias is already taken by another link, return **`409 Conflict`** immediately (no retries). |
| *"Redirect users when clicking a link"* | Returning permanent redirect (`301 Moved Permanently`) causes browsers to cache the destination indefinitely, bypassing our server on subsequent clicks and blinding analytics and link revocation. | **HTTP Redirection Semantics**: Must return **`302 Found`** with `Cache-Control: no-store, no-cache` headers so every click passes through the server for accurate tracking and instantaneous link revocation. |
| *"Track analytics on short links"* | Recording raw client IP addresses violates GDPR/CCPA privacy standards and enterprise compliance baselines. Blocking HTTP redirect response on database writes adds latency. | **Privacy-Safe & Asynchronous Analytics**: Client IP addresses are hashed using SHA-256 with a daily salt (never stored in plain text). Redirection publishes an event to an in-memory bounded channel (`System.Threading.Channels.Channel<ClickEvent>`) so database writes never slow down redirection latency. |
| *"Support link expiration and removal"* | Deleting records from the database permanently breaks audit lineage. Users clicking expired links receive confusing `404 Not Found`. | **Soft-Revocation & Distinct Status Codes**: Revocation sets `isActive = false` (`DELETE /api/v1/urls/{shortCode}` returns `204 No Content`). Links that are expired (`expiresAt < utcNow`) or revoked return **`410 Gone`**. Unknown links return **`404 Not Found`**. |
| *"Mitigate abuse and denial-of-service"* | Malicious scripts could flood the link creator or redirection endpoint, exhausting memory or database connections. | **Per-Client Rate Limiting**: `POST /api/v1/urls` is limited to **10 requests/min per IP**. `GET /{shortCode}` is limited to **100 requests/min per IP**. Exceeding limits returns **`429 Too Many Requests`** with a `Retry-After: 60` header and RFC 7807 `ProblemDetails`. |
| *"Block malicious or internal URLs (Anti-SSRF)"* | Attackers could submit links pointing to loopback (`127.0.0.1`), private networks (`10.0.0.0/8`), cloud metadata endpoints (`169.254.169.254`), or self-referencing loops to attack internal infrastructure. | **Multi-Stage URI Validation**: Scheme must be `https://` (with `http://` permitted only in local development). Rejects loopback (`127.0.0.0/8`, `::1`, `localhost`), RFC 1918 private ranges, AWS/Azure/GCP metadata (`169.254.169.254`), dangerous protocols (`javascript:`, `file:`, `data:`), and self-referencing shortener domains. |

---

## 👥 User Scenarios & Acceptance Criteria

### User Story 1 — Greenfield: Core URL Shortening, Anti-SSRF & Redirection (Priority: P1)

*As an enterprise user or integrated client service,*  
*I want to submit a long HTTPS destination URL and receive a cryptographically random 7-character short link that quickly redirects visitors,*  
*So that I can share clean links while protecting the organization against malicious web addresses and server-side request forgery (SSRF).*

**Why this priority**: Forms the core Minimum Viable Product (MVP) of the URL Shortener service. Without this, no shortener capability exists.

**Independent Testability**: Can be fully verified by sending a valid `POST /api/v1/urls` request, confirming the returned `201 Created` payload with short code, and executing `GET /{shortCode}` to verify a sub-50ms `302 Found` redirection.

**Acceptance Scenarios**:
1. **Given** a valid, public destination URL (`https://www.schwab.com/investment-portfolio`),  
   **When** a client sends `POST /api/v1/urls` with this destination,  
   **Then** the service returns HTTP `201 Created` with a `Location` header pointing to `/api/v1/urls/{shortCode}`, a 7-character Base62 `shortCode`, the full `shortUrl`, and UTC timestamps for `createdAt`.
2. **Given** an invalid or dangerous destination URL (e.g., `javascript:alert(1)`, `http://169.254.169.254/latest/meta-data`, `http://localhost:8080`, or a non-HTTPS scheme in production),  
   **When** a client submits it to `POST /api/v1/urls`,  
   **Then** the service blocks the request immediately, returning HTTP `400 Bad Request` with an RFC 7807 `ProblemDetails` response identifying the exact validation failure.
3. **Given** an existing active short code (e.g., `aB3x9Q1`),  
   **When** a visitor issues a `GET /aB3x9Q1` request,  
   **Then** the service returns HTTP `302 Found` with the `Location` header set to the destination URL, `Cache-Control: no-store, no-cache`, and completes with sub-50ms latency.
4. **Given** a non-existent short code (e.g., `zZ99999`),  
   **When** a visitor issues a `GET /zZ99999` request,  
   **Then** the service returns HTTP `404 Not Found` with an RFC 7807 `ProblemDetails` response.

---

### User Story 2 — Brownfield: Privacy-Safe Analytics & Emergency Link Revocation (Priority: P2)

*As a marketing manager, compliance officer, or application owner,*  
*I want to inspect click performance metrics for an active short link and have an emergency off-switch to revoke links,*  
*So that I can evaluate campaign effectiveness without storing customer PII, and immediately terminate links that point to outdated or erroneous content.*

**Why this priority**: Enhances the existing greenfield codebase (brownfield evolution) by introducing analytics and lifecycle management without breaking existing redirection endpoints.

**Independent Testability**: Can be independently tested by clicking an existing short link 3 times, calling `GET /api/v1/urls/{shortCode}` to verify click counts, issuing `DELETE /api/v1/urls/{shortCode}` to revoke the link, and verifying subsequent clicks return `410 Gone`.

**Acceptance Scenarios**:
1. **Given** an active short link that has been clicked 5 times by 3 unique visitor IP addresses,  
   **When** a client requests `GET /api/v1/urls/{shortCode}`,  
   **Then** the service returns HTTP `200 OK` containing `originalUrl`, `shortCode`, `totalClicks: 5`, `uniqueVisitors: 3`, `createdAt`, `lastAccessedAt`, and `isActive: true`, with zero customer PII or raw IP addresses exposed.
2. **Given** an active short link,  
   **When** an authorized user sends `DELETE /api/v1/urls/{shortCode}`,  
   **Then** the service marks the record inactive (`isActive = false`) and returns HTTP `204 No Content`.
3. **Given** a revoked or expired short link,  
   **When** a visitor attempts to access `GET /{shortCode}`,  
   **Then** the service terminates the redirect and returns HTTP `410 Gone` with an RFC 7807 `ProblemDetails` payload stating the link is no longer active.

---

### User Story 3 — Ambiguous Requirements & Intent Normalization (Priority: P2)

*As an enterprise engineer or team lead,*  
*I want the system to automatically analyze vague stakeholder requests, identify unstated assumptions or boundary conditions, and prompt for clarification,*  
*So that requirements are formally normalized into executable contracts before architectural planning or coding begins.*

**Why this priority**: Directly demonstrates the Requirement Understanding and Ambiguity Normalization capabilities required in Section 4 of the Charles Schwab assignment.

**Independent Testability**: Can be tested by executing the Ambiguous scenario in the Orchestrator CLI, which inputs a vague prompt (`"Let users pick custom names, track clicks, and prevent spam"`), flags the missing edge cases, displays the normalized rules, and halts for human approval.

**Acceptance Scenarios**:
1. **Given** a vague feature request lacking boundary constraints,  
   **When** the requirement is processed through the Spec Kit `/speckit.clarify` workflow,  
   **Then** the system surfaces explicit questions regarding alias length, reserved keywords, collision handling, rate limit thresholds, and expiration semantics.
2. **Given** a request to create a short link with a custom alias (e.g., `customAlias: "tax-guide-2026"`),  
   **When** the requested alias is already registered to an existing record,  
   **Then** the service does not retry generation; it immediately returns HTTP `409 Conflict` with an RFC 7807 `ProblemDetails` error explaining the alias is unavailable.
3. **Given** a link creation request with an optional expiration timestamp (`expiresAt`),  
   **When** `expiresAt` is set to a past timestamp,  
   **Then** the service returns HTTP `400 Bad Request` explaining that expiration timestamps must be in the future (UTC).

---

### User Story 4 — Reusable Agentic SDLC Orchestrator CLI (`Orchestrator.Cli`) (Priority: P1 — Critical Differentiator)

*As an engineering lead, architect, or technical reviewer,*  
*I want a standalone, runnable C# console application that coordinates the entire SDLC across Greenfield, Brownfield, and Ambiguous scenarios with explicit human approval gates and self-correcting bounded retries,*  
*So that I can evaluate autonomous multi-agent development under controlled enterprise governance, and reuse the CLI across future repositories in my workspace.*

**Why this priority**: This is the central architectural deliverable and primary differentiator of the Charles Schwab assignment.

**Independent Testability**: Can be tested by running `dotnet run --project src/Orchestrator.Cli`, stepping through the interactive terminal menu, executing scenarios, exercising approval gates (`[A]pprove / [R]eject / [S]afe-stop`), triggering simulated test failures to verify bounded retries/rollback, and reviewing the generated telemetry report.

**Acceptance Scenarios**:
1. **Given** a reviewer launches `dotnet run --project src/Orchestrator.Cli` without external API keys or cloud credentials,  
   **When** the CLI starts,  
   **Then** it initializes in Zero-Config Evaluation Mode, presenting an interactive menu to execute the Greenfield, Brownfield, or Ambiguous scenarios.
2. **Given** an executing scenario,  
   **When** the orchestrator reaches a lifecycle gate (Gate 1: Specification, Gate 2: Architecture & Tasks, Gate 3: Release Readiness),  
   **Then** the system pauses execution and prompts the human user for input (`[A]pprove`, `[R]eject`, or `[S]afe-Stop`).
3. **Given** a task execution that results in a compiler error or failing test during automated verification (`dotnet test`),  
   **When** the failure is detected,  
   **Then** the orchestrator triggers bounded self-correction retries up to a maximum of 3 attempts. If the 3rd attempt fails, it automatically rolls back the repository to the pre-execution snapshot and halts safely.
4. **Given** the scenario completes,  
   **When** all tasks are finalized,  
   **Then** the orchestrator displays a comprehensive Telemetry & Audit Report including Task Success Rate, Retry/Rollback Frequency, Mean Time to Recovery (MTTR), and End-to-End Latency.
5. **Given** a user invokes the CLI with reusable flags (e.g., `--init --workspace <path>` or `--verify`),  
   **When** executed against an external repository in `source/repos/`,  
   **Then** the orchestrator scaffolds the standard `specs/` directory (`constitution.md`, `spec.md`, `plan.md`, `tasks.md`) or runs pre-push verification gates independently of the URL Shortener domain.
6. **Given** an executing workflow where the human engineer selects `[R]eject` at a checkpoint or modifies an upstream specification/requirement mid-run,  
   **When** the upstream change is submitted,  
   **Then** the orchestrator dynamically re-plans all affected downstream architecture and implementation tasks in the dependency graph while preserving cross-stage context, decision lineage, and constitutional governance.

---

## ⚠️ Edge Cases & Failure Modes

1. **Base62 Collision Exhaustion**:
   - If a cryptographically generated 7-character Base62 code collides with an existing active database record, the service generates a new code and retries up to 3 times.
   - If all 3 attempts collide, the service returns HTTP `503 Service Unavailable` with `ProblemDetails` instructing the caller to retry shortly.
2. **Rate Limit Throttling**:
   - If a client IP exceeds 10 link creations per minute or 100 redirects per minute, the service returns HTTP `429 Too Many Requests`, sets the `Retry-After: 60` response header, and returns a structured `ProblemDetails` body.
3. **SSRF Evasion & DNS Rebinding**:
   - Validates hostname syntax and resolves IP addresses to verify destination does not route to `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.169.254`, or `::1`.
   - Rejects destination URLs pointing back to the service's own host domain to prevent infinite redirection loops.
4. **Oversized Payloads**:
   - JSON payloads exceeding 4 KB are rejected at the API perimeter with HTTP `413 Payload Too Large`.
   - Destination URL strings exceeding 2,048 characters are rejected with HTTP `400 Bad Request`.
5. **Database / Cache Unavailability**:
   - If memory cache misses occur and persistence is degraded, the service falls back gracefully and returns HTTP `503 Service Unavailable` with correlation `traceId`.

---

## 📋 Functional Requirements (`FR-001` – `FR-016`)

- **FR-001**: The system MUST expose `POST /api/v1/urls` accepting an `originalUrl` (string, max 2048 characters, mandatory `https://` in production), optional `customAlias` (string, 4–32 characters), and optional `expiresAt` (ISO 8601 UTC timestamp).
- **FR-002**: The system MUST validate incoming destination URLs against an Anti-SSRF policy, blocking private networks, localhost, cloud metadata (`169.254.169.254`), non-HTTP/HTTPS schemes, and self-referencing loops.
- **FR-003**: The system MUST generate 7-character Base62 short codes (`[0-9a-zA-Z]`) using a cryptographically secure random number generator (`RandomNumberGenerator`), providing $\approx 3.52 \times 10^{12}$ unique entropy combinations.
- **FR-004**: The system MUST retry random code generation up to 3 times upon collision before returning HTTP `503 Service Unavailable`.
- **FR-005**: The system MUST validate user-supplied custom aliases against `^[a-zA-Z0-9_-]{4,32}$` and block reserved words (`api`, `health`, `admin`, `swagger`, `metrics`). If a custom alias already exists, it MUST return HTTP `409 Conflict`.
- **FR-006**: The system MUST expose `GET /{shortCode}` returning HTTP `302 Found` with the `Location` header set to the destination URL and `Cache-Control: no-store, no-cache`.
- **FR-007**: The system MUST record redirection click events asynchronously via a non-blocking in-memory channel (`Channel<ClickEvent>`) without slowing down HTTP redirect response time.
- **FR-008**: The system MUST hash visitor IP addresses using SHA-256 with a salt before persisting analytics, guaranteeing raw IP addresses are never stored.
- **FR-009**: The system MUST expose `GET /api/v1/urls/{shortCode}` returning link metadata, `totalClicks`, `uniqueVisitors`, `createdAt`, `expiresAt`, `isActive`, and `lastAccessedAt`.
- **FR-010**: The system MUST expose `DELETE /api/v1/urls/{shortCode}` performing a soft revocation (`isActive = false`) and returning HTTP `204 No Content`.
- **FR-011**: The system MUST return HTTP `410 Gone` when a visitor requests an expired or revoked short code.
- **FR-012**: The system MUST return HTTP `404 Not Found` when a visitor requests a non-existent short code.
- **FR-013**: The system MUST enforce rate limiting (10 req/min for creation; 100 req/min for redirection), returning HTTP `429 Too Many Requests` with a `Retry-After` header.
- **FR-014**: The system MUST format all error responses according to RFC 7807 / RFC 9457 `ProblemDetails`, including a unique `traceId` for end-to-end auditability.
- **FR-015**: The system MUST expose `GET /health` returning HTTP `200 OK` (`{"status": "Healthy"}`) for readiness and liveness orchestration.
- **FR-016**: The `Orchestrator.Cli` state machine MUST enforce a non-linear dependency graph with Entry/Exit Gates (Gates 1–3), bounded self-correction retries ($\le 3$) with automatic Git rollback, audit-grade telemetry (Success Rate, Retry/Rollback Frequency, MTTR, Stage Latency), and dynamic downstream re-planning whenever upstream outputs or human gate decisions change.

---

## 🗄️ Key Data Entities

### 1. `ShortUrl` (Core Domain Entity)
- `Id`: GUID / ULID (primary key, non-sequential).
- `ShortCode`: String (7 chars Base62, unique index) or Custom Alias (4–32 chars).
- `OriginalUrl`: String (max 2048 chars, validated HTTPS URI).
- `CreatedAt`: DateTime (UTC).
- `ExpiresAt`: Nullable DateTime (UTC).
- `IsActive`: Boolean (default `true`, flipped to `false` on revocation).
- `RowVersion`: Concurrency token (byte array).

### 2. `ClickEvent` (Analytics Entity)
- `Id`: GUID / ULID (primary key).
- `ShortUrlId`: GUID / ULID (foreign key to `ShortUrl`).
- `HashedIp`: String (SHA-256 salted hash of client IP, 64 hex characters).
- `UserAgent`: String (truncated to max 256 chars, sanitized).
- `Referer`: Nullable String (sanitized).
- `Timestamp`: DateTime (UTC).

### 3. `ProblemDetails` (Standard Error Representation)
- `Type`: URI reference identifying the error type (e.g., `https://tools.ietf.org/html/rfc7231#section-6.5.1`).
- `Title`: Short, human-readable summary of the problem.
- `Status`: HTTP status code integer.
- `Detail`: Specific explanation of this occurrence.
- `Instance`: URI path of the request.
- `TraceId`: Unique request correlation identifier.

---

## 📊 Measurable Success Criteria (`SC-001` – `SC-007`)

- **SC-001**: Redirection (`GET /{shortCode}`) MUST achieve **sub-50ms p99 latency** under normal operational load by utilizing in-memory cache lookup.
- **SC-002**: Automated test suite MUST achieve **$\ge 80\%$ line and branch coverage** across domain logic, security checks, and controllers/endpoints.
- **SC-003**: The service MUST achieve **100% compliance** with the OpenAPI 3.1 contract, verified by automated schema contract tests (`UrlShortener.ContractTests`).
- **SC-004**: The solution MUST build with **0 compiler errors and 0 compiler warnings** under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **SC-005**: Anti-SSRF security test suite (`UrlShortener.SecurityTests`) MUST verify **100% rejection** of loopback, RFC 1918 private IPs, and cloud metadata addresses.
- **SC-006**: The C# Orchestrator CLI MUST complete simulated runs across all 3 scenarios with **zero broken commits** pushed to repository branches.
- **SC-007**: The C# Orchestrator CLI MUST demonstrate self-correction of simulated build/test failures within $\le 3$ bounded retries, or execute a clean automatic rollback.

---

## 📜 Formal OpenAPI 3.1 Executable Contract

Below is the complete, machine-readable OpenAPI 3.1 YAML specification for the service:

```yaml
openapi: 3.1.0
info:
  title: Enterprise URL Shortener API
  version: 1.0.0
  description: >
    Production-grade, financial-resilient URL Shortener service built with ASP.NET Core Minimal APIs.
    Enforces cryptographic Base62 tokenization, Anti-SSRF boundary protection, RFC 7807 error handling,
    sub-50ms redirection, and privacy-safe asynchronous analytics.
servers:
  - url: https://localhost:7001
    description: Local development environment
  - url: https://shortener.schwab.com
    description: Production environment

paths:
  /api/v1/urls:
    post:
      summary: Create a shortened URL
      description: Validates destination URL, checks anti-SSRF policies, and generates a Base62 7-char short link or claims custom alias.
      operationId: createShortUrl
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateShortUrlRequest'
      responses:
        '201':
          description: Short URL successfully created
          headers:
            Location:
              description: URI of the created resource
              schema:
                type: string
                example: /api/v1/urls/aB3x9Q1
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ShortUrlResponse'
        '400':
          description: Invalid input or dangerous URI scheme (Anti-SSRF violation)
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '409':
          description: Requested custom alias is already taken
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '429':
          description: Rate limit exceeded (max 10 creations per minute)
          headers:
            Retry-After:
              schema:
                type: integer
                example: 60
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '503':
          description: Short code collision exhaustion after 3 retries
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'

  /api/v1/urls/{shortCode}:
    get:
      summary: Retrieve URL metadata and analytics summary
      description: Returns click statistics, creation timestamp, and active status without leaking PII.
      operationId: getUrlMetadata
      parameters:
        - name: shortCode
          in: path
          required: true
          schema:
            type: string
            pattern: '^[a-zA-Z0-9_-]{4,32}$'
      responses:
        '200':
          description: URL metadata and analytics summary
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UrlMetadataResponse'
        '404':
          description: Short code not found
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'

    delete:
      summary: Revoke an active short URL
      description: Soft-deactivates the short URL so subsequent redirects return 410 Gone.
      operationId: revokeShortUrl
      parameters:
        - name: shortCode
          in: path
          required: true
          schema:
            type: string
            pattern: '^[a-zA-Z0-9_-]{4,32}$'
      responses:
        '204':
          description: Short URL successfully revoked (no content returned)
        '404':
          description: Short code not found
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'

  /{shortCode}:
    get:
      summary: Redirect to original destination URL
      description: Resolves short code and returns HTTP 302 Found temporary redirect with Cache-Control no-store.
      operationId: redirectToDestination
      parameters:
        - name: shortCode
          in: path
          required: true
          schema:
            type: string
            pattern: '^[a-zA-Z0-9_-]{4,32}$'
      responses:
        '302':
          description: Temporary redirect to destination URL
          headers:
            Location:
              description: Target destination URL
              schema:
                type: string
                example: https://www.schwab.com/tax-guide-2026
            Cache-Control:
              schema:
                type: string
                example: no-store, no-cache
        '404':
          description: Short code not found
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '410':
          description: Link has expired or been revoked
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '429':
          description: Rate limit exceeded (max 100 redirects per minute)
          headers:
            Retry-After:
              schema:
                type: integer
                example: 60
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'

  /health:
    get:
      summary: Service health check
      description: Liveness and readiness probe for cloud and container orchestration.
      operationId: getHealth
      responses:
        '200':
          description: Service is healthy
          content:
            application/json:
              schema:
                type: object
                properties:
                  status:
                    type: string
                    example: Healthy

components:
  schemas:
    CreateShortUrlRequest:
      type: object
      required:
        - originalUrl
      properties:
        originalUrl:
          type: string
          format: uri
          maxLength: 2048
          example: https://www.schwab.com/retirement-planning
        customAlias:
          type: [string, "null"]
          minLength: 4
          maxLength: 32
          pattern: '^[a-zA-Z0-9_-]{4,32}$'
          example: retirement-2026
        expiresAt:
          type: [string, "null"]
          format: date-time
          example: '2026-12-31T23:59:59Z'

    ShortUrlResponse:
      type: object
      required:
        - shortCode
        - shortUrl
        - originalUrl
        - createdAt
        - isActive
      properties:
        shortCode:
          type: string
          example: aB3x9Q1
        shortUrl:
          type: string
          format: uri
          example: https://schwab.co/aB3x9Q1
        originalUrl:
          type: string
          format: uri
          example: https://www.schwab.com/retirement-planning
        createdAt:
          type: string
          format: date-time
          example: '2026-11-03T18:00:00Z'
        expiresAt:
          type: [string, "null"]
          format: date-time
          example: '2026-12-31T23:59:59Z'
        isActive:
          type: boolean
          example: true

    UrlMetadataResponse:
      type: object
      required:
        - shortCode
        - originalUrl
        - totalClicks
        - uniqueVisitors
        - createdAt
        - isActive
      properties:
        shortCode:
          type: string
          example: aB3x9Q1
        originalUrl:
          type: string
          format: uri
          example: https://www.schwab.com/retirement-planning
        totalClicks:
          type: integer
          format: int64
          example: 142
        uniqueVisitors:
          type: integer
          format: int64
          example: 98
        createdAt:
          type: string
          format: date-time
          example: '2026-11-03T18:00:00Z'
        expiresAt:
          type: [string, "null"]
          format: date-time
          example: '2026-12-31T23:59:59Z'
        lastAccessedAt:
          type: [string, "null"]
          format: date-time
          example: '2026-11-03T20:15:30Z'
        isActive:
          type: boolean
          example: true

    ProblemDetails:
      type: object
      required:
        - type
        - title
        - status
        - traceId
      properties:
        type:
          type: string
          format: uri
          example: https://tools.ietf.org/html/rfc7231#section-6.5.1
        title:
          type: string
          example: Bad Request
        status:
          type: integer
          example: 400
        detail:
          type: string
          example: The provided destination URL violates the Anti-SSRF policy.
        instance:
          type: string
          example: /api/v1/urls
        traceId:
          type: string
          example: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
```
