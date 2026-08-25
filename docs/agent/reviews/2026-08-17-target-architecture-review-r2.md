# Target architecture doc — r2 (re-verify) — 2026-08-17

- **Target**: paths `docs/plans/domainmodeling-target-architecture-2026-08-16.md` (header: edited 2026-08-17 for F1–F11)
- **Mode**: standard, **re-verify** of `2026-08-17-target-architecture-review.md`
- **Issue counts**: 0 bugs, 4 suggestions, 2 nits
- **Verdict**: **Accept as the target-layout sketch.** Prior F1–F11 are actually in the text (not just checked off). Still **do not implement** folder moves from this file. Residual items are mapping holes, not a second architecture.
- **Process notes**: Follow-ups file had already marked F1–F11 `[x]` before this re-verify. Disposition below is from **this** read of the current doc, not from that checkbox list.

## Summary

The edited doc fixes the three r1 bugs: M1 is a split-walk finish, not “stop printf”; `.poly` print is on the pipeline; M1/M3/M4 are “still a lie” until host-ABI, and slice 3 is Comment honesty not M3. §8 lock matches the ADR (pipeline, three nouns, contract fill ≠ library). What remains is incomplete type inventory and one host-emit seam (`IStorageSyntaxEmitter`) still drawn inside DomainModeling.

Oracle: current markdown + `IStorageSyntaxEmitter.cs` / `DomainMember.cs` this pass. No tests.

## Prior issues (r1) — disposition

| r1 | Severity | Status | Evidence in current doc |
|----|----------|--------|-------------------------|
| 1 M1 printf | bug | **fixed** | §3 M1: `Export` = `ToSyntax`; split walk; “not stop printf” |
| 2 never emit text | bug | **fixed** | §0 + §1 print `.poly` via `Language/`; host text out |
| 3 lock M1–M6 | bug | **fixed** | §3 split locked vs after host-ABI; §8–§9; slice 3 ≠ M3 |
| 4 missing homes | suggestion | **fixed** (core) | Bootstrap, Queries, DomainCompilation, ExpressionMeaning, annotation syntax, rewrite-base, §4b lint |
| 5 zero-behavior Ontology | suggestion | **fixed** | Ontology = records + flatten; `Dispatch/` separate |
| 6 exactly two passes | suggestion | **fixed** | DE + effect + `ToSyntax`; no “two” invariant |
| 7 HTTP into Libraries/ | suggestion | **fixed** | §4 last row stay in `src/`; Libraries = Temporal + Storage |
| 8 pure transform | suggestion | **fixed** | §1 one input/output; bags + gated evolution |
| 9 slices = M3 | suggestion | **fixed** | §9 table |
| 10 rename CURRENT | suggestion | **fixed** | §2 callout; §8 no folder CURRENT |
| 11 Ast / rewrite-base / target-only | nit | **fixed** | §6 `Poly.Ast`; §2 target-only; rewrite-base delete-or-Lowering |

## Issues (remaining)

### Issue 1 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:125` (code: `Poly/DomainModeling/Lowering/IStorageSyntaxEmitter.cs:5-34`)
- Description: `IStorageSyntaxEmitter` decorates **host** `CompilationUnitNode`s for `DbContextGenerator` / `MinimalApiGenerator` (`EmitDbContext`, `EmitApi`). §0 says DomainModeling does not emit host text. §4 still offers `Meaning/` or `Libraries/Storage/` as its home — both inside DomainModeling. That keeps a host-emit hook in the front-end compiler. Consumers today are `src/Poly.DslCompiler` only.
- Suggestion: Disposition = **src / vendor assembly** (same as the generators), or delete if unused. Not Meaning (not `.poly` concept binding). Not in-tree `Libraries/Storage` unless a seed library actually implements it.
- Status: open

### Issue 2 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:39-45`
- Description: Ontology list still omits live fact types: `DomainMember`, `DomainObject`, `DomainType` / `DomainTypeReference`, `Constraint` (root + `Constraints/`), `StageSubscription`, `SubscriptionEventAccess`. §4b covered factory/query/lint, not these. An agent moving “the ontology” will leave them at the module root again (today’s README hole).
- Suggestion: Add to Ontology (or Contract/Subscriptions under it). One line in §4b is enough.
- Status: open

### Issue 3 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:99` vs cleanup slice 2
- Description: Locked M2 lists `Emit` as a session operation now. Cleanup slice 2 allows `GenerateAllFiles` to remain a thin loop that only *reads* session analysis. Agents may treat M2 as “delete `GenerateAllFiles` in slice 2.”
- Suggestion: One clause: M2’s `Emit` is the **target**; slices 1–2 require `Analyze` (and parse/print) on the session. `Emit` as `session.Emit` waits until contributors replace the thin loop (later work on the cleanup plan).
- Status: open

### Issue 4 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:168`
- Description: O3 still asks whether vendors live in `src/` or `Libraries/` after §2/§4 already say `src/`. Relitigates F7.
- Suggestion: Close O3 as “`src/` assemblies; mysql stays extra / not a CLI seed.” Remaining question is catalog membership, not folder.
- Status: open

### Issue 5 -- Severity: nit
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:65-66`
- Description: `ExpressionTypeCheckRegistry` is a target `Meaning/` file; the live type is `Analysis/ExpressionTypeCheckRegistry.cs` and feeds `ExpressionTypeAnalyzer`. Putting the registry in Meaning and the pass in Analysis is defensible (config vs bag) but easy to fork again.
- Suggestion: One sentence: registry stays next to the pass under `Analysis/`, or Meaning holds only fold/print/default tables.
- Status: open

### Issue 6 -- Severity: nit
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:52`
- Description: `DomainSession` comment is `parse · analyze · lower · print · emit`. §0 says host emit is outside DomainModeling. “Emit” here means `IArtifactContributor` files, not `CSharpGenerator`. Same word as M1/M2 host emit.
- Suggestion: `parse · analyze · print(.poly) · lower · contribute artifacts`.
- Status: open

## What is sound

Pipeline, three nouns, `.poly` vs host text, M3 not license to delete `ExecuteStructured`, vendors in `src/`, not CURRENT, cleanup remains the executable plan.
