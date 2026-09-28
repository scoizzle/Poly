# PR 79 Final Boss — follow-ups (63bad953)

Companion to `docs/agent/reviews/2026-09-27-pr79-63bad953-final-boss.md`. Plain-English open items from this review plus the carried known items. No product code is changed by these notes.

## Open from this review

- [ ] **F28 (suggestion)** — `docs/interpretation/domain-execution-model.md` (`:22`, `:160`, `:168`, `:219-220`, `:224`, `:228`, `:281`, `:344`, `:347`, `:358`) and `docs/interpretation/README.md:10` still describe the deleted `AnyRelated`/`AllRelated`/`NoneRelated`/`CountRelated` Store jobs, the deleted export throw, the old `DomainExpressionDispatch<Node>` pass shape, and "per-target re-lowering inside the Store job". Rewrite these sections to describe the foreach lowering. PR 80 does not touch this doc today. This is the only reason the hand-edit gate is NO; Scot may waive it.
- [ ] **F29 (nit)** — `Poly.Mcp/Docs/poly-dsl-guide.md:556-561` opens with "at any depth … is rejected" and then adds the "Not yet checked … nested quantifier whose relationship does not resolve" carve-out; qualify the leading universal ("while every relationship in the path resolves") so the two sentences agree.
- [ ] **F30 (nit)** — `docs/plans/simple-agent-tasks/e2e-2-README.md:6` replaced the suite's `` `[ ]` ``/`` `[x]` `` Status encoding with a prose paragraph; content is accurate but it can no longer be scanned and reads like a second DONE signal. Keep the prose as a note and restore `` **Status:** `[ ]` ``.

## Carried known / accepted items (unchanged, not raised as blockers)

- **F14 (deferred)** — `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs:355` still calls `Store.GetRelatedInstances` for the OneToOne nav read (the outbound-only switch only covered collection navs).
- **F17 (pre-existing)** — `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs:202-203`: an unlinked path-prefix hop simulates false but the export `NullForgiving`-derefs and throws NRE.
- **F20 remainder (PR 80)** — `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:433` comment still says "VM StoreQuantifier path requires UseThisReference:false (any/all/none)."
- **F24 (parked, pre-existing)** — a Number-valued policy simulates as nonzero but prints `bool P() => this.N` (CS0029), and a plain scalar `exists` differs between simulate and print. Pinned at `Poly.Tests/DomainModeling/Lowering/QuantifierLoopTests.cs:380-396`.
- **F25 (parked, pre-existing)** — `Poly/DomainModeling/Analysis/PolicyConstraintAnalyzer.cs:311-336` does not validate a quantifier nested inside another quantifier's body (its property reads are checked against the wrong target entity, and its relationship is never resolved). The `for`-predicate nested case is therefore still unpinned in tests; simulate/export fail loud rather than silently.
