# Relationship refactor review (source-scoping + entity-owned navs) — 2026-08-10

- **Target**: local — uncommitted `git diff HEAD` (31 files, +482/−223)
- **Mode**: standard
- **Issue counts**: 1 bug, 3 suggestions, 1 nit
- **Verdict**: **not ship** — one confirmed compile-breaking bug (CS1501) in the export; two doc/claim gaps; one bridge-ctor footgun
- **Primary evidence**: all findings reproduced with own greps / compiles / `git show` this session; suite 1970 green (plan claim verified)

## Summary

The change correctly implements relationship identity = (source entity, name): the parser/structural/RLM/MTI all become source-scoped, `Domain.Relationships` is a derived flatten of `Entity.Navigations`, mutations operate on entity navs, and the runtime reports precise reverse-side errors. The migration bridge (3-arg `Domain` ctor) keeps ~356 test sites compiling. But the **export still has a live arity bug** the refactor did not fix — the same CS1501 the companion plan documents — and the refactor's own claims (back-ref derived, auto-wire) are not delivered by this diff.

## Issues

### Issue 1 -- Severity: bug (export CS1501 — live, unreported by suite)
- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:1316` (AddCreateNavMethod signature) vs `EffectLoweringPass.cs:403` (action arg build)
- Description: A `create in Rel` whose target entity has its own `many` navs emits a `CreateNav` method with **fewer params than the action's call passes**. Repro (from `csharp-export-createin-bugs-2026-08-10.md`, verified live):
  ```csharp
  private Order CreateOrders(string title, long total, Customer customer)          // 3 params
  ... CheckOut(...) => Success(this.CreateOrders(book, 0L, null, null, null));     // 5 args
  ```
  `error CS1501: No overload for method 'CreateOrders' takes 5 arguments`. `AddCreateNavMethod` builds its signature from ESM **excluding collection navs** (empty-init'd inside via `new List<T>()`), while the action-lowering still passes collection args. The library demo compiled only because `Loan` has no collection navs — the shape is untested. The plan doc honestly flags it, but it ships with this diff.
- Suggestion: make the two arg lists identical by construction — have the action-lowering call `CreateNav` with only the nav-method's params (the collections are initialized inside the factory, so the caller should NOT pass them). Fix + a render-and-compile test for a target entity with its own `many` navs (this is the E-guard class).
- Status: open

### Issue 2 -- Severity: suggestion (guide §0.3 auto-wire claim unmet)
- File: `Poly.Mcp/Docs/poly-dsl-guide.md:82-83`
- Description: "The generated C# sets `borrower` to `this` automatically" — but the export's `CreateNav` passes the **back-reference as a ctor param** (`this` at the call site only when the DSL author binds it), and `IsBackReference` is only the **self-relationship** heuristic (`rel.Target.TypeName == entity.Name`). For a cross-entity back-ref (`Loan.borrower → Patron`), the generated `Loan.Create(..., borrower)` gets `null` unless bound. The ADR's "back-references are derived" is future work, not this diff — so the guide overclaims what today's export does.
- Suggestion: narrow the guide to what's true ("the back-ref nav is exempt from required-check because create-in may set it"; remove the "sets borrower to this automatically" promise until the derived back-ref lands), and file the derived-back-ref as the ADR's own follow-up.
- Status: open

### Issue 3 -- Severity: suggestion (3-arg bridge ctor loses navs via `DomainBuilder.Build`)
- File: `Poly/DomainModeling/Builders/DomainBuilder.cs:127` + `Domain.cs` Redistribute
- Description: `Redistribute` replaces `e with { Navigations = rels }`, **dropping any Navigations already on the entity**. `DomainBuilder.Build()` (MCP product path) passes pre-built entities + a separate relationship list — if a builder ever pre-sets navs AND the relationship list, the pre-set navs are silently lost. Today builders don't pre-set navs, so it's latent — but the bridge is the product path and the plan says "retire the bridge in cleanup."
- Suggestion: make `Redistribute` append (`e with { Navigations = [.. e.Navigations, .. rels] }`) or document the drop; prefer retiring the bridge (plan phase 6) so product construction uses entity navs directly.
- Status: open

### Issue 4 -- Severity: suggestion (runtime `ResolveSourceRelationshipOrThrow` reachability)
- File: `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:1038-1063`
- Description: The new reverse-side error ("Entity X is not the source of relationship 'name'. Source is 'Y'.") fires when a name exists on another source. But the caller already resolved the relationship *from its own entity* (`rlm.TryGetRelationship(Entity.Name, ...)`), so on valid domains this only fires on a genuinely wrong-direction authoring — good. However, when `FindByNameAcrossSources` yields the **first** match and there are multiple sources with that name, the error reports an arbitrary one (no source disambiguation in the message). Minor, but a multi-source domain gets a potentially misleading "Source is 'Y'" when several Y exist.
- Suggestion: when >1 source has the name, say "exists on multiple source entities (…)" instead of picking the first — matches the MCP/tool behavior.
- Status: open

### Issue 5 -- Severity: nit (mutation ModifiedNodes loses per-relationship invalidation)
- File: `Poly/DomainModeling/Evolution/DomainMutationContext.cs:88-96`
- Description: `ReplaceInEntity` records only the entity in `ModifiedNodes`, whereas the old relationship mutations added the relationship node. Incremental analysis invalidates the entity's whole subtree (which now includes Navigations), so it's over-invalidation, not under — but the relationship node itself is never re-analyzed directly. Harmless today; worth a comment or a per-nav add if fine-grained invalidation ever matters.
- Status: open

## Verified-clean (adversarial passes came back empty)

- **All consumers migrated**: no remaining `rlm.Relationships.TryGetValue` / flat `RelationshipsByName[name]`; `Domain.Children` no longer carries relationships; analysis passes read `entity.Navigations` or the source-scoped RLM. (Sibling-path check passed — no old-shape stragglers.)
- **Runtime source-sideness**: `ResolveSourceRelationshipOrThrow` preserves fail-closed; reverse-side now reports precisely.
- **Incremental invalidation**: subtree invalidation covers the entity + its navs (verified `IncrementalAnalysisAnalyzer`).
- **MCP `remove`/`describe`**: source-disambiguation when a name is on multiple sources; single-source auto-resolves. Fail-closed on missing.
- **Suite**: 1970 green (own run).

## Process notes

- The `create in Rel` arity bug is the **same class** the export-review series already fixed for the `create Type` path (CS7036) — the `create in Rel` nav-factory path was never given a compile oracle. The E-guard (render + `dotnet build` in-suite) remains the highest-leverage process fix; it would have caught this CS1501.
- The ADR's "back-references are derived" and "auto-wire" are **not delivered** by this change — the ADR/plan should mark them future, and the guide must not claim them as shipped.
- The bridge ctor is a deliberate transition cost, but it is the product path (`DomainBuilder.Build`) — its lossy `Redistribute` deserves at least a comment today.

## Follow-ups (checkable)

- [ ] **R1 (bug)** — fix `create in Rel` CS1501: action-lowering must pass only the nav-method's params; add a render-and-compile test for a target entity with its own `many` navs.
- [ ] **R2** — narrow the DSL guide §0.3 auto-wire claim to what the export does today; file the derived back-ref as the ADR's future phase.
- [ ] **R3** — `Redistribute` append (not replace) navs, or comment the drop; prefer retiring the bridge (plan phase 6) for product construction.
- [ ] **R4** — multi-source name in `ResolveSourceRelationshipOrThrow` → list all sources, not the first.
- [ ] **R5** — comment or implement per-relationship `ModifiedNodes` granularity in `ReplaceInEntity`.
- [ ] **R6 (process)** — add the E-guard (in-suite render + `dotnet build` compile smoke) so nav-factory arity regressions fail in CI, not in dogfood.
