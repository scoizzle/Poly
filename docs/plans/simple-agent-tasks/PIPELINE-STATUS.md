# Pipeline status

**Updated:** 2026-08-25
**Authority:** this file is the **sole CURRENT/DONE truth** for agent suite admission.  
Other indexes must **mirror** this file (or link here) — do not invent a second CURRENT line.

---

## Agent pick (one line)

```text
DONE:    gpure (2026-08-07 + follow-ups 08-08); mcp-minify (2026-08-08 + follow-ups); grammar-revision (2026-08-09: v2 engine + DSL cutover + printer + review fixes); dead-dual cleanup (2026-08-09: Validation + Text.Matching deleted); domainmodeling vision-cleanup slices 1–3 (2026-08-17: one door, session.Analyze, Comment not emit-meaning); emit-session CompileMode seed-only (2026-08-24: HTTP host only via uses http / Load(HttpLibrary); bag-gated emit); host-ABI first slice (StageTransition merged PR 21)
CURRENT: host-ABI (self-invoke on This — in progress / this PR)
ADMIT:   host-ABI
THEN:    host-ABI remaining store effects (create / create-in / for-invoke / cross-entity invoke)
PARKED:  pack-2 IDomainPack; mut-safety; e2e-*; pack-host “packs extend Grammar tables”; session four-slot Meaning/Emit
PULL:    E5; EF codegen; naming cleanup
```

```bash
# Fleet dispatch (see pack-README.md):
opencode run --dir . --auto --title pack-1-1 --agent build "Assigned: docs/plans/simple-agent-tasks/pack-1-1-token-writer.md"
```

---

## Suites

| Suite | Status | Notes |
|-------|--------|-------|
| **gpure** | ✅ **DONE** 2026-08-07 (+ follow-ups 2026-08-08) | Pure Grammar product path (Option A ladder + tables); S1–S5/N1–N4/P1 closed. |
| **mcp-minify** | ✅ **DONE** 2026-08-08 (+ follow-ups same day) | Catalog 46→24; DSL-only expressions; unified `add`/`remove`; follow-ups closed. |
| **grammar-revision** | ✅ **DONE** 2026-08-09 | v2 engine (`Grammar<TToken, TTokenKind>`, examine/consume, longest-match, stateless printer) + DSL cutover; review B1–B3/N1–N3/C1 closed. Executed directly (not via plan-suite) — see [`../grammar-revision.md`](../grammar-revision.md) |
| **emit-session** | ✅ **DONE** 2026-08-24 (CompileMode honesty) | Libraries add `INodeAnalyzer`. Spell closed. Emit reads bags, not `CompileMode`. CompileMode.All/Db seed persistence only; HTTP host requires `uses http` (catalog id `http`) or `Load(HttpLibrary)`. Remaining lies: TemporalLibrary Meaning unused; RuntimeAnalysisCache core-catalog reopen. |
| **host-ABI** | **CURRENT** 2026-08-25 | First slice DONE (StageTransition, PR 21). CURRENT: self-invoke on This (`Invoke(Member(This, action))`, InvokeNamed fallback). Create / create-in / for-invoke / cross-entity invoke still EffectExecutor. Sequential transitions stale SourceStageName. |
| **pack-host** | Parked (phase 1 shipped; **extension model superseded**) | TokenWriter + binders done. “Packs extend Grammar tables” is not the product contract — extension is analysis passes. pack-2 `IDomainPack` parked. |
| **gcyc** | Parked (first admit shipped) | [`gcyc-README.md`](./gcyc-README.md) — remaining G4 unparse is THEN, not CURRENT |
| **grammar wrap-up** | Parked | LeftAssoc live-fold — not a prereq of pack-1 TokenWriter |
| **mut-safety** | Parked | Session lock + idempotent add + rollback DX |
| **p1** temporal | Phase 3a after pack-2-gate | Patterns + binders on both primaries |
| amu / p4 / coh / dogfood / GI / … | Archived | See `docs/plans/archive/` |

---

## Related

| Doc | Role |
|-----|------|
| [`READY-TO-TASK.md`](./READY-TO-TASK.md) | Ready-suite index (mirrors this) |
| [`../v2-to-v3/master-roadmap.md`](../v2-to-v3/master-roadmap.md) | Milestone index + Agent pick (mirrors this) |
| [`../README.md`](../README.md) | Plans admission rules (points here for CURRENT) |
| [`../dead-dual-inventory-2026-08-08.md`](../dead-dual-inventory-2026-08-08.md) | Validation / Text / second-evaluator kill list |
| [`../grammar-revision.md`](../grammar-revision.md) | ✅ **DONE 2026-08-09** — v2 engine + DSL cutover + printer + review fixes closed |

## Notes

- **grammar wrap-up** (admit next): LeftAssoc live-fold + S1 span reconciliation — product fold path.
- **grammar-revision** ✅ DONE 2026-08-09: v2 engine + DSL cutover + printer + review fixes (B1–B3, N1–N3, C1).
- Span-vs-fold `not`-in-chain pinned: `SpanVsFold_NotInChain_TableRejectsFoldAccepts` until wrap-up reconciles.
- **emit-session remaining lies:** TemporalLibrary does not register Meaning handlers (design: dispatch/type-check + `TemporalPass` vocabulary bag). `RuntimeAnalysisCache` / static `DomainModelAnalyzer.Analyze` reopen a core-catalog session (vendor maps ignored). CompileMode seed-only honesty is DONE via #20.
- **host-ABI remaining lie:** create / create-in / for-invoke / cross-entity invoke still EffectExecutor. `ExecuteStructured` remains until mixed if+create can lower. `LowerStageTransitions` still gates those effects (not StageTransition / self-invoke). Sequential transitions stale SourceStageName.
