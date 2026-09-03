# Pipeline status

**Updated:** 2026-09-01
**Authority:** this file is the **sole CURRENT/DONE truth** for agent suite admission.  
Other indexes must **mirror** this file (or link here) — do not invent a second CURRENT line.

---

## Agent pick (one line)

```text
DONE:    gpure (2026-08-07 + follow-ups 08-08); mcp-minify (2026-08-08 + follow-ups); grammar-revision (2026-08-09: v2 engine + DSL cutover + printer + review fixes); dead-dual cleanup (2026-08-09: Validation + Text.Matching deleted); domainmodeling vision-cleanup slices 1–3 (2026-08-17: one door, session.Analyze, Comment not emit-meaning); emit-session CompileMode seed-only (2026-08-24: HTTP host only via uses http / Load(HttpLibrary); bag-gated emit); host-ABI StageTransition (PR 21); host-ABI self-invoke (PR 22); host-ABI cross-entity invoke (PR 23); host-ABI for-invoke (PR 24); rewrite-to-master (PR 26); interpretation-language-engine (ile-gate 2026-08-31)
CURRENT: create/create-in
ADMIT:   parallel (exclusive files)
THEN:    MCP mut-safety; Grammar wrap-up; V3 naming
PARKED:  pack-2 IDomainPack; mut-safety; e2e-*; pack-host “packs extend Grammar tables”; session four-slot Meaning/Emit
PULL:    E5; EF codegen; naming cleanup
```

```bash
# Fleet dispatch (see pack-README.md). pack-1 is archived — assign a live parked task file.
```

---

## Suites

| Suite | Status | Notes |
|-------|--------|-------|
| **gpure** | ✅ **DONE** 2026-08-07 (+ follow-ups 2026-08-08) | Pure Grammar product path (Option A ladder + tables); S1–S5/N1–N4/P1 closed. |
| **mcp-minify** | ✅ **DONE** 2026-08-08 (+ follow-ups same day) | Catalog 46→24; DSL-only expressions; unified `add`/`remove`; follow-ups closed. |
| **grammar-revision** | ✅ **DONE** 2026-08-09 | v2 engine (`Grammar<TToken, TTokenKind>`, examine/consume, longest-match, stateless printer) + DSL cutover; review B1–B3/N1–N3/C1 closed. Archived: [`../archive/completed-2026-08-late/grammar-revision.md`](../archive/completed-2026-08-late/grammar-revision.md) |
| **emit-session** | ✅ **DONE** 2026-08-24 (CompileMode honesty) | Libraries add `INodeAnalyzer`. Spell closed. Emit reads bags, not `CompileMode`. CompileMode.All/Db seed persistence only; HTTP host requires `uses http` (catalog id `http`) or `Load(HttpLibrary)`. Remaining lies: TemporalLibrary Meaning unused; RuntimeAnalysisCache core-catalog reopen. |
| **host-ABI** | Strong slice **DONE** (PRs 21–24) | StageTransition, self-invoke, cross-entity, for-invoke same-tree. Create / create-in still EffectExecutor — **not** the rewrite-to-master gate. |
| **interpretation-language-engine** | ✅ **DONE** 2026-08-31 | ile-0…ile-3 + ile-gate: no POC passthrough, `Compile` fail-closed, LanguageVmTests + LanguageSurfaceTests, CORE/README match. Plan: [`../archive/completed-2026-08-late/simple-agent-tasks/interpretation-language-engine-README.md`](../archive/completed-2026-08-late/simple-agent-tasks/interpretation-language-engine-README.md). |
| **rewrite-to-master** | ✅ **DONE** 2026-08-25 (PR 26) | Rewrite is `master`. Plan: [`../archive/completed-2026-08-late/simple-agent-tasks/rewrite-to-master-2026-08-25.md`](../archive/completed-2026-08-late/simple-agent-tasks/rewrite-to-master-2026-08-25.md). New work from `master`; do not open work on the rewrite branch. |
| **pack-host** | Parked (phase 1 shipped; **extension model superseded**) | TokenWriter + binders done. “Packs extend Grammar tables” is not the product contract — extension is analysis passes. pack-2 `IDomainPack` parked. |
| **gcyc** | Parked (first admit shipped) | [`gcyc-README.md`](./gcyc-README.md) — remaining G4 unparse is THEN, not CURRENT |
| **grammar wrap-up** | Parked | LeftAssoc live-fold — not a prereq of pack-1 TokenWriter |
| **mut-safety** | Parked | Session lock + idempotent add + rollback DX |
| **p1** temporal | Phase 3a after pack-2-gate | Patterns + binders on both primaries |
| amu / p4 / coh / dogfood / GI / gpure / mcp-minify / ile / … | Archived | See [`../archive/`](../archive/) — latest bucket [`completed-2026-08-late`](../archive/completed-2026-08-late/README.md) |

