# Diagnostic allowance (replaces contradiction tolerance, #435)

Status: accepted 2026-11-08. Supersedes [`2026-11-07-contradiction-tolerance.md`](2026-11-07-contradiction-tolerance.md) (#429, #430).

## Verdict

Scot, on merged #430: the bag is over-engineered; let the existing constraint diagnostics guide the
allowances. #430 added a second rules table (width, scale, nullability, family) beside constraint
analysis, a library, strict mode, and a gate beside `ThrowIfHasErrors`. All of it is deleted.

## Decision

Two records, in `Poly/DomainModeling/Analysis/DiagnosticAllowance.cs`:

- `ObservedFlow(SourcePath, TargetPath)`: a copy the platform's reader found, as dotted
  `Entity.Property` paths. Bound with `SessionBuilder.WithObservedFlows`; a null element or a
  repeated pair throws `ArgumentException` at bind.
- `DiagnosticAllowance(Code, ElementPath, ProvenanceIds)`: acknowledges one diagnostic those checks
  emit, keyed by code plus the finding's element path. Bound with `WithDiagnosticAllowances`.

Both are frozen on `DomainSession` and read by the analyzers from `RuntimeAnalysisCache.Session`;
`WithDomain` carries them on both branches. No library, no keyword, no load-order rule.

There is no second rules table. The flow is presented to the checks that already decide:
`IsConstraintSubsumed` (length, range) gives `DMEFF008` on the target property;
`CheckCompatible` gives `DMSEM003` on the target property; a path that does not resolve is
`DMSEM005` on the domain and no allowance clears it. No node is inserted into the domain, so
nothing lowers and a cross-entity copy is not a `DMEFF012`. Scale and nullability have no
constraint diagnostic, so they are not checked here.

- No matching allowance: the diagnostic is an error and `ThrowIfHasErrors` (the only gate) refuses.
- Matching allowance: the same diagnostic is a warning, kept in the report. Direct `session.Lower`
  of an acknowledged domain succeeds.
- An allowance no flow diagnostic consumed is `DMALW001` (error, not acknowledgeable): wrong code or
  path, stale after the flow became compatible, or empty or blank provenance ids.

Ordinary parameter assigns keep their own path and severity; the allowance is consulted only by the
flow arms, never by a pass over the diagnostic list.

## Platform

Producing while an allowance is still the open review item is refused by the platform (#7 / #30),
not by the engine. Resolving it is a proposal whose edit stops the diagnostic firing; the platform
then drops the allowance, and a leftover one fails the next analyze (`DMALW001`).

## Consequences

- Net deletion of the #430 surface. The optional claim arguments are gone from `Open`,
  `ForExtensions`, `ForAuthoringDocument`, `ForAuthoringSource`; they were never pin-bumped.
- A rename changes the element path, so the allowance stops matching and the diagnostic is an error
  again: visible, not a silent reattach.
