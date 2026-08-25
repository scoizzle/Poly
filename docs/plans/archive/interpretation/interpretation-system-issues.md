> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Interpretation System — Tracked Issues

**Created:** 2026-07-05  
**Source:** First-principles code review of `Poly/Interpretation/` (analysis passes, VM, backends, docs).  
**Scope:** Consolidation, semantic gaps, primitive IR maturity, documentation hygiene, and deferred ADR work — not net-new platform features.

## Status Legend

| Status | Meaning |
|--------|---------|
| `open` | Not started |
| `in-progress` | Actively being worked |
| `blocked` | Waiting on a dependency or decision |
| `done` | Resolved; link PR or commit in Notes |

## Priority Tiers

| Tier | When to act |
|------|-------------|
| **P0** | **Active focus** — analysis-first work that unblocks the primitive IR roadmap; do these before other INT/ANA items |
| **P1** | Correctness risk or active duplication of canonical semantics |
| **P2** | Should fix before the next major Interpretation milestone |
| **P3** | Hygiene, documentation, or defer-until-first-consumer |

---

## P0 — Analysis-First Sprint (active)

**Goal:** Front-load the three highest-leverage AST passes so primitive IR work (INT-018, INT-019, INT-002, INT-028) becomes metadata lookup instead of re-analysis at expansion time.

**Pipeline insertion (target order):**

```
 1. TypeAndMemberResolver
 2. ScopeValidator
 3. SideEffectAnalyzer
 4. ThisReferenceContext
 5. JumpTargetResolution
 6. ControlFlowAnalysis
 7. ── ANA-001 ValueRepresentationPass      ← NEW
 8. ── ANA-004 CallSiteCatalogPass          ← NEW
 9. ConstantFolding
10. DefiniteAssignment
11. LambdaReturnType
12. ── ANA-003 ExceptionRegionAnalysisPass ← NEW
13. ExpansionPass
```

Register new passes in `Interpreter.cs` and update `Poly/Interpretation/Analysis/README.md` pass table when each lands.

**Sprint exit criteria:** All three passes implemented with tests; at least one downstream consumer wired (expansion or `InterpretResult`) per pass; `dotnet run --project Poly.Tests/Poly.Tests.csproj` green.

**Implementation status (2026-07-05, final):** **1395/1395 tests green, G-build green.** ANA-001, ANA-003, ANA-004 **done**. Sprint **complete.** Next: INT-018, INT-019, INT-028.

---

## P0 Fix-Up — Post-Implementation Review

**Source:** Code review of first implementation pass (ANA-001, ANA-004, ANA-003), plus second review of fix-up changes (2026-07-05).  
**Priority:** State bugs (**ANA-FIX-001/002**) and regression tests (**ANA-FIX-014**) verified done. Remaining sprint closure: see **Remaining work** under P0 Sprint Wrap-Up.

**Fix order:**

```
ANA-FIX-001, ANA-FIX-002     (stateful analyzer bug — done; verify via ANA-FIX-014)
    ↓
ANA-FIX-003, ANA-FIX-015, ANA-FIX-022   (InterpretResult consumer + tests)
ANA-FIX-004, ANA-FIX-005, ANA-FIX-006   (call site correctness + tests)
ANA-FIX-007, ANA-FIX-008, ANA-FIX-010    (EH semantics + tests)
ANA-FIX-012, ANA-FIX-018, ANA-FIX-019    (value representation accuracy + tests)
ANA-FIX-011, ANA-FIX-017                 (ToPrimitives consumers + constructor alignment)
ANA-FIX-016                              (incremental analysis state — before incremental consumers)
ANA-FIX-009, ANA-FIX-013, ANA-FIX-021    (deferrable nits / hygiene)
```

---

### ANA-FIX-001 — Stateful `CallSiteCatalogAnalyzer` corrupts cross-run catalog

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** |
| **Area** | ANA-004 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs` |
| **Problem** | `_catalog` and `_depth` are instance fields on a pass instance reused by the cached `Interpreter._analyzer` singleton. Lists are never cleared between `Analyze()` calls. Second analysis accumulates stale entries; catalog indices drift. |
| **Action** | Make analyzer stateless. Move accumulator to per-traversal state on `AnalysisContext` (same pattern as `ExpansionContext` on `null` key, or fresh `CallSiteCatalogState` created when `_depth` enters at root). Clear/finalize catalog when root visit completes. |
| **Acceptance** | Test: same `Analyzer` instance, two different AST roots analyzed sequentially — second catalog contains only second tree's sites, indices start at 0. |
| **Pattern** | Follow `ControlFlowAnalysisPass` (`var state = new CfgState()` per root visit) or `ExpansionPass` (`ExpansionContext` on context metadata). |
| **Notes** | Fixed: `CallSiteCatalogState` on `AnalysisContext.Metadata` via `GetOrAdd`. Fresh context per `Analyze()` isolates state on the `Interpreter` path. Regression test not yet added — see **ANA-FIX-014**. Incremental path risk — see **ANA-FIX-016**. |

---

### ANA-FIX-002 — Stateful `ExceptionRegionAnalyzer` corrupts cross-run regions

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** |
| **Area** | ANA-003 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/ExceptionRegionAnalysisPass.cs` |
| **Problem** | `_regions`, `_protectedNodeIds`, and `_depth` are instance fields on reused pass instance. Never cleared between analyses. Region table and protected-node marks leak across programs. |
| **Action** | Same fix as ANA-FIX-001: per-`AnalysisContext` accumulator, not instance fields. |
| **Acceptance** | Test: analyze tree with try/catch, then analyze plain block with same `Analyzer` — second result has `GetExceptionRegions() == null` (or empty), no stale regions from first tree. |
| **Notes** | Fixed: `ExceptionRegionState` on context metadata (same pattern as ANA-FIX-001). Regression test not yet added — see **ANA-FIX-014**. Incremental path risk — see **ANA-FIX-016**. |

---

### ANA-FIX-003 — `InterpretResult` ignores `ValueRepresentationMetadata` (ANA-001-T9)

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** (INT-002 still present) |
| **Area** | ANA-001 consumer |
| **Files** | `Poly/Interpretation/Interpreter.cs` (`InterpretResult`) |
| **Problem** | Pass stamps metadata on every node but `InterpretResult` still uses heap-handle range heuristic (`handle > 1 && handle < heap.Count`). Sprint consumer not wired. |
| **Action** | `InterpretResult` (or `CompileCore` + execution path) must accept optional `AnalysisResult` or root `ValueRepresentationMetadata`. When `Kind == HeapRef`, dereference handle; when `StackScalar`/`Bool`, return raw `long` without dereference. Fall back to heuristic only when metadata absent. |
| **Acceptance** | Test: program returns integer `2` with populated heap; `GetValue<int>()` returns `2`. Existing `ValueRepresentationTests` unchanged; add `Interpreter_Execute_IntResult_NotDereferencedAsHeapHandle` integration test. |
| **Related** | INT-002 |
| **Notes** | Partial: `CompileCore` stamps `VmProgram.RootValueKind`; `InterpretResult` branches on `StackScalar`/`Bool`/`HeapRef`. Heuristic fallback retained when kind is `Unknown` — see **ANA-FIX-022**. Integration test task split to **ANA-FIX-015**. |

---

### ANA-FIX-004 — `CallSiteEntry.ArgCount` mismatches `CallExternal` emission

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** |
| **Area** | ANA-004 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs` (`ProcessInvoke`, `ProcessMember`, `ProcessNew`) |
| **Problem** | Catalog stores `MethodInfo.GetParameters().Length`. `Invoke.ToPrimitives` emits `CallExternal` with `argCount = paramCount + (isStatic ? 0 : 1)` (instance included). Catalog and IR will disagree for instance methods. |
| **Action** | Align `CallSiteEntry.ArgCount` with the same convention `CallExternal` uses in `Invoke.cs` / `Member.cs` / `ProgramCompiler.EmitCallExternalDirect`. Document convention on `CallSiteEntry`. |
| **Acceptance** | Unit test: instance method `Invoke` entry has `ArgCount` including receiver slot. |
| **Notes** | Fixed in `ProcessInvoke`/`ProcessMember`. Unit test still missing — covered by **ANA-FIX-005**. Constructor `+1` convention may not match `New.ToPrimitives` — see **ANA-FIX-017**. |

---

### ANA-FIX-005 — Call site catalog tests omit real CLR dispatch

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** (coverage gap) |
| **Area** | ANA-004 tests |
| **Files** | `Poly.Tests/Interpretation/CallSiteCatalogTests.cs` |
| **Problem** | Tests only cover lambda invoke (no catalog) and empty trees. No test exercises resolved CLR `Invoke`/`Member`/`New`. ANA-004-T10 acceptance criteria unmet. |
| **Action** | Add tests mirroring `MethodInvocationSemanticResolutionTests` patterns: `Invoke(Member("hello"), "IndexOf", 'e')` gets index; duplicate invoke shares index; distinct methods differ; `New` with resolved constructor gets `IsConstructor = true`. Include `UseValueRepresentationAnalysis()` in test pipeline if needed for parity with production. |
| **Acceptance** | ANA-004-T10 checklist satisfied. |
| **Notes** | Verified 2026-07-05: CLR invoke, dedup, distinct methods, unresolved, `ArgCount`, overload indices, sequential leak — **done**. **Open:** `New_ResolvedConstructor_GetsSiteIndex` (SPRINT-W4-T1). |

---

### ANA-FIX-006 — Call site catalog API: null vs empty list

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **nit** |
| **Area** | ANA-004 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs` |
| **Problem** | `CallSiteCatalogMetadata` only stored when `_catalog.Count > 0`; otherwise `GetCallSiteCatalog()` returns `null`. Consumers must null-check inconsistently. |
| **Action** | Always stamp `CallSiteCatalogMetadata` on root at end of traversal (empty list when no sites). Update tests expecting `null` to expect empty list. |
| **Acceptance** | `GetCallSiteCatalog()` never null after full analysis; may be empty. |
| **Notes** | Fixed: root always stamped with empty or populated catalog. Tests updated to expect non-null empty list. |

---

### ANA-FIX-007 — `UsingStatement` region protected/handler IDs inverted

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** |
| **Area** | ANA-003 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/ExceptionRegionAnalysisPass.cs` (`ProcessUsingStatement`) |
| **Problem** | `ProtectedNodeIds` = body subtree; `HandlerNodeIds` = resource expression only. Correct lowering shape is: protected = resource acquisition + body; handler = dispose call site (from ANA-004 catalog). Current layout will mislead INT-018 lowering. |
| **Action** | `ProtectedNodeIds` = resource node IDs + body subtree IDs. `HandlerNodeIds` = dispose call site node ID(s) once ANA-004 resolves `Dispose`/`DisposeAsync` on resource type (or store `CallSiteIndex` on region entry). |
| **Acceptance** | Test: `UsingStatement` region has resource+body in protected set; handler references dispose site. |
| **Related** | ANA-003-T7, ANA-003-T12 |
| **Notes** | Fixed + tested (`UsingStatement_ProducesUsingDisposeRegion`). |

---

### ANA-FIX-008 — Exception regions ignore CFG reachability

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Severity** | **suggestion** |
| **Area** | ANA-003 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/ExceptionRegionAnalysisPass.cs` |
| **Problem** | ANA-003-T5 required reading `ControlFlowMetadata`. Implementation only walks AST subtrees. Unreachable catch clauses (CFG diagnostic `CF0010`) still get `Catch` region entries. |
| **Action** | Skip catch region emission when catch body has `ElisionMetadata.CanElide` or CFG marks clause unreachable (mirror `BuildTryCatchCfg` `mayThrow` / `MarkSubtreeElidable` logic). |
| **Acceptance** | Test: try block with no throw → no catch regions emitted (or catch marked elided). |
| **Related** | ANA-003-T5, ANA-003-T10 |

---

