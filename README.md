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
│   └── UrlShortener.Api/  # Production C# / .NET 9 Minimal API service
├── tests/
│   ├── UrlShortener.UnitTests/        # Domain logic and hashing tests
│   ├── UrlShortener.ContractTests/    # OpenAPI schema validation tests
│   └── UrlShortener.SecurityTests/    # SSRF, rate limit, and abuse tests
└── README.md
```

---

## 🚀 SDLC Execution Scenarios

1. **Greenfield Execution**: End-to-end autonomous synthesis of the core URL shortening and redirection engine from scratch.
2. **Brownfield Execution**: Context-aware refactoring and feature enhancement on existing codebases without introducing regression.
3. **Ambiguous Requirements**: Automated ambiguity detection, intent normalization, and interactive human clarification before execution.

---

## 👩‍💻 Author

**Vicky Lee Jones**  
*C# .NET Solutions Developer & AI Agentic Systems Engineer*