---

## Related

| Doc | Role |
|-----|------|
| [`../archive/completed-2026-08-late/simple-agent-tasks/rewrite-to-master-2026-08-25.md`](../archive/completed-2026-08-late/simple-agent-tasks/rewrite-to-master-2026-08-25.md) | Merge rewrite onto master — ✅ DONE PR 26 |
| [`READY-TO-TASK.md`](./READY-TO-TASK.md) | Ready-suite index (mirrors this) |
| [`../v2-to-v3/master-roadmap.md`](../v2-to-v3/master-roadmap.md) | Milestone index + Agent pick (mirrors this) |
| [`../README.md`](../README.md) | Plans admission rules (points here for CURRENT) |
| [`../archive/completed-2026-08-late/dead-dual-inventory-2026-08-08.md`](../archive/completed-2026-08-late/dead-dual-inventory-2026-08-08.md) | Validation / Text / second-evaluator kill list (executed) |
| [`../archive/completed-2026-08-late/grammar-revision.md`](../archive/completed-2026-08-late/grammar-revision.md) | ✅ **DONE 2026-08-09** — v2 engine + DSL cutover + printer + review fixes closed |

## Notes

- **grammar wrap-up** (admit next): LeftAssoc live-fold + S1 span reconciliation — product fold path.
- **grammar-revision** ✅ DONE 2026-08-09: v2 engine + DSL cutover + printer + review fixes (B1–B3, N1–N3, C1).
- Span-vs-fold `not`-in-chain pinned: `SpanVsFold_NotInChain_TableRejectsFoldAccepts` until wrap-up reconciles.
- **emit-session remaining lies:** TemporalLibrary does not register Meaning handlers (design: dispatch/type-check + `TemporalPass` vocabulary bag). `RuntimeAnalysisCache` / static `DomainModelAnalyzer.Analyze` reopen a core-catalog session (vendor maps ignored). CompileMode seed-only honesty is DONE via #20.
- **host-ABI remaining lie:** `EffectExecutor` deleted (all arms threw). Leaf creates now lower through VM via `InvokeNamed` runtime factories. `ExecuteStructured` remains for unique-assign if/else and store-coupled creates (`CreateEntityInstance` with `RelationshipName`). `LowerStageTransitions` still gates create shape (C# `Stay.Create` vs runtime `CreateByType`/`CreateInNav`). Sequential transitions SourceStageName **fixed** (updates after each transition). Emit path runs `Interpreter.Analyze` on the full projected unit (including `DomainResult<T>`); type-parameter / closed-generic / short-name resolve lives in Interpretation.
- **rewrite-to-master:** ✅ DONE PR 26. Product trunk is `master`. Parallel streams may run with exclusive file ownership (create/create-in; MCP mut-safety; Grammar wrap-up; V3 naming).
- **interpretation-language-engine:** ✅ DONE 2026-08-31 (ile-gate). CURRENT is create/create-in (host-ABI remaining store effects).