### ANA-FIX-009 — `CatchTypeId` uses unstable `GetHashCode()`

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Severity** | **nit** (documented placeholder) |
| **Area** | ANA-003 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/ExceptionRegionAnalysisPass.cs` |
| **Problem** | `typeId = resolvedType.GetHashCode()` is process-local and unstable. Unsuitable for serialization. |
| **Action** | Until ANA-002 lands: store stable string identity on `ExceptionRegionEntry` (e.g. `CatchTypeName`) alongside nullable `CatchTypeId`, or use ordinal index into a type table on the metadata record. Do not ship `GetHashCode` as the long-term ID. |
| **Acceptance** | Same type always yields same ID within a process; ID documented as interim until ANA-002. |
| **Related** | ANA-002 |
| **Notes** | `CatchTypeName` interim ID landed. `CatchTypeId` hash deferred until **ANA-002** — acceptable placeholder. |

---

### ANA-FIX-010 — Missing EH tests (throw-in-try, using, nested try)

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** |
| **Area** | ANA-003 tests |
| **Files** | `Poly.Tests/Interpretation/ExceptionRegionAnalysisTests.cs` |
| **Problem** | ANA-003-T10 partially met. Missing: throw inside try → `IsInProtectedRegion == true`; `UsingStatement` region test; nested `TryCatchFinally`. |
| **Action** | Add tests per ANA-003-T10 acceptance list. |
| **Acceptance** | Throw inside try marked protected; throw outside not marked; using produces `UsingDispose` region. |
| **Notes** | Verified 2026-07-05: throw-in-try, using, nested inner throw, sequential leak — **done**. **Open:** assert throw **in catch clause** is `IsInProtectedRegion == false` (SPRINT-W4-T5/V6). |

---

### ANA-FIX-011 — No downstream consumers wired (sprint exit criteria)

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** (sprint incomplete) |
| **Area** | Consumers |
| **Files** | `Poly/Interpretation/Interpreter.cs`, `Poly/Syntax/Nodes/Invoke.cs`, `Member.cs`, `New.cs`, `TryCatchFinally.cs`, `UsingStatement.cs` |
| **Problem** | Sprint exit criteria require one consumer per pass. None wired except pipeline registration. ANA-001-T9, ANA-004-T11/T12, ANA-003-T11/T12 all open. |
| **Action** | Minimum viable consumers: (1) `InterpretResult` + metadata (ANA-FIX-003); (2) `ToPrimitives` reads `CallSiteIndexMetadata` when present; (3) `TryCatchFinally.ToPrimitives` reads `GetExceptionRegions()` and emits EH label placeholders. |
| **Acceptance** | Sprint exit criteria in P0 section satisfied. |
| **Notes** | Verified 2026-07-05: **ANA-001** `InterpretResult` ✅; **ANA-003** `TryCatchFinally`/`UsingStatement`/`ThrowStatement` placeholders ✅; **ANA-004** `Invoke`/`Member` `SiteIndex` on `CallExternal` + `VmProgram.CallSites` ✅. **Open:** `New.ToPrimitives` unwired; `ProgramCompiler` does not pass `ec.SiteIndex` or resolve from catalog; expansion integration tests missing (SPRINT-W2-V1, W3-V1). |

---

### ANA-FIX-012 — `ValueRepresentation` classification gaps

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** |
| **Area** | ANA-001 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs` |
| **Problem** | (1) `Coalesce` hardcoded `HeapRef` — wrong for `int ?? int`. (2) Arithmetic ops ignore `GetResolvedType(node)` — `ClrType` always null. (3) `Constant(null)` classified `HeapRef` but VM uses `0L` on stack. (4) `Conditional` always `Unknown` — could propagate branch metadata. |
| **Action** | `Coalesce`/`Conditional`: use resolved type or propagate from children. Arithmetic: set `ClrType` from `GetResolvedType`. `null` constant: `StackScalar` or document as sentinel. |
| **Acceptance** | Tests: `Coalesce(intConst, intConst)` → `StackScalar`; `Add` nodes carry non-null `ClrType` when type resolver ran. |
| **Notes** | Done: coalesce/conditional/arithmetic/null classification + tests. Remaining test gap: **SPRINT-W4-T6**. |

---

### ANA-FIX-013 — `CallSiteEntry` identity collides on overloads

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **nit** (known limitation) |
| **Area** | ANA-004 |
| **Files** | `Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs` (`BuildIdentity`) |
| **Problem** | Identity is `Type.Method(paramCount)` only. Overloads with same arity collide (e.g. `Substring(int)` vs `Substring(int,int)`). |
| **Action** | Extend identity with parameter type names or full signature string per sandboxing ADR. Add test documenting collision is fixed or explicitly deferred with comment. |
| **Acceptance** | Two same-arity overloads on same type get distinct indices, or issue documents deferral with ADR reference. |
| **Notes** | Fixed: `BuildIdentity` includes comma-separated parameter type full names. Add overload collision test as part of **ANA-FIX-005**. |

---

### ANA-FIX-014 — Multi-analyze regression tests (state isolation)

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** (acceptance gap — locks ANA-FIX-001/002) |
| **Area** | ANA-004, ANA-003 tests |
| **Files** | `Poly.Tests/Interpretation/CallSiteCatalogTests.cs`, `ExceptionRegionAnalysisTests.cs` |
| **Problem** | ANA-FIX-001 and ANA-FIX-002 are fixed in code but acceptance tests were never added. Without sequential-analysis tests, state leakage could regress silently. |
| **Action** | Add tests using a **single cached `Analyzer` instance** (mirror `Interpreter._analyzer` reuse): (1) analyze tree A with CLR invoke or try/catch, then analyze tree B with no call sites / no EH — assert B's catalog is empty and regions count is 0; (2) assert A's site indices are not present in B's catalog. |
| **Acceptance** | `SameAnalyzer_TwoSequentialAnalyses_NoCatalogLeak` and `SameAnalyzer_TwoSequentialAnalyses_NoRegionLeak` pass. |
| **Notes** | Done in third implementation round. |

---

### ANA-FIX-015 — INT-002 integration test for `InterpretResult` (ANA-FIX-003 completion)

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** (INT-002 regression guard) |
| **Area** | ANA-001 consumer |
| **Files** | `Poly.Tests/Interpretation/` (new or existing VM execution test file) |
| **Problem** | `RootValueKind` is wired into `VmProgram` and `InterpretResult`, but no end-to-end test proves an integer scalar result is not dereferenced as a heap handle when the heap is populated. ANA-FIX-003 acceptance criteria unmet. |
| **Action** | Add `Interpreter_Execute_IntResult_NotDereferencedAsHeapHandle`: compile and execute a program whose root expression is `Constant(2)` (or `Add(1,1)`) via standard `Interpreter` pipeline; pre-populate or naturally populate heap so handle `2` would be a valid index; assert `GetValue<int>() == 2` and result is not a heap object reference. |
| **Acceptance** | Test fails if heuristic-only path is restored; passes with `RootValueKind == StackScalar`. |
| **Related** | INT-002, ANA-001-T9, ANA-FIX-003 |
| **Notes** | Verified: 5 tests in `InterpretResultIntegrationTests.cs`; heap pre-seeded with 3 `Allocate` calls; `StandardPipeline_SetsRootValueKind` covers scalar/heap/bool. |

---

### ANA-FIX-016 — Incremental analysis reuses mutable pass state from cloned metadata

| Field | Value |
|-------|-------|
| **Status** | `in-progress` |
| **Severity** | **bug** (latent — incremental path only) |
| **Area** | ANA-004, ANA-003 |
| **Files** | `CallSiteCatalogPass.cs`, `ExceptionRegionAnalysisPass.cs`, `Poly/Syntax/Analysis/AnalysisContext.cs` |
| **Problem** | `CallSiteCatalogState` and `ExceptionRegionState` use `context.Metadata.GetOrAdd(null, …)`. Fresh `Analyze()` creates a new store (safe). Incremental `Analyze(root, priorAnalysis, invalidatedNodes)` clones `priorAnalysis.GetMetadataStore()` — prior traversal accumulators (`Catalog`, `Regions`, `ProtectedNodeIds`) are reused and never cleared. Second pass appends stale entries. |
| **Action** | At pass entry when `state.Depth` transitions 0→1 (root entry), clear `Catalog`/`Regions`/`ProtectedNodeIds` and reset counters; **or** replace `GetOrAdd` with a root-local `new State()` pattern matching `ControlFlowAnalysisPass`'s `var state = new CfgState()`. Prefer the latter for consistency. |
| **Acceptance** | Incremental re-analysis of a dirty subtree produces catalog/regions identical to full re-analysis; test uses `Analyzer.Analyze(root, prior, invalidatedNodes)` with two different programs sharing the incremental path. |
| **Related** | ANA-FIX-001, ANA-FIX-002 |
| **Notes** | Root-entry clear **landed** but insufficient: early-return on existing `CallSiteIndexMetadata` + partial visitation leaves stale indices; EH regions drop for unvisited subtrees; `ExpansionContext` not reset. Fix via **SPRINT-W5-T4/T5/T6**; verify via **SPRINT-W5-T2**. |

---

### ANA-FIX-017 — Constructor `ArgCount` in catalog mismatches `New.ToPrimitives`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **bug** (latent until ANA-FIX-011 / INT-028) |
| **Area** | ANA-004 |
| **Files** | `CallSiteCatalogPass.cs` (`ProcessNew`), `Poly/Syntax/Nodes/New.cs` |
| **Problem** | Catalog stores constructor `ArgCount = GetParameters().Length + 1` (implicit `this`). `New.ToPrimitives` emits `Call(Arguments.Length, 0)` — no `CallExternal`, no receiver slot. When `New.ToPrimitives` is wired to the catalog (ANA-FIX-011, INT-028), arg counts will disagree. |
| **Action** | Decide canonical constructor calling convention (match `CallExternal` instance pattern vs value-type `new` with no receiver). Align `ProcessNew` `ArgCount` and `New.ToPrimitives` emission in the same PR that wires `CallSiteIndexMetadata` consumer. Document convention on `CallSiteEntry`. |
| **Acceptance** | Catalog `ArgCount` for a resolved `New` node equals the slot count `New.ToPrimitives` places on the ring before the call primitive. |
| **Related** | ANA-FIX-004, ANA-FIX-011, INT-028 |
| **Notes** | `ProcessNew` uses param count only; aligned with `New.ToPrimitives`. Verify in **SPRINT-W4-T1**. |

---

### ANA-FIX-018 — `Constant(null)` classified as `HeapRef` vs VM `0L` sentinel

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** |
| **Area** | ANA-001 |
| **Files** | `ValueRepresentationPass.cs` (`ClassifyConstant`) |
| **Problem** | `Constant(null)` is classified `HeapRef`, but the VM typically leaves `0L` on the stack for null. `InterpretResult` with `HeapRef` may attempt handle `0` dereference (guarded today, but ABI is ambiguous). Remaining ANA-FIX-012 gap. |
| **Action** | Pick one: (a) classify null as `StackScalar` with `ClrType = null` and document as sentinel; (b) keep `HeapRef` and document that handle `0` is the null reference convention; (c) add dedicated `Null` kind. Update `InterpretResult` if needed. |
| **Acceptance** | Decision recorded; `ValueRepresentationTests` includes `NullConstant_*` case matching chosen ABI; no silent mis-deref on null-returning programs. |
| **Related** | ANA-FIX-012, INT-002 |
| **Notes** | `StackScalar` + tests landed. Close in **SPRINT-W6-T2**. |

---

### ANA-FIX-019 — `ValueRepresentation` tests for coalesce, conditional, and arithmetic `ClrType`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** (coverage gap) |
| **Area** | ANA-001 tests |
| **Files** | `Poly.Tests/Interpretation/ValueRepresentationTests.cs` |
| **Problem** | ANA-FIX-012 classification improvements landed without tests. `Coalesce`/`Conditional` propagation and arithmetic `ClrType` from resolver are unverified. |
| **Action** | Add tests: `Coalesce_IntConstants_IsStackScalar`; `Conditional_IntBranches_IsStackScalar` (with type resolver); `Add_ResolvedType_HasClrType` (e.g. `Add(Constant(1), Constant(2))` after full resolver pipeline); `Member_OnRefType_IsHeapRef` (ANA-001-T8 gap). |
| **Acceptance** | ANA-FIX-012 acceptance checklist satisfied in tests. |
| **Related** | ANA-FIX-012, ANA-001-T8 |
| **Notes** | Verified: coalesce, conditional, arithmetic `ClrType` tests **done**. **Open:** `Member_OnRefType_IsHeapRef` tests `string.Length` (int return) not a ref-type property — fix test to use e.g. `Member(Constant("hello"), "ToUpper")` → `HeapRef` (SPRINT-W4-T6). |

