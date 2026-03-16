# Deprecation and Sunset Policy

This policy governs both API contract lifecycle and published model lifecycle.

## 1. API Contract Deprecation

### Versioning
- API endpoints are versioned (`/v1/...`, `/v2/...`).
- Deprecation must not change existing endpoint behavior during grace period.

### Required Deprecation Signals
- Add `deprecated: true` in OpenAPI for deprecated operations.
- Emit response header: `Sunset: <RFC-1123 date>`.
- Emit response header: `Deprecation: true`.
- Provide migration target in docs/changelog.

### Grace Period
- Minimum grace period: 90 days from deprecation announcement.
- Shorter periods require explicit platform-admin override ADR.

### Removal Gate
Hard removal is allowed only when all are true:
1. Grace period elapsed.
2. Replacement API documented and available.
3. Consumer impact assessment completed.
4. Release gate approves removal.

## 2. Model Contract Deprecation

### Published Model Lifecycle
- Published model versions may transition: `Published -> Deprecated -> Sunset`.
- Deprecated models remain readable/evaluable during grace period.

### Forced Migration Triggers
Forced migration can be required when:
- Contract-breaking security issue is identified.
- Contract-breaking correctness issue is identified.
- Platform non-goal would be violated by keeping old model active.

### Sunset Communication
Each model sunset announcement must include:
- Model SemanticId and version.
- Sunset date.
- Compatibility classification.
- Migration notes and replacement target.

## 3. Enforcement
- Governance endpoints must enforce transition legality.
- Idempotency key is required for stage/publish/deprecate/sunset operations.
- All transition attempts (success/failure) must emit audit events.
