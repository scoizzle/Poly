# PR 83 (c2265740) Final Boss follow-ups — 2026-09-28

## Open

- **F34 (nit)** — A2 advertises the `not(...)` unlinked case as True, but no committed test pins the negation; only the direct False/False form is committed. Add one `not(...)` assertion or drop the negation from the claim. `Poly.Tests/DomainModeling/Lowering/QuantifierLoopTests.cs:650-654`.
- **F35 (nit)** — The new A1 test asserts the literal generated names `previousStage0`/`previousStage1`, coupling it to the shared `LocalNames` counter offset for one model. Assert distinct/duplicate-free `previousStage` locals instead. `Poly.Tests/DomainModeling/Compile/WhenAllSimulatePrintAgreeTests.cs:165-166`.

## Closed

- **A1** — `previousStage` names come from the shared `LocalNames` (`EffectLoweringPass.cs:525`), one source per method (`DomainToCSharpExporter.Actions.cs:110,133,144,453`); pinned by `WhenAllSimulatePrintAgreeTests.cs:141-192`.
- **A2** — Unlinked path-prefix guard rebuilt so a statement-bearing predicate leaf runs inside `if (rel != null)` with `t` defaulted false (`DomainExpressionLoweringPass.cs:187-199`); pinned by `QuantifierLoopTests.cs:650-654` and `:790-793`.
- **A3** — `SourceEntityName` set at every production call site; hop leaves lower against the target name + enum map (`DomainExpressionLoweringPass.cs:183-186,510-525`); pinned by `QuantifierLoopTests.cs:658-718,721-794,797-844`.
- **F1** — Hop retargets `SourceEntityName` via `LowerAgainstEntity` (`DomainExpressionLoweringPass.cs:183-186`); pinned by the collision test `QuantifierLoopTests.cs:658-718`.
- **F2** — Ctor doc now says the root is the `subject` argument and `LoweringContext.Subject` is not read by this pass (`DomainExpressionLoweringPass.cs:55-59`); grep-confirmed.
- **F3** — Comment now says "statement-free predicate leaf" (`DomainExpressionLoweringPass.cs:170-176`), matching `:187-199`.
- **F4** — Ctor doc states the effect's entity always overwrites `SourceEntityName` (`EffectLoweringPass.cs:50-54`).
- **G2** — `SourceEntityName` docs reworded at `LoweringContext.cs:79-88` and `DomainExpressionLoweringPass.cs:64-68`: only the name and enum map retarget; the three resolvers stay source-scoped; binder roots keep the source name and enum map. No hop/binder overclaim remains.
- **G3** — Two-hop collision and direct-hop enum tests added (`QuantifierLoopTests.cs:721-794,797-844`); both would fail at `4b28ed3f`.

## Carried out-of-scope

- **W4** — `Age < advisor Age` with `advisor` unlinked still throws in simulate and NREs in print at this head (no value-hop guard). PR 82 covers it.
- **A4** — Notify name helper/rewrite deliberately not taken; helpers absent and emit/parse are master's inline form. PR 82 deletes the rewrite.
- **G1 (pre-existing, queued)** — Hop property-type / navigation-name / collection resolvers and binder roots stay source-scoped: `team Due + 1 > Start` prints `this.Team!.Due + 1L` (CS0019) and `line Kind == "Rush"` prints `target0.Kind == "Rush"` (CS0019). `LowerAgainstEntity` (`DomainExpressionLoweringPass.cs:510-525`) swaps only the name and enum map; the docs are honest about it. Not blocking.
- **G1-adjacent (pre-existing)** — `PolicyConstraintAnalyzer` validates nested-quantifier body properties against the outer item entity (`PolicyConstraintAnalyzer.cs:311-336`) and skips hop-target expressions (`:90-95`). Master is identical. This is the same root as PR 79 F25 (parked). Fold it into the G1 follow-up.