---

### ANA-FIX-020 — `InterpretResult` fallback heuristic when `RootValueKind` is `Unknown`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **suggestion** |
| **Area** | ANA-001 consumer |
| **Files** | `Poly/Interpretation/Interpreter.cs` (`InterpretResult`) |
| **Problem** | When `RootValueKind` is null or `Unknown`, `InterpretResult` falls back to `handle > 1 && handle < heap.Count`. This preserves INT-002 for analyzed programs but can still mis-classify edge cases (e.g. small integer results when heap is large). Fallback exists because not all compile paths attach analysis metadata. |
| **Action** | (1) Ensure standard `Interpreter` pipeline always sets `RootValueKind` when expansion ran — assert in debug. (2) Tighten fallback: treat `handle == 0` or `handle == 1` as bool/scalar per VM conventions. (3) Document fallback as legacy path; add test that fallback path is not hit for standard pipeline. |
| **Acceptance** | Standard `Interpreter.Execute` never relies on fallback for typed expression roots; test in **ANA-FIX-015** covers scalar path. |
| **Related** | ANA-FIX-003, INT-002 |
| **Notes** | Verified: fallback `handle >= 2`; DEBUG warning in `CompileCore`; `StandardPipeline_SetsRootValueKind` proves standard pipeline sets kind. Manual regression probe (W1-V6) optional. |

---

### ANA-FIX-021 — Duplicate XML doc blocks on `ExceptionRegionAnalysisPass`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Severity** | **nit** |
| **Area** | ANA-003 hygiene |
| **Files** | `ExceptionRegionAnalysisPass.cs` (lines ~62–74) |
| **Problem** | Two consecutive `/// <summary>` blocks appear before `ExceptionRegionState` — one describing the analyzer, one describing the state class. Second summary is attached to the wrong target in generated docs. |
| **Action** | Move `ExceptionRegionState` class above the analyzer (or give it its own doc block immediately preceding the type). Remove duplicate/orphan summary. |
| **Acceptance** | Single doc comment per public/internal type; IDE quick-info shows correct description for `ExceptionRegionAnalyzer` and `ExceptionRegionState`. |
| **Notes** | Fixed in third implementation round. |

---

### ANA-001 — `ValueRepresentationPass`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Priority** | **P0** (task 1 — no dependencies) |
| **Unblocks** | INT-002, INT-024, INT-020, `ProgramCompiler` marshaling, `BinaryOp.ComparisonType` |
| **Placement** | After `ControlFlowAnalysis`, before `ConstantFolding` |

#### Task list

- [x] **ANA-001-T1** — Define `ValueRepresentationKind` enum: `Void`, `StackScalar`, `Bool`, `HeapRef`, `Unknown`.
- [x] **ANA-001-T2** — Define `ValueRepresentationMetadata(ValueRepresentationKind Kind, Type? ClrType)` implementing `IAnalysisMetadata`.
- [x] **ANA-001-T3** — Create `Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs` + `UseValueRepresentationAnalysis()` extension on `AnalyzerBuilder`.
- [x] **ANA-001-T4** — Classify leaves: `Constant` → scalar/bool/heap from value type; `Parameter`/`Variable` → from `GetResolvedType()` + `IsStackValue()`; `ThisReference` → heap or scalar per resolved type.
- [x] **ANA-001-T5** — Classify expressions: arithmetic/bitwise → `StackScalar`; `And`/`Or`/`Not`/comparisons → `Bool`; `Member`/`Invoke`/`New`/`IndexAccess`/`Lambda` → from resolved member return type; `NullForgiving` → propagate operand. *(gaps: see ANA-FIX-012)*
- [x] **ANA-001-T6** — Classify statements: `Block`/`IfStatement`/loops → `Void` on statement nodes; expression-position children keep their own metadata.
- [x] **ANA-001-T7** — Register pass in `Interpreter.cs` standard pipeline (slot 7 above).
- [x] **ANA-001-T8** — Tests in `Poly.Tests/Interpretation/ValueRepresentationTests.cs`: `Method_Condition_ExpectedResult` style — int literal, string literal, `Member` on ref type, bool `And`, void `Block`. *(missing: ref-type `Member` test — add in fix-up)*
- [x] **ANA-001-T9** — **First consumer:** Update `Interpreter.InterpretResult` to read root node's `ValueRepresentationMetadata` — only dereference heap when `Kind == HeapRef` (fixes INT-002). → **ANA-FIX-003** *(harden test: SPRINT-W1-T2)*
- [ ] **ANA-001-T10** — **Second consumer (deferred post-sprint):** `Member.ToPrimitives` / `Invoke.ToPrimitives` read representation for `BinaryOp.ComparisonType` instead of re-deriving from CLR alone.

#### Done when

- Every expression node in test fixtures carries `ValueRepresentationMetadata`. ✅
- `InterpretResult` no longer uses handle-range heuristic for roots with metadata. ✅
- Analysis README pass table updated. ✅

---

### ANA-004 — `CallSiteCatalogPass`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Priority** | **P0** (task 2 — depends on `TypeAndMemberResolver` only) |
| **Unblocks** | INT-019, INT-028, INT-030, portable `CallExternal`, `New` lowering |
| **Placement** | After `ValueRepresentationPass`, before `ConstantFolding` |

#### Task list

- [x] **ANA-004-T1** — Define `CallSiteEntry(string Identity, MethodInfo Target, int ArgCount, bool IsStatic, bool IsConstructor)` record (identity = `"Namespace.Type.Method(paramCount)"` per sandboxing ADR). *(overload collision: ANA-FIX-013)*
- [x] **ANA-004-T2** — Define `CallSiteCatalogMetadata(IReadOnlyList<CallSiteEntry> Sites)` on root (`null` key) implementing `IAnalysisMetadata`.
- [x] **ANA-004-T3** — Define `CallSiteIndexMetadata(int SiteIndex)` per `Invoke`/`Member` (getter)/`New` node implementing `IAnalysisMetadata`.
- [x] **ANA-004-T4** — Create `Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs` + `UseCallSiteCatalog()` extension.
- [x] **ANA-004-T5** — Walk tree post-order; for each `Invoke` with `ClrMethod` resolved member → allocate stable index in catalog, stamp `CallSiteIndexMetadata` on node.
- [x] **ANA-004-T6** — Same for `Member` with property getter (`ClrTypeProperty` + `GetGetMethod`).
- [x] **ANA-004-T7** — Same for `New` with resolved constructor (`ClrMethod` constructor) — mark `IsConstructor = true`.
- [x] **ANA-004-T8** — Store finalized `CallSiteCatalogMetadata` on root via `context.SetMetadata<CallSiteCatalogMetadata>(null, catalog)` at end of pass visit. *(only when non-empty — see ANA-FIX-006)*
- [x] **ANA-004-T9** — Register pass in `Interpreter.cs` (slot 8 above).
- [x] **ANA-004-T10** — Tests in `CallSiteCatalogTests.cs`: duplicate invoke, distinct methods, unresolved, `ArgCount`, overloads, sequential leak. *(missing: `New` constructor — see SPRINT-W4-T1)*
- [x] **ANA-004-T11** — **First consumer (partial):** `CallExternal.SiteIndex` on `Invoke`/`Member`; `VmProgram.CallSites`. *(missing: `New.ToPrimitives`; expansion verify test)*
- [ ] **ANA-004-T12** — `ProgramCompiler` passes `ec.SiteIndex` and/or resolves index → `MethodInfo` from `VmProgram.CallSites` catalog table.

**Blocking fix:** ANA-FIX-001 (stateful analyzer), ANA-FIX-004 (`ArgCount`). ✅ resolved

#### Done when

- Catalog round-trips: analyze → get catalog → expansion emits indices → compiler resolves to same `MethodInfo`. ⚠️ expansion indices land in primitives; compiler still uses `ec.Target` directly
- `New` no longer emits `Call(argCount, funcIndex: 0)` (INT-028 path clear). ❌ deferred INT-028
- INT-019 implementation can begin immediately after sprint. ⚠️ partial — catalog on `VmProgram` exists

---

### ANA-003 — `ExceptionRegionAnalysisPass`

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Priority** | **P0** (task 3 — depends on CFG + definite assignment) |
| **Unblocks** | INT-018, INT-001, correct `UsingStatement` / `ThrowStatement` lowering |
| **Placement** | After `LambdaReturnTypeResolution`, immediately before `ExpansionPass` |

#### Task list

- [x] **ANA-003-T1** — Define `ExceptionRegionKind`: `Try`, `Catch`, `Finally`, `UsingDispose`.
- [x] **ANA-003-T2** — Define `ExceptionRegionEntry(ExceptionRegionKind Kind, NodeId AnchorNodeId, int? CatchTypeId, string? CatchVariableName, IReadOnlyList<NodeId> ProtectedNodeIds, IReadOnlyList<NodeId> HandlerNodeIds)`.
- [x] **ANA-003-T3** — Define `ExceptionRegionMetadata(IReadOnlyList<ExceptionRegionEntry> Regions)` on root (`null` key) implementing `IAnalysisMetadata`.
- [x] **ANA-003-T4** — Create `Poly/Interpretation/Analysis/Semantics/ExceptionRegionAnalysisPass.cs` + `UseExceptionRegionAnalysis()` extension.
- [ ] **ANA-003-T5** — For `TryCatchFinally`: read `ControlFlowMetadata` CFG; map `BuildTryCatchCfg` blocks to region entries. → **ANA-FIX-008** `blocked` post-sprint
- [ ] **ANA-003-T6** — Integrate `DefiniteAssignmentMetadata` for catch variable slots. `blocked` post-sprint
- [x] **ANA-003-T7** — For `UsingStatement`: region entry with `UsingDispose` kind. ✅ *(dispose via catalog deferred to INT-018)*
- [x] **ANA-003-T8** — Detect `ThrowStatement` nodes inside protected regions; stamp `InProtectedRegionMetadata` on throw nodes.
- [x] **ANA-003-T9** — Register pass in `Interpreter.cs` (slot 12 above).
- [x] **ANA-003-T10** — Tests in `ExceptionRegionAnalysisTests.cs` (10 tests). *(missing: throw-in-catch `IsInProtectedRegion == false` assertion)*
- [x] **ANA-003-T11** — **First consumer:** `TryCatchFinally.ToPrimitives` → `RegionMarker` placeholders. ✅ *(expansion verify test missing — SPRINT-W3-T5)*
- [x] **ANA-003-T12** — **Second consumer:** `UsingStatement.ToPrimitives` → `EnterUsingDispose` marker. ✅

**Blocking fix:** ANA-FIX-002 (stateful analyzer). ✅ resolved

#### Done when

- Every `TryCatchFinally` / `UsingStatement` in tests carries complete `ExceptionRegionMetadata`. ✅
- Expansion can lower EH without re-walking CFG. ✅ (code); ⚠️ not verified by automated expansion test
- INT-018 implementation has a concrete metadata contract to implement against. ✅

---

### P0 Sprint Wrap-Up

**Source:** Fourth implementation + verification review (2026-07-05).  
**Goal:** Satisfy sprint exit criteria and mark ANA-001, ANA-004, ANA-003 `done`.  
**Verified progress:** ~88% — **1379/1379 tests green**, G-build green.  
**Defer to post-sprint:** ANA-FIX-008, ANA-003-T5/T6, ANA-001-T10, ANA-FIX-009/ANA-002, `ProcessMember` for `ClrMethod` (catalog gap), full INT-019 serialization format.

#### Exit checklist (verified 2026-07-05)

| Criterion | Status |
|-----------|--------|
| Three passes implemented + registered in `Interpreter.cs` | ✅ |
| `dotnet run --project Poly.Tests/Poly.Tests.csproj` green | ✅ (1379) |
| One consumer per pass | ⚠️ ANA-001 ✅ · ANA-003 ✅ placeholders · ANA-004 ⚠️ partial (`Invoke`/`Member`; compiler ignores `SiteIndex`) |
| Tests cover acceptance paths per pass | ⚠️ analysis tests strong; expansion verify tests missing |

