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
- `DiagnosticAllowance(Code, ElementPath, ProvenanceIds)`: acknowledges one diagnostic an analysis
  arm emits — a flow finding, or a contract finding in the set below — keyed by code plus the
  finding's element path. Bound with `WithDiagnosticAllowances`.

Both are frozen on `DomainSession` and read by the analyzers from `RuntimeAnalysisCache.Session`;
`WithDomain` carries them on both branches. No library, no keyword, no load-order rule.

There is no second rules table. The flow is presented to the checks that decide — the ones that
already existed, plus the nullability arm #439 adds over the existing `optional` fact:
`IsConstraintSubsumed` (length, range) gives `DMEFF008` on the target property;
`CheckCompatible` gives `DMSEM003` on the target property; an optional source into a required
target gives `DMEFF014` on the target property, because `optional` is the only way an entity
property may hold null; a path that does not resolve is `DMSEM005` on the domain and no
allowance clears it. No node is inserted into the domain, so nothing lowers and a cross-entity
copy is not a `DMEFF012`. Scale has no constraint diagnostic, so it is not checked here (it is a
platform mapping row).

- No matching allowance: the diagnostic is an error and `ThrowIfHasErrors` (the only **severity**
  gate) refuses.
- Matching allowance: the same diagnostic is a warning, kept in the report. Direct `session.Lower`
  of an acknowledged **flow** domain succeeds; an acknowledged contract finding still refuses
  (see "Contract findings" below).
- An allowance no analysis arm consumed is `DMALW001` (error, not acknowledgeable): wrong code or
  path, stale after the finding stopped firing, or empty or blank provenance ids.

Ordinary parameter assigns keep their own path and severity; the allowance is consulted only by the
flow arms and the contract arm, never by a pass over the diagnostic list.

## Contract findings (#422)

The same `DiagnosticAllowance` record covers the contract findings decision 8 puts in the tolerant
set: `DMCON002`, `DMCON003`, `DMCON004`, `DMCON005`, `DMCON010`, and `DMCON013`. A match reports the
finding as a warning on the shared diagnostic record and stores one `ContractToleranceMetadata` fact
per (code, element path) on the domain; `polyc check` shows the warning and does not drop it.

Lower, Emit, Compile, and direct projection still refuse, through `ContractToleranceGate`, naming the
fact's code and element path. A recorded `ContractBindingMetadata.Status` other than `Bound` also
refuses in the splice. This is not the deleted #430 contradiction gate: that gate re-derived severity
from a second rules table for flow diagnostics. This one reads a fact that exists only because an
acknowledged **contract** finding was recorded, covers contract findings only, and never re-derives
severity. `ThrowIfHasErrors` remains the only severity gate.

Not acknowledgeable through this arm: `DMCON012` (an ambiguous overload is an authoring error),
the package-metadata and signature `DMCON001` errors, the unresolved-bind action/parameter cases,
and every flow code. `DMEFF014` is #444's rule and is not in this arm. `InternalDomainProducer`
takes no analysis and is not a Lower door.

## Platform

Producing while an allowance is still the open review item is refused by the platform (#7 / #30),
not by the engine. Resolving it is a proposal whose edit stops the diagnostic firing; the platform
then drops the allowance, and a leftover one fails the next analyze (`DMALW001`).

## Consequences

- Net deletion of the #430 surface. The optional claim arguments are gone from `Open`,
  `ForExtensions`, `ForAuthoringDocument`, `ForAuthoringSource`; they were never pin-bumped.
- A rename changes the element path, so the allowance stops matching and the diagnostic is an error
  again: visible, not a silent reattach.
