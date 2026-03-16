# Diagnostic Code Conventions

Diagnostic codes are stable identifiers for machine-readable failures and warnings.

## Format
`<CATEGORY>.<SUBCATEGORY>_<DETAIL>`

Examples:
- `STRUCT.MISSING_FIELD`
- `SEMANTIC.INVALID_TRANSITION`
- `COMPAT.BREAKING_CHANGE`
- `AUTH.TENANT_MISMATCH`
- `RUNTIME.NON_DETERMINISTIC_ORDER`

## Categories
- `STRUCT` - shape and construction issues.
- `SEMANTIC` - domain rule and lifecycle semantics.
- `COMPAT` - compatibility and publish-gate outcomes.
- `AUTH` - auth, tenant, and scope violations.
- `RUNTIME` - evaluator execution behavior and determinism.

## Rules
- Codes are immutable once shipped.
- Messages may change; codes must not.
- Prefer specific codes over generic catch-alls.
- Include a path or field reference when applicable.

## Severity Guidance
- `Error` - request/model is invalid or cannot continue.
- `Warning` - allowed but risky or non-blocking.
- `Info` - informative diagnostics only.

## Required Fields in Diagnostic Envelope
- `code` (string)
- `message` (string)
- `severity` (`Error|Warning|Info`)
- `path` (string, optional)
- `correlationId` (string, optional)

## Reserved Codes (Seed Set)
- `STRUCT.MISSING_FIELD`
- `STRUCT.INVALID_TYPE_EXPRESSION`
- `SEMANTIC.INVALID_TRANSITION`
- `SEMANTIC.LIFECYCLE_READONLY_VIOLATION`
- `COMPAT.BREAKING_CHANGE`
- `AUTH.TENANT_MISSING`
- `AUTH.TENANT_MISMATCH`
- `AUTH.TENANT_UNKNOWN`
- `AUTH.INSUFFICIENT_SCOPE`
- `AUTH.GRANT_REQUIRED`
- `RUNTIME.RULE_EVALUATION_FAILED`