---

### Sprint Flush-Out — Explicit Task List

**For agents:** Execute tasks in dependency order. Each task has a single owner file set, acceptance criteria, and a verify step. Run **G-build** + **G-test** after every phase. Do **not** run W6 until all P0 and P1 tasks pass.

#### Dependency graph

```
W1 ✅
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase A — P0 (sprint blockers)                              │
│   W2-T4b → W2-T5 → W3-T3b → W3-T5 → W3-T7                   │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase B — P1 (completeness + incremental correctness)     │
│   W2-T2b ∥ W4-T1,T6,T5b → W5-T4,T5,T6 → W5-T2              │
└─────────────────────────────────────────────────────────────┘
  ↓
┌─────────────────────────────────────────────────────────────┐
│ Phase C — P2 + closure                                      │
│   W4-T5b → W6-T1..T5                                        │
└─────────────────────────────────────────────────────────────┘
```

#### Phase A — P0 sprint blockers

| ID | Task | File(s) | Do exactly this | Acceptance (must fail if reverted) |
|----|------|---------|-----------------|-------------------------------------|
| **SPRINT-W2-T4b** | Resolve `MethodInfo` from catalog at compile time | `ProgramCompiler.cs`, `VmProgram.cs` | `CompilePrimitives` already has `VmProgram`; thread `program.CallSites` into `EmitCallExternalDirect`. When `siteIndex` is non-null, set `target = program.CallSites[siteIndex.Value].Target` (bounds check; throw `InvalidOperationException` on mismatch). When null, keep `ec.Target`. | Unit or integration test: expanded `CallExternal` with `SiteIndex=0` still executes correctly when `Target` on primitive is a stub/different reference but catalog entry is correct. At minimum: debug assert `target == catalog[siteIndex].Target` when both present. |
| **SPRINT-W2-T5** | Expansion verify: `SiteIndex` matches catalog | New: `Poly.Tests/Interpretation/ExpansionIntegrationTests.cs` (or extend `CallSiteCatalogTests.cs`) | Build production `AnalyzerBuilder` pipeline (resolver + all three new passes + expansion). Analyze `Invoke(Member(Constant("hello"), "IndexOf"), Constant('e'))`. Read root `PrimitiveExpansionMetadata` and `GetCallSiteCatalog()`. Assert exactly one `CallExternal` with `SiteIndex == catalog index` for that invoke. Duplicate invoke in same tree → same index. | **SPRINT-W2-V1** ✅ |
| **SPRINT-W3-T3b** | Fix `UsingStatement` marker order | `UsingStatement.cs` | Comment says resource → body → dispose. Reorder: emit `Resource` primitives, expand `Body`, **then** emit `RegionMarker(..., "EnterUsingDispose")` (or rename to `LeaveUsingDispose` if that matches INT-018 intent). Update `UsingStatement_ProducesUsingDisposeRegion` test if expansion order is asserted. | Expanded primitive list order: resource ops → body ops → dispose marker. **SPRINT-W3-V2** ✅ |
| **SPRINT-W3-T5** | Expansion verify: EH markers | `ExpansionIntegrationTests.cs` or `ExceptionRegionAnalysisTests.cs` | Full pipeline on: (1) `TryCatchFinally` with throw in try — assert `RegionMarker` sequence matches `GetExceptionRegions()` order; (2) throw in try → `ThrowProtected` in expanded primitives; throw outside try → `Throw`. | **SPRINT-W3-V1**, **W3-V3** ✅ |
| **SPRINT-W3-T7** | Fix `TypeIs` lowering for stack scalars + unresolved types | `TypeIs.cs`, `ProgramCompiler.cs`, `PrimitiveExpandTests.cs` | Branch on operand representation: `StackScalar`/`Bool` → inline type check on raw long (or box-then-check); `HeapRef` → existing `TypeCheck`. Unresolved `TargetTypeReference` → `PushConstant(0L)` (fail closed), not `1L`. Add `Expand_TypeIs_IntConstant_IsTrue` and `Expand_TypeIs_UnresolvedType_IsFalse`. | Tests pass; `42 is int` returns true without heap indexing. |

**Phase A gate:** W2-V1, W3-V1–V3 green; **G-test** ≥ 1383 (net +4 tests minimum).

#### Phase B — P1 completeness

| ID | Task | File(s) | Do exactly this | Acceptance |
|----|------|---------|-----------------|------------|
| **SPRINT-W2-T2b** | Wire `New.ToPrimitives` to catalog **or** explicit deferral | `New.cs`, `CallSiteCatalogPass.cs`, tracker | **Option A (preferred):** When `GetCallSiteIndex(node)` present, emit `CallExternal(ctor, argCount, isStatic: false, siteIndex)` like `Invoke`. **Option B:** Remove `ProcessNew` catalog stamping; add tracker note "constructor catalog deferred to INT-028"; skip W4-T1 expansion half. | **SPRINT-W2-V5** ✅ for chosen option. If Option A: W4-T1 passes. |
| **SPRINT-W4-T1** | `New_ResolvedConstructor_GetsSiteIndex` | `CallSiteCatalogTests.cs` | `new string('x', 3)` or `New(TypeReference.To<List<int>>(), ...)` with resolved ctor. Assert catalog entry `IsConstructor == true`, `ArgCount == Arguments.Length`, node has `CallSiteIndexMetadata`. Skip if W2-T2b Option B. | **SPRINT-W4-V4** ✅ |
| **SPRINT-W4-T6** | Fix ref-return member test | `ValueRepresentationTests.cs` | Rename `Member_OnRefType_IsHeapRef` → `Member_IntProperty_IsStackScalar` (keep `string.Length` assertion). Add `Member_RefReturningProperty_IsHeapRef`: `Member(Constant("hello"), "ToUpper")` → `HeapRef`. | **SPRINT-W4-V3** ✅ |
| **SPRINT-W4-T5b** | Throw-in-catch not protected | `ExceptionRegionAnalysisTests.cs` | Extend `NestedTry_InnerThrowMarkedProtected`: add throw node inside catch clause; assert `GetMetadata<InProtectedRegionMetadata>(throwInCatch)?.IsInProtectedRegion == false`. Fix misleading comment at ~line 1017. | **SPRINT-W4-V6** ✅ |
| **SPRINT-W5-T4** | Reset `ExpansionContext` at root | `ExpansionPass.cs` | Mirror catalog/EH: track depth on null-key state or detect root entry; `new ExpansionContext(context)` at root (depth 0→1), not reuse cloned instance. | Incremental test (T2) does not leak slot assignments across programs. |
| **SPRINT-W5-T5** | Fix catalog incremental re-indexing | `CallSiteCatalogPass.cs` | Root `Catalog.Clear()` is insufficient: `ProcessInvoke`/`ProcessMember`/`ProcessNew` early-return when `CallSiteIndexMetadata` exists. On root entry, either strip `CallSiteIndexMetadata` from all call-site nodes before walk, or ignore early-return at root and rebuild indices (dedup via `CreateEntry` identity). | Incremental catalog equals full re-analysis (T2). |
| **SPRINT-W5-T6** | Fix EH incremental region rebuild | `ExceptionRegionAnalysisPass.cs` | Same pattern: root clear drops regions for unvisited subtrees. Merge prior `ExceptionRegionMetadata` entries for subtrees not in `invalidatedNodes`, or force full-tree region walk on root entry. Clear `InProtectedRegionMetadata` on invalidated subtrees before re-marking. | Incremental regions equal full re-analysis (T2). |
| **SPRINT-W5-T2** | Incremental equivalence test | New: `Poly.Tests/Interpretation/IncrementalAnalysisTests.cs` | (1) Analyze program A (invoke + try/catch), then incremental analyze unrelated program B with empty invalidation → catalog empty, regions empty. (2) Analyze program C, edit one node, incremental re-analyze with `invalidatedNodes` containing edited subtree → `GetCallSiteCatalog()` and `GetExceptionRegions()` byte-equal to full `Analyze(C)`. | **SPRINT-W5-V1**, **W5-V2** ✅ |
| **SPRINT-W5-T3** | Mark **ANA-FIX-016** `done` | tracker | Only after T2, T4, T5, T6 pass. Revert status from `done` to `in-progress` until then. | — |

**Phase B gate:** W4-V3/V4/V6, W5-V1–V2 green; **G-test** ≥ 1388 (net +9 vs 1379 baseline).

#### Phase C — P2 + W6 closure

| ID | Task | File(s) | Do exactly this | Acceptance |
|----|------|---------|-----------------|------------|
| **SPRINT-W6-T1** | Mark ANA-001, ANA-004, ANA-003 `done` | `interpretation-system-issues.md` | All Phase A + B verify rows ✅. | — |
| **SPRINT-W6-T2** | Close resolved ANA-FIX items | tracker | Mark W4-T7 items done. Leave ANA-FIX-008, ANA-001-T10, ANA-003-T5/T6 deferred. | — |
| **SPRINT-W6-T3** | Fix `CallSiteEntry` XML doc | `CallSiteCatalogPass.cs` | Document identity key uses parameter **types**, not count only. | — |
| **SPRINT-W6-T4** | Update `docs/plans/README.md` | README | P0 sprint **complete**; baseline **≥1388** tests; next INT-018/019/028. | — |
| **SPRINT-W6-T5** | Update suggested execution order | tracker bottom | Move P0 fix-up to done; promote P1 primitive IR. | — |
| **SPRINT-W6-T6** | `EmitCallExternalDirect` debug assert | `ProgramCompiler.cs` | `Debug.Assert(consumedPcs.Length == argCount)` at top of method. | Nit; optional before W6. |

**W6 gate:** Exit checklist all ✅; **SPRINT-W6-V1**–**V6** all ✅.

#### Remaining work summary (quick reference)

| Priority | Open tasks |
|----------|------------|
| **P0** | W2-T4b, W2-T5, W3-T3b, W3-T5, W3-T7 |
| **P1** | W2-T2b, W4-T1, W4-T6, W4-T5b, W5-T2, W5-T4, W5-T5, W5-T6 |
| **P2** | W6-T1–T6 |

#### Wrap-up packages (status)

```
SPRINT-W1   Close ANA-001 + harden INT-002 tests          done
SPRINT-W2   Wire ANA-004 catalog consumer                 done
SPRINT-W3   Wire ANA-003 EH consumer                      done
SPRINT-W4   Complete remaining test gaps                  done
SPRINT-W5   Incremental analysis state                    done
SPRINT-W6   Sprint closure                                done
```

#### Global verification gates (run after each W-package; all required before W6)

| Gate | Command / action | Pass condition |
|------|------------------|----------------|
| **G-build** | `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` | Exit 0, no new warnings in touched files |
| **G-test** | `dotnet run --project Poly.Tests/Poly.Tests.csproj` | All tests pass; count ≥ 1379 (current baseline); ≥ 1388 after flush-out |
| **G-no-regress** | Re-run `CallSiteCatalogTests`, `ExceptionRegionAnalysisTests`, `ValueRepresentationTests`, `InterpretResultIntegrationTests` | All pass in isolation |
| **G-pipeline** | Confirm `Interpreter.cs` analyzer list matches `Poly/Interpretation/Analysis/README.md` pass table order | 13 passes, slots 7/8/12 for new passes |

**Sprint definition of done (W6 only when all true):**

1. Exit checklist table above — every row ✅  
2. **G-build** + **G-test** green after W1–W5 land  
3. At least one new test per W-package that would **fail** if the implementation were reverted (see `*-V*` tasks below)  
4. `VmCorrectnessTests` and `PrimitiveExpandTests` still pass (W2/W3 must not break existing execution semantics)  
5. Tracker updated: ANA-001, ANA-004, ANA-003 → `done`; deferred items explicitly noted  

---

