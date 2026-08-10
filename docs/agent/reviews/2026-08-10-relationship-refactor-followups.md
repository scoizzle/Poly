# Follow-ups — relationship refactor review (2026-08-10)

Review note: [`../agent/reviews/2026-08-10-relationship-refactor-review.md`](../agent/reviews/2026-08-10-relationship-refactor-review.md)

## Resolved (2026-08-10, same commit)

- [x] **R1 (bug)** — `create in Rel` CS1501 export arity fixed: `EffectLoweringPass.CreateEntityInRelationship` now skips collection navs (and back-refs) when building the call, matching `AddCreateNavMethod`'s factory signature — identical arg lists by construction. Verified by compiling the exact `csharp-export-createin-bugs` repro (export → render → `dotnet build`, 0 errors). Guard test: `Export_CreateInTargetWithCollectionNavs_SignatureMatchesCallArity`.
- [x] **R2 (doc honesty)** — `Poly.Mcp/Docs/poly-dsl-guide.md` §0.3 narrowed to what the export does: runtime store link is created; the C# back-ref property is **not** auto-populated (ctor param, `null` unless bound); to-one nav bindings rejected in `create in` initializers. Derived back-ref materialization remains the ADR's future phase.
- [x] **R3 (bridge footgun)** — `Domain.Redistribute` now **appends** to pre-set `Navigations` (`[.. e.Navigations, .. rels]`) instead of replacing. Bridge retirement still planned (synthesis plan phase 6).
- [x] **R4 (multi-source)** — `DomainEntityInstance.ResolveSourceRelationshipOrThrow` reports **all** declaring source entities, not the first.
- [x] **R5 (nit)** — accepted as-is: `ReplaceInEntity` marks the whole entity modified on relationship-content updates, which is correct (the entity changed); per-nav granularity has no current consumer.

## Open

- [ ] **R6 (process)** — E-guard: add an in-suite render-and-compile oracle (Roslyn `CSharpCompilation` or an in-suite `dotnet build`) for the export, so nav-factory arity regressions fail in CI. This class (CS7036/CS1501) has now recurred 4× without a compile oracle. The structural arity guard added for R1 is a stopgap; a true compile oracle is the durable fix.

## Disposition of prior review items (re-verified)

- 2026-08-10-lowering-findings-review R1–R4: fixed (verified in current source).
- 2026-08-09-csharp-export-review E-guard: **still open** — subsumed by R6 here.
