# Test Strategy

This project uses three test tiers: Unit, Integration, and Conformance.

## 1) Unit Tests
Purpose: validate isolated logic with no network, storage, or host runtime dependencies.

Scope:
- Value objects and parsers.
- Contract invariants.
- Deterministic utilities.
- Compatibility classifier logic.

Rules:
- No I/O.
- Fast execution.
- Table-driven cases preferred for classification logic.

## 2) Integration Tests
Purpose: validate behavior at service boundaries and composed subsystems.

Scope:
- HTTP endpoints.
- Auth/tenant policy enforcement.
- Idempotency and approval behavior.
- End-to-end evaluator pipeline through service interfaces.

Rules:
- Use in-memory defaults for v1 stores.
- Assert full diagnostic envelopes for failures.
- Include happy-path and negative-path assertions.

## 3) Conformance Tests
Purpose: verify semantic parity across interfaces and versions.

Scope:
- REST vs MCP evaluate parity.
- Cross-tenant and grant enforcement parity.
- V1 vs V2 semantic equivalence for agreed scenarios.

Rules:
- Same input must produce equivalent diagnostics and outcomes.
- Divergence is a test failure.

## Naming Conventions
- Unit: `*UnitTests.cs`
- Integration: `*IntegrationTests.cs`
- Conformance: `*ConformanceTests.cs`

## Mocking Guidance
- Unit tests: mock only external collaborators.
- Integration tests: prefer real in-memory implementations over mocks.
- Conformance tests: avoid mocks in assertion path; compare real outputs.

## Minimum Expectations Per Change
- Contract/value-object change: unit tests.
- Service-surface change: integration tests.
- REST/MCP parity or version-equivalence change: conformance tests.