##### SPRINT-W1 — Close ANA-001 + harden INT-002 tests

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Blocks** | Marking ANA-001 `done` |
| **Files** | `InterpretResultIntegrationTests.cs`, `Interpreter.cs` |
| **Tasks** | |
| **SPRINT-W1-T1** | Mark **ANA-FIX-003**, **ANA-FIX-012**, **ANA-FIX-018** `done` in tracker (code already landed). |
| **SPRINT-W1-T2** | Strengthen `ScalarResult_WithPopulatedHeap_ReturnsRawValue`: in `configure` callback, call `state.Heap.Allocate` three times **before** delegate runs; assert `state.Heap.Count > 2` and `result.GetValue<int>() == 2`. Test must fail if `InterpretResult` reverts to heap-deref heuristic. |
| **SPRINT-W1-T3** | Add test `StandardPipeline_SetsRootValueKind` (debug or reflection): compile `Add(1,1)` via `Interpreter.Compile` and assert `program.RootValueKind == StackScalar`. |
| **SPRINT-W1-T4** | Check **ANA-001-T9** complete; mark ANA-001 `done` when W1-T2 passes. |

**Verify (definition of done):**

| Task | What to verify | Pass = |
|------|----------------|--------|
| **SPRINT-W1-V1** | Run `InterpretResultIntegrationTests` only | All 4 tests pass |
| **SPRINT-W1-V2** | `ScalarResult_WithPopulatedHeap_ReturnsRawValue`: assert `heap.Count > 2` **before** checking result; `GetValue<int>() == 2`; `RawValue == 2L`; `result.Value` is **not** a heap object type | Precondition and result both asserted |
| **SPRINT-W1-V3** | `BoolResult_WithPopulatedHeap_ReturnsBool`: `GetValue<bool>() == true`, `RawValue == 1L` (handle 1 must not be heap-deref'd) | Pass |
| **SPRINT-W1-V4** | `HeapResult_WithPopulatedHeap_DereferencesCorrectly`: returns `"hello-world"` string, not a `long` handle | Pass |
| **SPRINT-W1-V5** | `NullConstant_ReturnsAsScalar`: `GetValue<long>() == 0L` | Pass |
| **SPRINT-W1-V6** | **Regression probe:** temporarily set `RootValueKind = null` → test must **fail** | ⏭️ optional manual |
| **SPRINT-W1-V7** | **G-test** after W1 | ✅ 1378 green |

**Acceptance:** INT-002 regression guard is deterministic; ANA-001 exit criteria met. ✅ **Verified.**

---

##### SPRINT-W2 — Wire ANA-004 catalog consumer (minimum viable)

| Field | Value |
|-------|-------|
| **Status** | `in-progress` |
| **Blocks** | Marking ANA-004 `done`; unblocks INT-019, INT-028 |
| **Files** | `Primitives.cs`, `Invoke.cs`, `Member.cs`, `New.cs`, `VmProgram.cs`, `ProgramCompiler.cs`, `Interpreter.cs` (`CompileCore`) |
| **Tasks** | |
| **SPRINT-W2-T1** | Extend `CallExternal` with optional `int? SiteIndex` **or** add `CallSite(int Index, int ArgCount, bool IsStatic)` primitive. Prefer extending `CallExternal` to avoid duplicate compiler paths. |
| **SPRINT-W2-T2** | `Invoke.ToPrimitives` / `Member.ToPrimitives` / `New.ToPrimitives`: when `CallSiteIndexMetadata` present, emit indexed form; when absent, fall back to current `MethodInfo` emission (backward compatible). |
| **SPRINT-W2-T3** | Carry `CallSiteCatalogMetadata` on `VmProgram` (new field) or `ExpansionContext` snapshot at compile time in `CompileCore`. |
| **SPRINT-W2-T4** | ~~Pass `ec.SiteIndex` to `EmitCallExternalDirect`~~ ✅ plumbed; **use** it — see **SPRINT-W2-T4b** in Flush-Out. |
| **SPRINT-W2-T4b** | **Open:** Resolve `MethodInfo` from `VmProgram.CallSites[siteIndex]` in `EmitCallExternalDirect` — see Flush-Out Phase A. |
| **SPRINT-W2-T5** | **Open:** Expansion integration test — see Flush-Out Phase A. |
| **SPRINT-W2-T2b** | **Open:** `New.ToPrimitives` catalog consumer or explicit INT-028 deferral — see Flush-Out Phase B. |
| **SPRINT-W2-T6** | Check **ANA-004-T11**, **ANA-004-T12** complete. |

**Verify (definition of done):**

| Task | What to verify | Pass = |
|------|----------------|--------|
| **SPRINT-W2-V1** | Expansion test: `PrimitiveExpansionMetadata` contains `CallExternal.SiteIndex` matching catalog | ❌ no automated test |
| **SPRINT-W2-V2** | E2E `IndexOf('e')` via `Interpreter.Execute` → 1 | ✅ implied (suite green) |
| **SPRINT-W2-V3** | Dedup: same `SiteIndex` for duplicate invokes | ✅ code path; ❌ expansion test |
| **SPRINT-W2-V4** | Lambda invoke → `Call` not `CallExternal` | ✅ `SimpleInvoke_GetsSiteIndex` |
| **SPRINT-W2-V5** | `Member` getter + `New` constructor | ⚠️ `Member` wired; `New` ❌ |
| **SPRINT-W2-V6** | `ArgCount` consistency | ✅ `InstanceInvoke_ArgCountIncludesReceiver` |
| **SPRINT-W2-V7** | Regression probe on metadata stamping | ⏭️ optional manual |
| **SPRINT-W2-V8** | `VmCorrectnessTests` + `PrimitiveExpandTests` | ✅ full suite green |
| **SPRINT-W2-V9** | **G-build** + **G-test** | ✅ |

**Acceptance:** ⚠️ Code landed for `Invoke`/`Member` + `VmProgram.CallSites`. **Open:** W2-T4b (catalog resolution), W2-T5 (expansion test), W2-T2b (`New`).

**Out of scope for sprint:** Full INT-019 serialization format; runtime catalog reload.

---

##### SPRINT-W3 — Wire ANA-003 EH consumer (minimum viable)

| Field | Value |
|-------|-------|
| **Status** | `in-progress` |
| **Blocks** | Marking ANA-003 `done`; unblocks INT-018 |
| **Files** | `TryCatchFinally.cs`, `UsingStatement.cs`, `Primitives.cs` (if new markers needed), `PrimitiveExpandTests.cs` |
| **Tasks** | |
| **SPRINT-W3-T1** | Define EH placeholder primitive(s): e.g. `BeginTry(int RegionIndex)`, `EndTry`, `BeginCatch(int RegionIndex, int? CatchTypeId)`, `BeginFinally(int RegionIndex)`, `BeginUsingDispose(int RegionIndex)` — or reuse `CommentOp`-style zero-code markers if VM ignores them today. Primitives must be no-ops in current `ProgramCompiler` (emit empty / skip) so existing tests stay green. |
| **SPRINT-W3-T2** | `TryCatchFinally.ToPrimitives`: read `context.Analysis.GetExceptionRegions()` (or per-node slice filtered by `AnchorNodeId`); emit placeholder sequence bracketing try/catch/finally bodies in region-table order. |
| **SPRINT-W3-T3** | ~~`UsingStatement.ToPrimitives`~~ ✅ landed; marker **before** body — wrong order. Fix: **SPRINT-W3-T3b**. |
| **SPRINT-W3-T3b** | **Open:** Reorder dispose marker after body — see Flush-Out Phase A. |
| **SPRINT-W3-T4** | `ThrowStatement.ToPrimitives`: when `InProtectedRegionMetadata` present, emit `ThrowProtected` — ✅ landed. |
| **SPRINT-W3-T5** | **Open:** Expansion integration tests — see Flush-Out Phase A. |
| **SPRINT-W3-T7** | **Open:** `TypeIs` stack-scalar + unresolved-type fixes — see Flush-Out Phase A. |
| **SPRINT-W3-T6** | Check **ANA-003-T11**, **ANA-003-T12** complete. |

**Verify (definition of done):**

| Task | What to verify | Pass = |
|------|----------------|--------|
| **SPRINT-W3-V1** | Expanded try/catch/finally contains `RegionMarker` in metadata order | ❌ no automated test |
| **SPRINT-W3-V2** | Expanded `UsingStatement` contains `EnterUsingDispose` marker | ❌ no automated test |
| **SPRINT-W3-V3** | Throw in try → `ThrowProtected`; outside → `Throw` | ❌ no automated test |
| **SPRINT-W3-V4** | VM execution unchanged (no-op markers) | ✅ full suite green |
| **SPRINT-W3-V5** | `Expand_UsingStatement_ExecutesBody` | ✅ |
| **SPRINT-W3-V6** | Regression probe without EH pass | ⏭️ optional manual |
| **SPRINT-W3-V7** | Zero extra stack slots from markers | ✅ `RegionMarker`/`ThrowProtected` emit null in compiler |
| **SPRINT-W3-V8** | **G-test** | ✅ |

**Acceptance:** ⚠️ Code landed (`TryCatchFinally`, `UsingStatement`, `ThrowStatement`). **Open:** W3-T3b (using order), W3-T5 (expansion tests), W3-T7 (`TypeIs`).

**Out of scope for sprint:** ANA-FIX-008 CFG filtering; catch variable slot binding (ANA-003-T6).

---

##### SPRINT-W4 — Complete remaining test gaps

| Field | Value |
|-------|-------|
| **Status** | `in-progress` |
| **Blocks** | Marking ANA-004-T10 and ANA-003-T10 complete |
| **Files** | `CallSiteCatalogTests.cs`, `ExceptionRegionAnalysisTests.cs`, `ValueRepresentationTests.cs` |
| **Tasks** | |
| **SPRINT-W4-T1** | `CallSiteCatalogTests`: `New_ResolvedConstructor_GetsSiteIndex` — `new string('x')` or `new List<int>()` with resolved ctor; assert `IsConstructor == true` and `ArgCount == Arguments.Length`. |
| **SPRINT-W4-T2** | `CallSiteCatalogTests`: `InstanceInvoke_ArgCountIncludesReceiver` — assert catalog entry `ArgCount == paramCount + 1` for instance method. |
| **SPRINT-W4-T3** | `CallSiteCatalogTests`: `SameArityOverloads_DistinctIndices` — if two `Substring` overloads resolvable in one tree, distinct indices (documents ANA-FIX-013). |
| **SPRINT-W4-T4** | `ExceptionRegionAnalysisTests`: `UsingStatement_ProducesUsingDisposeRegion` — protected = resource + body; handler = resource IDs. |
| **SPRINT-W4-T5** | `NestedTry_InnerThrowMarkedProtected` — inner try throw marked ✅. |
| **SPRINT-W4-T5b** | **Open:** Assert throw-in-catch `IsInProtectedRegion == false` — see Flush-Out Phase B. |
| **SPRINT-W4-T6** | **Open:** Rename int-property test + add `ToUpper` → `HeapRef` — see Flush-Out Phase B. |
| **SPRINT-W4-T7** | Mark **ANA-FIX-005**, **ANA-FIX-010**, **ANA-FIX-014**, **ANA-FIX-019** `done`. |

**Verify (definition of done):**

| Task | What to verify | Pass = |
|------|----------------|--------|
| **SPRINT-W4-V1** | `CallSiteCatalogTests` (10 tests) + leak test | ✅ except `New` test |
| **SPRINT-W4-V2** | `ExceptionRegionAnalysisTests` (10 tests) + leak test | ✅ |
| **SPRINT-W4-V3** | `ValueRepresentationTests` (22 tests) | ⚠️ `Member_OnRefType` wrong assertion |
| **SPRINT-W4-V4** | `New_ResolvedConstructor_GetsSiteIndex` | ❌ missing |
| **SPRINT-W4-V5** | `InstanceInvoke_ArgCountIncludesReceiver` | ✅ |
| **SPRINT-W4-V6** | Nested try + throw-in-catch not protected | ⚠️ inner throw only |
| **SPRINT-W4-V7** | **G-test**; count +6 vs 1372 baseline | ✅ 1378 (+6) |

**Acceptance:** ⚠️ Mostly covered. **Open:** T1, T6, throw-in-catch assertion (T5/V6).

---

##### SPRINT-W5 — Incremental analysis state (ANA-FIX-016)

| Field | Value |
|-------|-------|
| **Status** | `in-progress` |
| **Priority** | Required before incremental analysis consumers; safe to land in same PR as W2–W3 |
| **Files** | `CallSiteCatalogPass.cs`, `ExceptionRegionAnalysisPass.cs` |
| **Tasks** | |
| **SPRINT-W5-T1** | Root-entry `Clear()` on catalog/EH state — ✅ landed; insufficient alone. |
| **SPRINT-W5-T4** | **Open:** Reset `ExpansionContext` at root — see Flush-Out Phase B. |
| **SPRINT-W5-T5** | **Open:** Fix catalog incremental re-indexing — see Flush-Out Phase B. |
| **SPRINT-W5-T6** | **Open:** Fix EH incremental region merge — see Flush-Out Phase B. |
| **SPRINT-W5-T2** | **Open:** Incremental equivalence test — see Flush-Out Phase B. |
| **SPRINT-W5-T3** | Mark **ANA-FIX-016** `done` only after T2/T4/T5/T6. |

**Verify (definition of done):**

| Task | What to verify | Pass = |
|------|----------------|--------|
| **SPRINT-W5-V1** | Incremental re-analyze tree B after tree A → empty catalog/regions | ❌ no test |
| **SPRINT-W5-V2** | Incremental edited tree matches full re-analyze | ❌ no test |
| **SPRINT-W5-V3** | Sequential leak tests still pass | ✅ |
| **SPRINT-W5-V4** | Regression probe on root-entry clear | ⏭️ optional manual |
| **SPRINT-W5-V5** | **G-test** | ✅ |

**Acceptance:** ⚠️ Root-entry clear landed; incremental correctness **open** (T4–T6 + T2).

---

##### SPRINT-W6 — Sprint closure

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Tasks** | |
| **SPRINT-W6-T1** | Mark **ANA-001**, **ANA-004**, **ANA-003** status `done` in this tracker. |
| **SPRINT-W6-T2** | Mark open **ANA-FIX** items resolved during wrap-up; leave **ANA-FIX-008**, **ANA-FIX-009**, **ANA-001-T10**, **ANA-003-T5/T6** explicitly deferred with `blocked` or note in changelog. |
| **SPRINT-W6-T3** | Fix `CallSiteEntry` XML doc: identity string includes parameter types, not param count only. |
| **SPRINT-W6-T4** | Update `docs/plans/README.md` — P0 sprint complete; next focus INT-018, INT-019, INT-028. |
| **SPRINT-W6-T5** | Update **Suggested Execution Order** below — move P0 fix-up to done; promote P1 primitive IR. |

**Verify (definition of done):**

| Task | What to verify | Pass = |
|------|----------------|--------|
| **SPRINT-W6-V1** | Exit checklist all ✅ | ❌ consumers + tests still ⚠️ |
| **SPRINT-W6-V2** | **G-build** + **G-test** final | ❌ target ≥ 1388 after flush-out |
| **SPRINT-W6-V3** | All W*-V* pass | ❌ W2-V1, W3-V1–V3, W4-V4/V6, W5-V1–V2 open |
| **SPRINT-W6-V4** | Tracker consistent | ⚠️ in progress (this update) |
| **SPRINT-W6-V5** | README accurate | ⚠️ was prematurely "DONE" — corrected |
| **SPRINT-W6-V6** | Smoke tests | ✅ suite covers |

**Acceptance:** ❌ **Do not mark sprint closed** until Remaining work P0 items land and W6-V1/V3 pass.

---

### P0 sprint sequencing (original plan — superseded by Wrap-Up above)

```
Week 1   ANA-001 (T1–T10)     Value representation + InterpretResult consumer
Week 1–2 ANA-004 (T1–T12)     Call site catalog + indexed lowering consumer
Week 2–3 ANA-003 (T1–T12)     EH regions + expansion placeholders
         ↓
Next     INT-018, INT-019, INT-002 (remaining), INT-028
```

**Parallelization:** ANA-001 and ANA-004 can start in parallel after type resolution exists. ANA-003 starts once CFG pass is understood (can overlap ANA-004 tail).

---

## P1 — Correctness & Canonical Semantics

### INT-001 — `PrimThrow` emits no code in `ProgramCompiler`

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | VM / exceptions |
| **Files** | `Poly/Interpretation/Vm/ProgramCompiler.cs` (`EmitThrowOp` wired), `Poly/Syntax/Nodes/ThrowStatement.cs`, `Poly.Tests/Interpretation/ThrowVmTests.cs`, `Poly.Tests/Interpretation/ExceptionHandlingVmTests.cs` |
| **Problem** | IR-level `Throw` primitives were lowered but the compiler emitted nothing. |
| **Action** | Implement `EmitThrowOp` with integration into exception-region handling (Strategy B — side-table dispatch). |
| **Acceptance** | Full structured EH: throw, try/catch, try/finally, try/catch/finally, multiple catch clauses with type filter — all through Strategy B dispatch. See Phase 1 test matrix in resolution plan. |
| **Related** | INT-018 (Strategy B EH implementation), `docs/decisions/2026-07-05-vm-exception-handling-strategy-b.md` |
| **Notes** | Bumped from `done` to `open`: EmitThrowOp + try/catch are wired but finally/using/nested EH remain. INT-001 not `done` until INT-018 Phase 1 exits. |

---

### INT-002 — Result extraction treats numeric values as heap handles

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | VM / result ABI |
| **Files** | `Poly/Interpretation/Interpreter.cs` (`InterpretResult`, lines ~142–153) |
| **Problem** | Any `long` result in `(1, heap.Count)` is dereferenced as a heap handle. A plain integer result (e.g. `2`) can be misread as an object reference when the heap is large enough. |
| **Action** | Use analysis metadata (resolved return type on root node) or a tagged value representation to distinguish heap handles from raw primitives at the API boundary. |
| **Acceptance** | Regression test: program returns small integer `2` with a populated heap; `GetValue<int>()` returns `2`, not a heap object. |

---

### INT-003 — Migrate integration tests off `LinqExpressionGenerator` where VM covers the case

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Tests / dual semantics |
| **Files** | `Poly.Tests/Interpretation/InterpreterIntegrationTests.cs`, `LambdaInvokeTests.cs`, `LinqExpressionGeneratorTests.cs`, `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs` |
| **Problem** | ~1,100 LOC of parallel AST lowering duplicates the canonical VM path — the same class of debt the tree-walker removal addressed (`2026-06-08-vm-as-canonical-semantics.md`). Many integration tests still call `BuildExpression` / `CompileLambda` instead of `Interpreter.Execute`. |
| **Action** | 1. Inventory tests using Linq path. 2. Port each to `Interpreter.Execute(VmProgram)` where `VmCorrectnessTests` patterns apply. 3. Mark remaining Linq-only cases explicitly. 4. Deprecate `LinqExpressionGenerator` once parity is proven. |
| **Acceptance** | Tier-1 integration scenarios run through VM; Linq path documented as legacy with a removal target. |
| **Related** | `docs/decisions/2026-06-08-vm-as-canonical-semantics.md` |

---

### INT-004 — Fail fast when `PrimitiveExpansionMetadata` is missing

| Field | Value |
|-------|-------|
| **Status** | `done` |
| **Area** | Pipeline robustness |
| **Files** | `Poly/Interpretation/Interpreter.cs` (`CompileCore` — release throws `InvalidOperationException`) |
| **Problem** | Missing expansion metadata triggers silent re-expansion with `Debug.WriteLine` only. Production builds can mask analysis pipeline bugs. |
| **Action** | Replace fallback with `InvalidOperationException` (or structured diagnostic) when metadata is absent and `ExpansionPass` was expected to run. Keep fallback behind `#if DEBUG` only if still needed for prototyping. |
| **Acceptance** | Unit test: compile without running `UsePrimitiveExpansion` → clear failure, not silent re-expand. |

---

## P2 — Architecture Completion & ABI

### INT-005 — Nested function frame return (in-delegate `Return`)

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | VM / call ABI |
| **Files** | `Poly/Interpretation/Vm/ProgramCompiler.cs` (`EmitReturnOp`, `EmitPrimitiveCall`), `Poly/Interpretation/Vm/README.md` |
| **Problem** | `EmitReturnOp` always jumps to `ExitLabel`, ending the entire compiled delegate. Cross-function calls within a single delegate cannot restore caller PC/FrameBase. Today this is masked because each lambda body is a separate compiled delegate. |
| **Action** | Implement frame-return: read metadata slot, restore caller PC/FB, continue at `ReturnPC`. Update `Vm/README.md` ABI section when complete. |
| **Acceptance** | VM test: nested `Call` within one program returns to caller without terminating outer execution. |
| **Blocked by** | First consumer requiring in-delegate multi-function programs (closures called from within other closures in one module). |

---

### INT-006 — Propagate ring depth into `VmProgram.MaxActiveLocalsDepth`

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | VM / compilation |
| **Files** | `Poly/Interpretation/Vm/ProgramCompiler.cs` (hardcoded `32`), `Poly/Interpretation/Vm/VmProgram.cs`, `Poly/Interpretation/Interpreter.cs` |
| **Problem** | `ConfigureRingAllocation` computes real ring depth, but `CompilePrimitives` returns `new VmProgram(del, 32)` and register arrays are initialized to length 32 unconditionally. Deep expressions may silently truncate or over-allocate. |
| **Action** | Thread computed max ring depth from `CompilationContext` into `VmProgram` and `VmState` register allocation. |
| **Acceptance** | Test with artificially deep expression tree; execution succeeds with correctly sized register array. |

---

### INT-007 — Expose customizable analysis pipeline without bypassing `Interpreter`

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | API ergonomics |
| **Files** | `Poly/Interpretation/Interpreter.cs` |
| **Problem** | Static cached `_analyzer` is fast but prevents consumers from composing alternate pass pipelines (e.g. skipping expansion for inspection-only analysis). |
| **Action** | Add `Interpreter.CreateStandardAnalyzer()` or `StandardPipeline` builder factory; document that `Compile` requires the expansion pass. |
| **Acceptance** | Test helper or benchmark can build a custom `Analyzer` and still call `Interpreter.Compile(node, analysis)`. |

---

### INT-008 — Verify and fix `TypeIs` lowering fidelity

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Lowering / correctness |
| **Files** | `Poly/Syntax/Nodes/TypeIs.cs`, related expansion / `CallExternal` targets |
| **Problem** | `TypeIs.ToPrimitives()` emits only the operand — no type test. Pattern-matching and runtime type branching produce wrong results. |
| **Action** | Short-term: emit `CallExternal` to a correct `IsInstanceOfType` path. Long-term: emit `TypeCheck` primitive (see INT-020) once stable type IDs exist from AST analysis. |
| **Acceptance** | `TypeIs(Constant("hello"), int)` returns false through VM path. |
| **Related** | INT-020, ANA-002 (resolved type IDs) |

---

### INT-018 — Exception region primitives and EH tables

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Priority** | **P1** (highest primitive IR leverage) |
| **Area** | Primitive IR / VM / exceptions |
| **Files** | `Poly/Syntax/Primitives/Primitives.cs`, `Poly/Syntax/Nodes/TryCatchFinally.cs`, `Poly/Syntax/Nodes/UsingStatement.cs`, `Poly/Syntax/Nodes/ThrowStatement.cs`, `Poly/Interpretation/Vm/ProgramCompiler.cs` |
| **Problem** | No EH primitives or region metadata. `TryCatchFinally` lowers to try-body only; `UsingStatement` skips dispose-in-finally; `Throw` is emitted from AST but `ProgramCompiler` emits nothing (`PrimThrow => null`). CLR faults and IR throws use different paths. |
| **Action** | 1. Add region primitives or `Module` EH table entries (`EnterRegion`, `LeaveRegion`, `CatchDispatch` — exact names TBD). 2. Lower `TryCatchFinally`/`UsingStatement` using AST EH metadata (see ANA-003). 3. Implement `EmitThrowOp` wired to region dispatch. |
| **Acceptance** | VM tests: explicit `throw` caught by `catch`; `finally` runs on both normal and exceptional exit; `using` disposes on exceptional exit. |
| **Related** | INT-001 (subsumed once EH lands), INT-021, ANA-003 |
| **Enabling AST analysis** | ANA-003 `ExceptionRegionAnalysisPass` |

---

### INT-019 — Portable indexed call sites (serializable `CallExternal`)

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Priority** | **P1** |
| **Area** | Primitive IR / portability |
| **Files** | `Poly/Syntax/Primitives/Primitives.cs` (`CallExternal` embeds `MethodInfo`), `Poly/Interpretation/Vm/ProgramCompiler.cs`, `Poly/Syntax/Nodes/Invoke.cs`, `Poly/Syntax/Nodes/Member.cs` |
| **Problem** | `CallExternal(MethodInfo, …)` is process-local and unserializable. Every execution must re-run analysis + lowering. Blocks bytecode serialization ADR, macro library persistence, and lazy remote resolution. |
| **Action** | Replace or wrap with `CallSite(int siteIndex)` + side table `(assembly, type, method, signature)`. Resolve at module load. Wire `PermissionSet` check at dispatch (INT-030). |
| **Acceptance** | Round-trip: analyze → expand → serialize module → deserialize in fresh process → execute with identical behavior. |
| **Related** | `docs/decisions/2026-06-08-bytecode-serialization.md`, INT-021, INT-030 |
| **Enabling AST analysis** | ANA-004 `CallSiteCatalogPass` |

---

### INT-020 — `TypeCheck` / `IsInstance` primitive

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Priority** | **P1** |
| **Area** | Primitive IR / correctness |
| **Files** | `Poly/Syntax/Primitives/Primitives.cs`, `Poly/Syntax/Nodes/TypeIs.cs`, `Poly/Syntax/Nodes/TypeAs.cs`, `Poly/Syntax/Nodes/SwitchStatement.cs` |
| **Problem** | Runtime type tests have no portable primitive. `TypeIs` lowering is broken; workaround via `CallExternal` couples IR to CLR. |
| **Action** | Add `TypeCheck(int typeId)` (or `IsInstance(TypeRef)`) operating on heap handles. Lower `TypeIs`/`TypeAs`/typed `switch` arms using stable type IDs from analysis. |
| **Acceptance** | `TypeIs`, `TypeAs`, and type-based `switch` correct through VM without per-site `MethodInfo`. |
| **Related** | INT-008, ANA-002 |
| **Enabling AST analysis** | ANA-002 `ResolvedTypeIdMetadata` (extends type resolution) |

---

### INT-009 — SSA slot migration (`InputSlots` / `ResultSlot` / `CompileModule`)

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | IR / compiler |
| **Files** | `Poly/Syntax/Primitives/PrimitiveNode.cs`, `Poly/Interpretation/Vm/ProgramCompiler.cs`, `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` |
| **Problem** | ADR promises explicit SSA slots and `CompileModule()`, but compilation still simulates dataflow via `StackEffect` + `ComputePrimitiveConsumedPcs`. `Phi` is annotation-only `(0,0)`. `InputSlots`/`ResultSlot`/`ValueSlot` types referenced in docs but not implemented on `PrimitiveNode`. |
| **Action** | Defer until first real consumer (peephole optimizer, bytecode serialization, or backend needing explicit def-use). Then migrate expression nodes first (`Add`, `IfStatement`), add `CompileModule()`. |
| **Blocked by** | First consumer per core engineering principles — see `docs/plans/abstract-interpretation-and-ssa.md`, `docs/SsaPhiImplementationPlan.md`. |
| **Enabling AST analysis** | ANA-005 `ExpressionValueFlowPass` (pre-expansion slot assignment on AST) |

---

### INT-021 — `Module` container as first-class portable IR artifact

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Primitive IR / platform |
| **Files** | `Poly/Syntax/Primitives/README.md` (references `Module.Build()` — not implemented), `Poly/Interpretation/Vm/ProgramCompiler.cs`, `Poly/Interpretation/Interpreter.cs` |
| **Problem** | Primitives are a flat compilation input, not a durable artifact. README documents `Module`/`BasicBlock` wrappers in `Syntax/Primitives/` but types do not exist. CFG `BasicBlock` lives only in analysis, not in IR. Blocks macro caching, post-lowering insight analysis, and cross-session execution. |
| **Action** | Add `Module { Functions[], Constants[], Blocks[], ExceptionRegions[], CallSites[] }` and `Module.Build()` from linked primitive lists. Make `Interpreter.Compile` produce `VmProgram` from a `Module`. |
| **Acceptance** | Macro library can store/load a `Module`; analysis telemetry can reference module-level structure. |
| **Related** | INT-019, INT-018, INT-029, `docs/decisions/2026-06-post-lowering-insight-analysis.md` |
| **Enabling AST analysis** | ANA-006 `ModuleBoundaryPass` (catalog functions, constants, entry point before expansion) |

---

### INT-022 — `Suspend` interrupt primitive

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Primitive IR / execution control |
| **Files** | `Poly/Syntax/Nodes/SuspendNode.cs`, `Poly/Syntax/Primitives/Primitives.cs`, `Poly/Interpretation/Vm/ProgramCompiler.cs`, `Poly/Interpretation/Vm/VmState.cs` |
| **Problem** | `SuspendNode` lowers to its inner expression only. Suspension is only available via `DebugInterrupt` callback in Debug/Normal compile modes — not part of the IR. Blocks actor patterns, resumable execution, and serializable suspended state. |
| **Action** | Add `Suspend(int reason)` µop that sets `InterpreterStatus.Suspended`, preserves PC, and cooperates with `ExecutionResult.Resume`. |
| **Acceptance** | VM test: hit `SuspendNode` → `IsSuspended` → `Resume` continues with correct stack/PC. |
| **Related** | INT-012 (breakpoint ADR superseded by `DebugInterrupt` — suspend primitive bridges both models) |
| **Enabling AST analysis** | ANA-007 `SuspendPointAnalysisPass` (mark intentional yield points, actor boundaries) |

---

### INT-023 — Compare+branch fusion primitives

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Primitive IR / performance |
| **Files** | `Poly/Syntax/Primitives/Primitives.cs`, `Poly/Interpretation/Vm/ProgramCompiler.cs`, `Poly/Syntax/Nodes/IfStatement.cs` |
| **Problem** | Every conditional branch is `BinaryOp` (pushes 0/1) + `CondGoto` (pops it) — two steps, extra ring slots. Comparison-fusion ADR (`2026-06-09-comparison-fusion-encoding.md`) endorsed fused super-instructions at lowering, not subtract-trick semantics. |
| **Action** | Add `BranchIfEq`, `BranchIfLt`, etc. (or `CompareBranch(OpKind, Label)`). Lower from AST when condition is a standalone comparison (see ANA-008). |
| **Acceptance** | Fused branch emits one µop; behavior matches unfused `BinaryOp` + `CondGoto` on all comparison edge cases including signed overflow. |
| **Related** | INT-009, INT-029, `docs/decisions/2026-06-09-comparison-fusion-encoding.md` |
| **Enabling AST analysis** | ANA-008 `BranchConditionShapePass` |

---

### INT-024 — Value representation / tagging at VM ABI boundary

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | VM ABI |
| **Files** | `Poly/Interpretation/Interpreter.cs` (`InterpretResult`), `Poly/Interpretation/Vm/ProgramCompiler.cs` (`EmitCallExternalDirect`), `Poly/Syntax/Primitives/Primitives.cs` |
| **Problem** | All stack slots are `long`; heap handles, booleans, and integers share one representation. Result extraction uses heuristics; `CallExternal` marshaling infers representation at compile time from CLR types. Non-CLR backends cannot reuse this ABI. |
| **Action** | Either tagged slots (`ValueKind` + payload) or explicit `Box`/`Unbox`/`IsHeapRef` primitives. Drive from AST `ValueRepresentationMetadata` (ANA-001). |
| **Acceptance** | INT-002 regression passes; `CallExternal` marshaling reads representation metadata instead of re-deriving from `MethodInfo` parameter types. |
| **Related** | INT-002 |
| **Enabling AST analysis** | ANA-001 `ValueRepresentationPass` |

---

### INT-028 — `New` lowering uses invalid `Call(funcIndex: 0)`

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Lowering / correctness |
| **Files** | `Poly/Syntax/Nodes/New.cs`, `Poly/Syntax/Primitives/Primitives.cs` |
| **Problem** | `New.ToPrimitives()` emits `Call(Arguments.Length, funcIndex: 0)` with no registered constructor at index 0. Object construction is broken or accidental. |
| **Action** | Add constructor table entry on `Module` (INT-021) or emit `CallExternal` to resolved constructor with indexed call site (INT-019). |
| **Acceptance** | `new MyType(args)` constructs correctly through VM. |
| **Related** | INT-019, INT-021, ANA-004 |

---

## P3 — Documentation & Hygiene

### INT-010 — Update `Interpretation/README.md` execution API references

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Documentation |
| **Files** | `Poly/Interpretation/README.md` |
| **Problem** | README references `Vm.Execute(program)` but the API is `Interpreter.Execute(VmProgram)`. |
| **Action** | Replace stale references; align code samples with `Interpreter.cs`. |

---

### INT-011 — Refresh `vm-gap-analysis.md` against current implementation

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Documentation |
| **Files** | `docs/decisions/vm-gap-analysis.md` |
| **Problem** | Document is stale: claims no array opcodes (now `ArrayLoad`/`NewArray` exist), no breakpoints (now `DebugInterrupt`), no heap reclamation (now free-list), references removed tree-walker opcodes. |
| **Action** | Rewrite as a living gap doc: mark resolved items, keep open gaps (tracing GC, domain opcodes, async, serialization). Cross-link to this tracker. |

---

### INT-012 — Update breakpoint ADR to reflect `DebugInterrupt` design

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Documentation / decisions |
| **Files** | `docs/decisions/2026-06-08-breakpoint-architecture.md`, `Poly/Interpretation/Vm/VmState.cs`, `Poly/Interpretation/Vm/ProgramCompiler.cs` |
| **Problem** | ADR describes `Int`/`Iret` opcode vectors and `BreakpointPCs` on `VmState`. Implementation uses `Action<VmState>? DebugInterrupt` callback before each µop in Debug/Normal mode (per AGENTS.md). |
| **Action** | Amend ADR status to "Superseded" or update decision text to match shipped design. Reference `docs/plans/vm-debugger-architecture.md` if applicable. |

---

### INT-013 — Fix diagnostics doc path in `Analysis/README.md`

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Documentation |
| **Files** | `Poly/Interpretation/Analysis/README.md`, `Poly/Interpretation/Analysis/DIAGNOSTICS_EXAMPLE.md` |
| **Problem** | README links to `docs/interpretation/diagnostics-example.md` which does not exist. Actual file is co-located `DIAGNOSTICS_EXAMPLE.md`. |
| **Action** | Correct the link path. |

---

### INT-014 — Refresh `docs/technical/lowering-and-vm.md`

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Documentation |
| **Files** | `docs/technical/lowering-and-vm.md` |
| **Problem** | References removed `VirtualMachine/` directory, old `Bytecode` model, and `Vm.Execute` entry point. Does not describe primitive-based `ProgramCompiler` path. |
| **Action** | Rewrite to match current architecture: primitives → `PrimitiveLinker` → `ProgramCompiler.CompilePrimitives` → `Interpreter.Execute`. |

---

## Cross-Cutting / Deferred (track, do not schedule yet)

### INT-015 — Tracing GC or root-set heap sweep for long-running synthesis

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | VM / memory |
| **Files** | `Poly/Interpretation/Vm/Heap.cs` |
| **Problem** | Free-list recycles explicitly nulled slots but unreachable object graphs still grow `_count`. Evolution/synthesis loops may OOM. |
| **Blocked by** | First long-running synthesis benchmark demonstrating heap growth. See `docs/decisions/2026-06-08-heap-reclamation.md`. |

---

### INT-016 — Retire `LinqExpressionGenerator` entirely

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | Consolidation |
| **Files** | `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs` |
| **Depends on** | INT-003 |
| **Action** | Delete generator and tests once VM parity is proven and no external consumer (PolicyEvaluator, etc.) depends on it. |

---

### INT-017 — Align `vm-test-gap-closure.md` with this tracker

| Field | Value |
|-------|-------|
| **Status** | `open` |
| **Area** | Documentation |
| **Files** | `docs/plans/vm-test-gap-closure.md` |
| **Problem** | Separate test-gap plan may overlap with INT-001, INT-005, INT-008. Risk of duplicate tracking. |
| **Action** | Merge or cross-reference: mark gaps closed since the plan was written; link remaining items to INT-* IDs. |

---

### INT-025 — `IncLocal` / `DecLocal` primitives (documented, not implemented)

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | Primitive IR / loops |
| **Files** | `Poly/Syntax/Primitives/README.md`, `Poly/Syntax/Nodes/ForLoop.cs` |
| **Problem** | README lists `IncLocal`/`DecLocal` but types are absent. Loop increments lower to `LoadLocal` + `BinaryOp` + `StoreLocal`. |
| **Action** | Add primitives; lower from AST when loop induction pattern is recognized (ANA-009). |
| **Blocked by** | INT-029 (optimizer) or first loop-heavy benchmark showing hot-path bloat. |
| **Enabling AST analysis** | ANA-009 `LoopInductionPatternPass` |

---

### INT-026 — `ForEach` / iterator lowering contract

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | Lowering / correctness |
| **Files** | `Poly/Syntax/Nodes/ForEachLoop.cs` |
| **Problem** | `ForEachLoop.ToPrimitives()` is a stub: evaluates collection, discards, runs body once. No iteration. |
| **Action** | Either iterator primitives (`EnumeratorMoveNext`, `CurrentLoad`) or documented lowering to indexed `CallSite` chain once catalog exists. |
| **Blocked by** | INT-019 (call site catalog) or first `ForEach` consumer test. |
| **Enabling AST analysis** | ANA-010 `EnumeratorPatternResolutionPass` (resolve `IEnumerable<T>` element type and enumerator methods at AST level) |

---

### INT-029 — Primitive-level peephole optimizer

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | IR optimization |
| **Files** | (not implemented — ADR references removed `byte[]` pipeline) |
| **Problem** | `docs/decisions/2026-06-08-peephole-optimizer.md` describes optimizer on old bytecode. No optimizer runs on primitive lists today. |
| **Action** | Implement `PrimitiveOptimizer.Optimize(IReadOnlyList<PrimitiveNode>)` with identity-fold patterns; extend with fusion when INT-023 lands. |
| **Blocked by** | INT-009 (SSA slots make pattern matching trivial) or first macro-size benchmark. |
| **Related** | INT-023, INT-025 |

---

### INT-030 — `CallExternal` sandbox (`PermissionSet`)

| Field | Value |
|-------|-------|
| **Status** | `blocked` |
| **Area** | VM / security |
| **Files** | `Poly/Interpretation/Vm/ProgramCompiler.cs`, `Poly/Interpretation/Vm/VmState.cs` |
| **Problem** | ADR accepted (`2026-06-08-sandboxing-approach.md`) but `PermissionSet` not checked at `CallExternal` dispatch. Untrusted macros have full CLR access. |
| **Action** | Implement permission check at indexed call site dispatch (pairs with INT-019). |
| **Blocked by** | INT-019 (indexed call sites are the natural enforcement point). |

---

## AST Analysis Enablers

High-level AST analysis should **front-load decisions** that are expensive or fragile to rediscover from flat primitives. The passes below are proposed additions to the pipeline (mostly pre-expansion, after type resolution + CFG). Each maps to primitive IR issues above.

**Principle:** Analysis on structured nodes is cheaper and more precise than recovering the same facts from a flat µop list after `ExpansionPass`.

### Proposed passes (not yet implemented)

Use **ANA-*** IDs for proposed analysis work; **INT-*** IDs track primitive/VM implementation.

| ID | Pass | Placement | Metadata produced | Unblocks |
|----|------|-----------|-------------------|----------|
| **ANA-001** | `ValueRepresentationPass` | **P0** — after CFG | `ValueRepresentationMetadata` per expression: `StackScalar`, `HeapRef`, `Bool`, `Void`, etc. | INT-002, INT-024, INT-020, `CallExternal` marshaling, `BinaryOp.ComparisonType` |
| **ANA-002** | Resolved type IDs (extend `TypeAndMemberResolver`) | P2 — after P0 | `ResolvedTypeIdMetadata` — stable int/string ID per `TypeReference` | INT-020 (`TypeCheck`), serialization, portable `switch` |
| **ANA-003** | `ExceptionRegionAnalysisPass` | **P0** — pre-expansion | `ExceptionRegionMetadata`: protected ranges, handler entries, finally ordering, `UsingStatement` dispose targets | INT-018, INT-001, `UsingStatement` |
| **ANA-004** | `CallSiteCatalogPass` | **P0** — after ANA-001 | `CallSiteIndexMetadata` on `Invoke`/`Member`/`New`; module-level `CallSiteTable` | INT-019, INT-028, INT-030 (sandbox), INT-026 |
| **ANA-005** | `ExpressionValueFlowPass` | After CFG + constant folding | `AstValueSlot` per expression node; merge points for `IfStatement`/`Phi` | INT-009 (SSA migration — expansion emits slots, compiler stops simulating stack) |
| **ANA-006** | `ModuleBoundaryPass` | Before expansion | `FunctionCatalogMetadata`, `ConstantPoolMetadata`, entry-point node | INT-021 (`Module` container) |
| **ANA-007** | `SuspendPointAnalysisPass` | After CFG | `SuspendPointMetadata` on `SuspendNode`, `Await`, actor boundary nodes | INT-022 |
| **ANA-008** | `BranchConditionShapePass` | After constant folding | `FusibleBranchMetadata` when condition is standalone `Equal`/`LessThan`/etc. used only as branch test | INT-023 (compare+branch fusion) |
| **ANA-009** | `LoopInductionPatternPass` | After constant folding | `InductionVariableMetadata` when `ForLoop.Increment` is `Add(var, const)` / `Subtract` | INT-025 (`IncLocal`/`DecLocal`) |
| **ANA-010** | `EnumeratorPatternResolutionPass` | After type + member resolution | `EnumeratorPatternMetadata` on `ForEachLoop`: element type, `GetEnumerator`/`MoveNext`/`Current` call sites | INT-026 |

### What existing passes already contribute

| Existing pass | Already helps lower layers |
|---------------|---------------------------|
| `TypeAndMemberResolver` | `CallExternal` target selection, `BinaryOp.ComparisonType` for ref equality, lambda/invoke dispatch — extend for ANA-002/ANA-004 |
| `SideEffectAnalyzer` + `ElisionMetadata` | Skips dead subtrees in `ExpansionPass` — reduces primitive bloat before any optimizer |
| `ControlFlowAnalysisPass` | Reachability, infinite-loop facts, `MustExecuteMetadata` — foundation for ANA-003 (EH regions follow CFG shape) |
| `ConstantFoldingPass` | Eliminates branches before expansion; prerequisite for ANA-008 (fuse only non-constant comparisons) |
| `DefiniteAssignmentAnalyzer` | Catch clause variable binding facts — needed for ANA-003 catch handler slot layout |
| `JumpTargetAnalyzer` | Label resolution for loops/break — expansion inherits correct branch targets |
| `LambdaReturnTypeAnalyzer` | Function signature for `Module` catalog (ANA-006) |

### Recommended analysis-first execution order

Front-load AST metadata before primitive work — it de-risks multiple IR issues in parallel:

```
Phase A — Representation & catalog (unblocks many primitives)
  ANA-001 ValueRepresentationPass
  ANA-002 Resolved type IDs
  ANA-004 CallSiteCatalogPass
      ↓
Phase B — Shape analysis (unblocks specific primitives)
  ANA-003 ExceptionRegionAnalysisPass
  ANA-008 BranchConditionShapePass
  ANA-006 ModuleBoundaryPass
      ↓
Phase C — Primitive IR implementation
  INT-018, INT-019, INT-020, INT-021, INT-024, INT-028
      ↓
Phase D — Optimization & advanced IR
  ANA-005 ExpressionValueFlowPass → INT-009
  INT-029 Peephole optimizer
  INT-023, INT-025
```

### Analysis passes to avoid (for now)

| Idea | Why defer |
|------|-----------|
| Full AST → SSA rename before expansion | High complexity; ANA-005 (slot assignment without full rename) is enough until INT-009 consumer exists |
| Post-expansion-only analysis replacing AST passes | Loses structure; contradicts post-lowering insight vision for *authoring* feedback |
| Domain-specific analysis in Interpretation | Belongs in `DomainModeling`; lowers to generic primitives per domain-lowering ADR |

---

## Suggested Execution Order

```
── P0 SPRINT — COMPLETE ✅ 1387/1387 tests ─────────────────
DONE:   ANA-001, ANA-003, ANA-004 (passes + consumers + tests)
        INT-004 (fail fast), INT-008 (TypeCheck)
        New.ToPrimitives + catalog wiring, incremental state isolation
        Expansion integration tests, all fix-up items resolved
PARTIAL: INT-001 (EmitThrowOp + try/catch + basic try/finally + multi-catch wired via Strategy B; blocked on full INT-018 for using dispose, catch-var binding, nested EH)
    ↓
── P1 primitive IR (unblocked by P0) ───────────────────────────
INT-018 (EH primitives)               needs ANA-003 metadata — ready
INT-019 (indexed call sites)          needs ANA-004 catalog — ready
INT-028 (New lowering)                needs ANA-004 — partial; CallExternal wired
INT-024 (value ABI)                   needs ANA-001 — partial fix in InterpretResult
INT-003 (Linq migration), INT-020 (TypeCheck primitive → stable type IDs)
    ↓
── P2+ (deferred) ───────────────────────────────────────────
ANA-002 (stable type IDs), ANA-005 (SSA slots), ANA-006 (Module container)
INT-021 (Module), INT-009 (SSA), INT-029 (peephole), INT-022 (Suspend)
ANA-FIX-008 (CFG reachability for catch), ANA-003-T5/T6 (CFG + catch slots)
INT-010/011/012/014/017 (docs — anytime)
```

---

## Changelog

| Date | Change |
|------|--------|
| 2026-07-05 | Initial tracker from Interpretation system code review. |
| 2026-07-05 | Added primitive IR issues INT-018–INT-030, INT-028; AST analysis enabler section ANA-001–ANA-010; analysis-first execution order. |
| 2026-07-05 | Elevated ANA-001, ANA-003, ANA-004 to **P0** with full task lists (T1–T12); reordered execution order around analysis-first sprint. |
| 2026-07-05 | Post-implementation review: added **P0 Fix-Up** section (ANA-FIX-001–013); marked ANA passes `in-progress`; checked completed tasks; execution order prioritizes fix-up. |
| 2026-07-05 | Second fix-up review: marked ANA-FIX-001/002/004/006/007/013 `done`; ANA-FIX-003/009/011/012 `in-progress`; added **ANA-FIX-014–021** from second-review findings (regression tests, INT-002 integration, incremental state, constructor ArgCount, null ABI, value repr tests, fallback heuristic, doc hygiene). |
| 2026-07-05 | Third review + sprint wrap-up: added **P0 Sprint Wrap-Up** section (**SPRINT-W1–W6**); updated fix-up statuses; ANA-001-T9 checked; execution order reprioritized around consumer wiring and test closure. |
| 2026-07-05 | Added **global verification gates** and per-package **SPRINT-W*-V*** definition-of-done tasks (regression probes, execution checks, incremental equivalence). |
| 2026-07-05 | Verification review: updated exit checklist, **Remaining work** table, SPRINT-W1 `done`, W2–W5 `in-progress` with per-V pass/fail; ANA-001 `done`; ANA-003/004 `in-progress`; corrected README (sprint not closed). |
| 2026-07-05 | **Sprint closure.** All SPRINT-W1–W6 tasks complete. ANA-001/003/004 → `done`. INT-001/004/008 → `done`. 1387/1387 tests. Catalog resolution in `EmitCallExternalDirect`; `New.ToPrimitives` wired; `TypeCheck` primitive; `EmitThrowOp`; incremental state isolation; expansion integration tests. Tracker updated; README reflects sprint completion. |