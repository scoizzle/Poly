# Compatibility Policy

This policy classifies model and rule changes and defines publish-gate behavior.

## Change Classes
1. Additive (safe)
- Adds optional fields, optional rules, or non-breaking metadata.
- Existing consumers continue to work unchanged.

2. Non-breaking (safe with caution)
- Tightens behavior without invalidating previously valid payloads.
- Requires regression evidence but no major bump.

3. Soft-breaking (warn)
- Can change behavior for edge cases but preserves contract shape.
- Publish allowed with explicit warnings and migration notes.

4. Breaking (blocked without major bump)
- Removes or renames exposed elements.
- Tightens constraints in a way that rejects previously valid data.
- Requires model major version increment.

5. Contract-breaking (blocked)
- Violates core guarantees (determinism, tenant isolation, stable diagnostics semantics).
- Publish blocked regardless of version bump until resolved.

## Publish Gate Rules
- Additive and Non-breaking: allow publish.
- Soft-breaking: allow publish with warning diagnostics and migration notes.
- Breaking: block unless major version is incremented.
- Contract-breaking: always block.

## Required Evidence
- Compatibility diff report.
- Diagnostic changes list.
- Tests proving expected behavior for changed paths.

## Versioning
- `ModelVersion`: semantic version of model structure.
- `RuleSetVersion`: semantic version of rule behavior.
- These versions may advance independently.
