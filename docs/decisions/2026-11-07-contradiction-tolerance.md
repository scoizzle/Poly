# Contradiction tolerance (engine half, #429)

Status: **superseded 2026-11-08** by [`2026-11-08-diagnostic-allowance.md`](2026-11-08-diagnostic-allowance.md) (#435). The bag,
`uses contradictions`, strict mode, and the `GetOrLower` gate were deleted; the text below is history, not current truth.

Was: accepted 2026-11-07. Design source: poly-platform `docs/product/modernization.md`
(contradiction-tolerant modeling); platform ask scoizzle/poly-platform#29.

## Decision

- Mode-on is `uses contradictions` (library id `contradictions`, registered by `ProductCatalog`
  in `Catalog` and `Model`). Mode-off is its absence. No keyword, no document setting.
- The platform binds `ContradictionClaim`s on the session (`SessionBuilder.WithContradictionClaims`,
  carried by `ForExtensions`, `Open`, `ForAuthoringDocument`, `ForAuthoringSource`; `ForDocument` and
  `ForSource` stay claims-free). The set is frozen as `DomainSession.ContradictionClaims`;
  `WithDomain` copies it on the same-extensions path and passes it to `Open` on reload. A reload
  that changes `uses` goes through the Core catalog (the session keeps no catalog), so it fails
  closed on the unknown id; a reload that drops the id fails strict mode.
- `ContradictionFlowPass` merges each claim's two `Entity.Property` endpoints with the domain's
  declared facts and publishes a `ContradictionBag` (`ContradictionMetadata`), empty whenever the
  id is loaded. Four kinds: width, scale, nullability, type-coercion. Families are compared, never
  dialect type names. Unknown facts and compatible flows create no entry.
- Claims the engine cannot check are `DMCONT001` errors (unresolvable path, blank name, empty or
  blank claim ids, undefined status, identical endpoints, duplicate names, negative numbers),
  never a dropped entry.
- One gate in `RuntimeAnalysisCache.GetOrLower`, after `ThrowIfHasErrors` and before `Bind`.
  It refuses while any entry is `open` and names it, and refuses when the session lists the id
  but no bag was published. Lower, Emit, simulation, `ModuleInstanceExecutor`, and MCP
  `EnsureExecution` all reach it. Only `open` blocks.
- Strict mode: `SessionBuilder.Build` throws when claims are bound and the id is not loaded.
  Binding after the library is loaded throws.

## Rejected

- A diagnostic as the store: it would stop the document saving, the failure this removes.
- A gate only in `LowerToCatalog` or after `GetLower`: simulation would slip through, or lowering
  would happen before refusing.
- A lowering flag or a second gate in `DomainSession`.
- Slashed `DomainElementPath` strings for claim endpoints (a known foot-gun, #415).
- Folding `ConstraintPropagationAnalyzer` errors into the bag; reader/DDL parsing; resolution options.

## Consequences

- Claims are frozen per session. A claim change needs a new session and a new `Domain` instance
  before lowering, because `RuntimeAnalysisCache.Bind` refuses a second session over an
  already-lowered `Domain`.
- The gate reads the bag from the analysis, not the session's claim list. A session and its
  analysis must come from the same compile: handing a claim-free analysis to a claim-bearing
  session defeats the gate. That is a caller precondition, not a second check.
- `WithDomain` keeps no catalog, so a reload that changes `uses` goes through the Core catalog and
  fails closed on `contradictions`. A caller re-opening with a product catalog passes
  `DomainSession.ContradictionClaims` explicitly.
- `Kinds` is a set and may be empty: one flow can be both narrow and non-null, and a resolved or
  withdrawn claim whose facets now agree stays in the bag with no kinds.
- The scale rule follows the accepted definition (target precision or scale less than the source).
  It does not compare integer digits (`precision - scale`), so `NUMBER(12,2)` into `NUMBER(14,6)`
  is not flagged; a stricter rule is a spec change, not an implementation detail.
- The optional claim argument on `Open`, `ForExtensions`, `ForAuthoringDocument`, and
  `ForAuthoringSource` is source-compatible but binary-breaking for an already-compiled caller;
  the platform pin bump must rebuild.
