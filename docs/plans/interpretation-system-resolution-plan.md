# Interpretation System — Resolution Plan

**Created:** 2026-07-05  
**Updated:** 2026-07-06 (checkbox sync #2)  
**Source:** [`docs/interpretation-system-architecture-review.md`](../interpretation-system-architecture-review.md) (Rev 1.15)  
**Companion:** [`interpretation-system-issues.md`](interpretation-system-issues.md) (INT-/ANA- tracker)  
**Baseline:** 1420/1420 tests green (was 1408); P0 analysis sprint complete; **Phase 1 in progress** (Strategy B try/catch MVP landed); **P2 harness + P3 hardening partial** (uncommitted).

This plan turns architectural findings into **ordered, checkable work**. Task IDs are stable (`P0-001`, `P1C-012`, …). Check boxes in PRs or update status inline as work lands.

### Progress snapshot (2026-07-06)

| Phase | Done | In progress | Open | Notes |
|-------|------|-------------|------|-------|
| **P0** Truth sync | 8/16 | — | 8 | Review §5 + several C-* resolved; tracker/vision ADRs still open |
| **P1** EH (INT-018) | 22/36 | 14 | — | P1A+P1B done; P1C try/catch MVP; finally/using/nested remain |
| **P2** Parity | 4/27 | 2 | 21 | Harness + 2 MatchLinq tests (uncommitted); P2-003/004 open |
| **P3** Hardening | 9/21 | 2 | 10 | Ring save, TypeIs scalar, ABI partial, ExpansionPass guard |
| **P4–P5** | 0 | — | all | Not started |
| **P6** Hygiene | 2/24 | — | 22 | Dead CSharp code removed; Phi README fixed |

**Current focus:** P1C-030–042 (try/finally, using, nested EH), commit P2/P3 uncommitted work, then P0 tracker hygiene.

**Task status legend:** `open` | `in-progress` | `blocked` (needs Q#) | `done`

---

## Working agreement

**It is okay to be wrong.** This plan is a hypothesis. Update tasks, registers, and ADRs when evidence contradicts an item.

**Ask questions early.** Tasks marked `blocked — Q#` must not be guessed. Surface alternatives with tradeoffs. See [Open decisions](#open-decisions-for-the-owner).

**Per completed task:** implement → test → `dotnet run --project Poly.Tests/Poly.Tests.csproj` green → mark C-/K- resolved in architecture review → update tracker → ADR if policy-level.

---

## How to use this document

| Section | Use |
|---------|-----|
| [Priority overview](#priority-overview) | Sequencing and dependencies |
| [Phase summaries](#phase-summaries) | Goals and exit criteria |
| [Detailed task backlog](#detailed-task-backlog) | **Executable checklist** — start here |
| [Open decisions](#open-decisions-for-the-owner) | Unblock `blocked` tasks |
| [Register closure](#register-closure-checklist) | When to close C-/K- rows |

---

## Priority overview

```mermaid
flowchart TD
    P0[Phase 0: Truth sync] --> P1[Phase 1: EH INT-018]
    P1 --> P2[Phase 2: Cross-engine parity]
    P1 --> P3[Phase 3: VM hardening]
    P2 --> P4[Phase 4: Portable IR INT-019]
    P3 --> P4
    P0 --> P5[Phase 5: Domain bridge]
    P4 --> P6[Phase 6: Hygiene]
```

| Phase | Theme | Task ID prefix | Exit |
|-------|-------|----------------|------|
| **0** | Truth sync | `P0-` | Doc/tracker/ADR aligned |
| **1** | EH (INT-018) | `P1A-` `P1B-` `P1C-` | throw/catch/finally/using correct |
| **2** | Parity (INT-003) | `P2-` | ≥60% overlapping features cross-validated |
| **3** | VM hardening | `P3-` | C-022, ring/closure/TypeIs gaps closed |
| **4** | Portable IR (INT-019) | `P4-` | Serialize + round-trip execute |
| **5** | Domain bridge | `P5-` | Transition documented; scope decided |
| **6** | Hygiene | `P6-` | Dead code removed; deferred ADRs triaged |

---

## Phase summaries

### Phase 0 — Truth sync
**Goal:** Docs and tracker match code. No false `done`.  
**Exit:** High-severity doc-only C-* resolved; §5 header restored.  
**Status:** **in progress** — 8/16 tasks done.

### Phase 1 — Exception handling (INT-018)
**Goal:** VM EH matches semantics (Strategy B — side table).  
**Exit:** C-017, C-018, C-023 resolved; INT-018 `done`; INT-001 `done` only with catch/finally.  
**Status:** **in progress** — try/catch MVP landed (`ExceptionTableBuilder`, `DispatchException`, 8 EH tests); finally/using/nested open.

### Phase 2 — Cross-engine parity
**Goal:** VM ↔ Linq disagree in tests, not silently.  
**Exit:** C-016, C-026 resolved or narrowed; parameterized `AssertVmMatchesLinq`.  
**Status:** **in progress** — parameterized harness + 2 MatchLinq tests (uncommitted).

### Phase 3 — VM correctness hardening
**Goal:** Latent bugs exposed by tests.  
**Exit:** C-022, C-014, C-015, C-021 resolved.  
**Status:** **in progress** — ring save by `SavedSp`, scalar TypeIs, ABI partial, ExpansionPass depth guard.

### Phase 4 — Portable IR (INT-019)
**Goal:** Catalog + primitives serialize; one call path.  
**Exit:** INT-019 MVP; C-013, C-006 resolved.

### Phase 5 — Domain → VM (post-transition)
**Goal:** Dual-path intentional; scope explicit.  
**Exit:** C-019, C-020 updated per Q8.

### Phase 6 — Hygiene
**Goal:** Reduce dead code and doc debt.  
**Exit:** K-052–K-063 addressed or deferred with note.  
**Status:** **in progress** — `WriteTestTopLevelStatement` removed; Phi README fixed.

---

## Detailed task backlog

### Phase 0 — Truth sync (`P0-`)

#### P0-A — Architecture review hygiene

- [x] **P0-001** Restore `## 5. Contradiction register` header before the C-* table in `docs/interpretation-system-architecture-review.md` (currently missing after §4.22).
  - **Files:** `docs/interpretation-system-architecture-review.md` (~line 2367)
  - **Done:** 2026-07-06 (commit `3d7b1d7`)

- [x] **P0-002** Mark **C-004** resolved in §5 with `Status: resolved`, `Resolved: 2026-07-05`, note: README rewrite lists passes 1–13.
  - **Done:** 2026-07-05/06 in architecture review §5

- [ ] **P0-003** Fix §4.22.5 opening paragraph — still says "recommended Strategy A" before the Strategy B update note at §4.22.5 line ~2308.
  - **Files:** `docs/interpretation-system-architecture-review.md` §4.22.5

- [x] **P0-004** Update **K-046** in §6: mark superseded by K-027 / §4.12.7 (Strategy B primary).
  - **Done:** K-046 row updated in §6

- [x] **P0-005** Fix §3.4: `EmitPhi` is compile-time no-op (K-022), not runtime merge implementation.
  - **Done:** §3.4 SSA row corrected

#### P0-B — Tracker alignment

- [ ] **P0-010** Change **INT-001** status from `done` → `blocked` (blocked on INT-018) or `open`; update Problem/Acceptance to require catch/finally, not just `EmitThrowOp` exists.
  - **Files:** `docs/plans/interpretation-system-issues.md` §INT-001
  - **Maps:** C-002, C-012

- [ ] **P0-011** Update sprint summary / dependency graph: INT-001 not done until INT-018 Phase 1 complete.
  - **Files:** `docs/plans/interpretation-system-issues.md` (header, §SPRINT-W1 reference ~line 1305)

- [ ] **P0-012** Tracker hygiene: mark SPRINT-W6 rows ✅ where sprint closure completed; remove stale ❌.
  - **Files:** `docs/plans/interpretation-system-issues.md` §SPRINT-W6
  - **Maps:** C-009

#### P0-C — ADR reconciliation

- [x] **P0-020** Revise `docs/decisions/vm-gap-analysis.md` feature matrix: Exceptions → ✗; TypeIs → ✓; remove or reorder resolved priority items (#1 TypeIs, #2 GC, #4 breakpoints partial).
  - **Done:** 2026-07-06 (commit `3d7b1d7`); priority list annotated

- [ ] **P0-021** Refresh vm-gap-analysis EH row — still says "catch/finally bodies execute unconditionally" though try/catch dispatch is implemented; update to reflect P1C partial (try/catch ✓, finally/using ✗).
  - **Depends:** P0-020 (partial refresh)

- [x] **P0-022** Reconcile priority #7 "policy/event opcodes" with domain-lowering-boundary ADR — document V2 lowers to generic ops; remove or reword #7.
  - **Done:** C-025 resolved in review; vm-gap priority #7 updated

- [x] **P0-023** Fix `docs/decisions/README.md` index bullet: remove "tree-walker interpreter" wording; VM is canonical.
  - **Done:** 2026-07-06 (commit `3d7b1d7`)

- [ ] **P0-024** **Q1=defer** Add note to `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md` or add amendment doc: two-tier VM→backend; primitives as IR; no `Poly/Ir/`; no tree-walker.
  - **Files:** vision doc or new `docs/decisions/2026-07-05-vision-amendment-vm-primitives.md`
  - **Maps:** C-024, K-039

- [ ] **P0-025** **Q1=defer** Update `2026-07-04-primitives-as-canonical-ir.md` status note: Module/BasicBlock deferred until consumer emerges; flat `CompilePrimitives` sufficient.
  - **Maps:** C-001, K-003, INT-009, INT-021

- [ ] **P0-026** **Q2=defer all** Triage unimplemented ADRs — set explicit status on each:
  - `bytecode-serialization` → Deferred until INT-019 consumer
  - `peephole-optimizer` → Deferred (INT-008)
  - `sandboxing-approach` → Deferred (no PermissionSet)
  - `breakpoint-architecture` → Partial (`DebugInterrupt` only) — defer `BreakpointPCs`
  - **Files:** each ADR front-matter + `docs/decisions/README.md`
  - **Maps:** K-040, open Q13 in architecture review

#### P0-D — Verification (no code)

- [x] **P0-030** Run full test suite; confirm green.
  - **Done:** 1420/1420 (2026-07-06)

**Phase 0 exit checklist:**
- [x] P0-001, P0-002, P0-004, P0-005 (review doc) — P0-003 still open
- [ ] P0-010 through P0-012 (tracker)
- [x] P0-020, P0-022, P0-023 (ADR sync) — P0-021 refresh open
- [ ] P0-025 (Q1 answered defer; ADR note not yet written)

---

### Phase 1 — Exception handling (`P1A-` `P1B-` `P1C-`)

**Strategy:** Strategy B (runtime dispatch / side table) per §4.12.7 — **Q3 confirmed Yes**.

#### Phase 1a — Wire throw (`P1A-`) — **complete**

- [x] **P1A-001** In `ProgramCompiler` primitives switch: `PrimThrow => EmitThrowOp(consumedPcs, ctx)`.
  - **Done:** `ProgramCompiler.cs:160`

- [x] **P1A-002** Document `RegionMarker => null` as compile-time metadata only; `PrimThrowProtected` wired to `EmitThrowOp`.
  - **Done:** comments at `ProgramCompiler.cs:161–163`

- [x] **P1A-003** `Poly.Tests/Interpretation/ThrowVmTests.cs` — 4 tests (uncaught throw, message, void context, after local).
  - **Done:** 2026-07-06

- [x] **P1A-004** `Throw_OutsideTry_Propagates` in `ExceptionHandlingVmTests.cs` (covers unprotected throw through full pipeline).
  - **Done:** superseded dedicated test name; same acceptance

- [x] **P1A-005** `Throw_WithMessage_PropagatesCorrectMessage` verifies heap-handle dereference path.
  - **Done:** `ThrowVmTests.cs`

- [ ] **P1A-006** Tracker: INT-001 still marked `done` prematurely — update to `blocked` on INT-018 or add note that catch/finally acceptance is partial.
  - **Open:** `interpretation-system-issues.md` §INT-001 still `done`

**Phase 1a exit:** ✅ P1A-001, P1A-003 green.

---

#### Phase 1b — EH ADR (`P1B-`) — **mostly complete**

- [x] **P1B-001** ADR `docs/decisions/2026-07-05-vm-exception-handling-strategy-b.md` — Status: Accepted.
  - **Done:** 2026-07-06 (commit `3d7b1d7`)

- [x] **P1B-002** ADR documents Strategy B components: side table, handler `Functions[]`, dispatch algorithm.
  - **Done:** ADR §Strategy B Design

- [ ] **P1B-003** Link ADR from `Poly/Interpretation/Vm/README.md` EH section.
  - **Open:** no link in Vm README yet

**Phase 1b exit:** P1B-001 ✅ — P1B-003 remains.

---

#### Phase 1c — Strategy B implementation (`P1C-`)

##### P1C-1 — Types and table construction

- [x] **P1C-001** `ExceptionRegionTable` + `ExceptionRegionEntry` in `ExceptionRegionTable.cs`.
  - **Done:** includes `CatchTypeName`, `CatchVariableName`, `ParentRegionIndex`

- [x] **P1C-002** `ExceptionRegionTable? Regions` on `VmProgram`.
  - **Done:** `VmProgram.cs`

- [x] **P1C-003** `Interpreter.CompileCore` reads `ExceptionRegionMetadata` (null key).
  - **Done:** `Interpreter.cs:259+`

- [x] **P1C-004** `ExceptionTableBuilder.BuildTable(primitives, metadata)`.
  - **Done:** `ExceptionTableBuilder.cs`

- [x] **P1C-005** `ExceptionRegionTableTests.cs` — shape tests for try/catch, try/finally table entries.
  - **Done:** 2026-07-06

##### P1C-2 — Handler compilation

- [x] **P1C-010** `ExceptionTableBuilder.ExtractHandlerRanges(primitives)`.
  - **Done:** `ExceptionTableBuilder.cs` (not `ProgramCompiler`)

- [x] **P1C-011** Handler ranges compiled via `ProgramCompiler.CompilePrimitives` in `CompileCore`.
  - **Done:** `Interpreter.cs:268–277`

- [x] **P1C-012** Combined `Functions[]` with `WithHandlerIndexAt` updating table entries.
  - **Done:** `Interpreter.cs:280–296`

- [ ] **P1C-013** Catch variable binding — exception object not injected into catch body frame.
  - **Open:** typed catch test passes only when body ignores exception variable

##### P1C-3 — Main body + dispatch wrapper

- [x] **P1C-020** Normal path: `TryCatchFinally.ToPrimitives` emits `Goto(AfterCatches)` to skip catch bodies; handlers also compiled separately.
  - **Done:** `TryCatchFinally.cs` + flat main delegate

- [x] **P1C-021** `ProgramCompiler.DispatchException` — PC scan, handler invoke, rethrow if unhandled.
  - **Done:** `ProgramCompiler.cs:762+` — **gap:** no `CatchTypeName` filter yet; no `Finally` kind dispatch

- [x] **P1C-022** Main delegate wrapped in CLR `try/catch` → `DispatchException` in `CompileCore`.
  - **Done:** `Interpreter.cs:300–307` (runtime wrapper; `EmitExceptionDispatchWrapper` exists but unused)

- [x] **P1C-023** `PrimThrowProtected => EmitThrowOp` with fault PC saved before throw.
  - **Done:** `EmitThrowOp` sets `state.ProgramCounter`

- [x] **P1C-024** `TryCatch_NormalCompletion_SkipsCatch` — catch not run on normal exit.
  - **Done:** `ExceptionHandlingVmTests.cs`; C-017 marked resolved in review

##### P1C-4 — Try/finally and using

- [ ] **P1C-030** Try/finally without catch: finally handler runs on normal and exceptional exit.
  - **Test:** `TryFinally_Normal_FinallyRuns`, `TryFinally_Throw_FinallyThenRethrow`
  - **Maps:** INT-018 acceptance

- [ ] **P1C-031** `UsingStatement` / `LeaveUsingDispose`: dispose handler invoked on exceptional exit; normal exit runs dispose in sequence or via finally dispatch.
  - **Files:** may need `UsingStatement.ToPrimitives` order verified against ANA-FIX-007
  - **Maps:** K-045, ANA-FIX-007
  - **Related open tracker:** ANA-FIX-010 (EH tests)

- [ ] **P1C-032** Test: `Using_DisposeOnException`, `Using_DisposeOnNormalExit`
  - **Maps:** ANA-FIX-010

##### P1C-5 — Nested EH

- [ ] **P1C-040** Support `ParentRegionIndex` in dispatch: unwind to parent region when catch type does not match.
  - **Test:** `TryCatch_Nested_InnerCatch`, `TryCatch_Nested_OuterCatch`

- [ ] **P1C-041** Test: multiple catch clauses (type filter order).
  - **Test:** `TryCatch_MultipleCatch_FirstMatching`

- [ ] **P1C-042** Test: throw inside catch; finally inside nested try.
  - **Maps:** ANA-FIX-010

##### P1C-6 — Ring depth side table (optional in same PR or follow-up)

- [ ] **P1C-050** Add `PcToRingDepth?` side table on `VmProgram` for debugger/EH (ghost ValueStack — K-035).
  - **Files:** `VmProgram.cs`, `ProgramCompiler.cs`
  - **Maps:** K-035, open Q9
  - **Can defer** if not needed for dispatch MVP

##### P1C-7 — Cleanup and closure

- [x] **P1C-060** `RegionMarker => null` with INT-018 Strategy B comment.
  - **Done:** `ProgramCompiler.cs:163`

- [ ] **P1C-061** Update `vm-gap-analysis.md` EH row to reflect partial implementation (try/catch ✓, finally/using ✗).
  - **Open:** feature matrix still ✗ with stale note

- [ ] **P1C-062** Architecture review §5: **C-017 resolved**; **C-018** and **C-023** still open.
  - **Partial:** C-017/C-002/C-012 done 2026-07-06; C-018 awaits finally/using/nested

- [ ] **P1C-063** Mark **INT-018** `done` and reconcile **INT-001** in tracker.
  - **Blocked on:** P1C-030–032 minimum

- [ ] **P1C-064** Remove stale EH placeholder comments in `ProgramCompiler.cs`, `Primitives.cs`, architecture review §7 (still says EmitThrowOp dead).

**Phase 1 minimum test matrix (all required for exit):**

| Test ID | Scenario | Status |
|---------|----------|--------|
| T-EH-01 | Uncaught throw propagates | ✅ `ThrowVmTests`, `Throw_OutsideTry_Propagates` |
| T-EH-02 | Throw caught; catch returns value | ✅ `TryCatch_Throw_CatchReturnsValue` |
| T-EH-03 | Try/finally; no throw; finally runs | ❌ |
| T-EH-04 | Throw; finally runs; exception propagates | ❌ |
| T-EH-05 | Normal try completion; catch skipped | ✅ `TryCatch_NormalCompletion_SkipsCatch` |
| T-EH-06 | Using dispose on normal exit | ❌ |
| T-EH-07 | Using dispose on exception | ❌ |
| T-EH-08 | Nested try/catch | ❌ |

---

### Phase 2 — Cross-engine parity (`P2-`)

#### P2-A — Harness infrastructure

- [x] **P2-001** Refactor `AssertVmMatchesLinq` in `VmCorrectnessTests.cs` to accept `object?[] args` and pass to both LINQ `DynamicInvoke(args)` and VM `SetArgs`.
  - **Files:** `Poly.Tests/Interpretation/VmCorrectnessTests.cs` (~272–295)
  - **Maps:** K-047, C-026, §4.13.5 Phase 1
  - **Done:** `AssertVmMatchesLinqImpl(expr, subject, args)` — uncommitted

- [x] **P2-002** Add overload `AssertVmMatchesLinq(DomainExpression expr)` → calls with empty args (preserve existing 11 tests).
  - **Done:** overloads at ~272–282 — uncommitted

- [ ] **P2-003** Extract shared `NormalizeResult(object?) → long` used by both paths (bool → 0/1, null → 0, etc.).
  - **Open:** inline switch in `AssertVmMatchesLinqImpl`; not yet extracted

- [ ] **P2-004** Fix **K-048**: either add LINQ comparison to `AssertVmMatchesLinqComposite` OR rename to `AssertVmMultiCase` and document VM-only.
  - **Files:** `VmCorrectnessTests.cs` (~381–410)

#### P2-B — Breadth-first MatchLinq tests (Syntax.Node or DomainExpression)

- [x] **P2-010** `MatchLinq_PropertyAccess_Age` — parameterized entity
  - **Done:** `VmCorrectnessTests.cs` — uncommitted (manual LINQ/VM compare; not yet routed through harness)
- [ ] **P2-011** `MatchLinq_PropertyAccess_NameEq`
- [x] **P2-012** `MatchLinq_MethodCall_StringLength`
  - **Done:** `VmCorrectnessTests.cs` — uncommitted
- [ ] **P2-013** `MatchLinq_MethodCall_MathMax`
- [ ] **P2-014** `MatchLinq_Conditional_WithEntity`
- [ ] **P2-015** `MatchLinq_IfElse_WithComparison`
- [ ] **P2-016** `MatchLinq_WhileLoop_Count`
- [ ] **P2-017** `MatchLinq_ForLoop_Count` (if VM supports)
- [ ] **P2-018** `MatchLinq_DoWhileLoop`
- [ ] **P2-019** `MatchLinq_Coalesce`
- [ ] **P2-020** `MatchLinq_TypeIs_HeapRef` — **blocked** until P3-040 or confirm TypeCheck works
- [ ] **P2-021** `MatchLinq_Lambda_NoCapture` — simple `() => expr`
- [ ] **P2-022** `MatchLinq_Lambda_WithCapture` — **blocked** until P3-030
  - **Maps:** C-016, K-024

#### P2-C — EH cross-validation (blocked on Phase 1)

- [ ] **P2-030** `MatchLinq_Throw_Uncaught` — both engines throw
- [ ] **P2-031** `MatchLinq_TryCatch_ReturnsCatchValue`
- [ ] **P2-032** `MatchLinq_TryFinally_ReturnsFinallyValue`
- [ ] **P2-033** `MatchLinq_Using_DisposeCalled`
  - **Depends:** P1C complete

#### P2-D — Short-circuit (blocked on Q4)

- [ ] **P2-040** **blocked — Q4** If VM fix: in `EmitBinaryOp`, emit `Expression.AndAlso`/`OrElse` for `BinaryOperator.And`/`Or` when operands are boolean logic (not bitwise).
  - **Files:** `ProgramCompiler.cs`
  - **Maps:** K-042
  - **Alt:** expansion lowers `&&`/`||` to `CondGoto` chain — document in ADR

- [ ] **P2-041** `MatchLinq_And_ShortCircuit_SideEffect` — second operand not evaluated when first false
  - **Depends:** P2-040

#### P2-E — Oracle matrix documentation

- [ ] **P2-050** Add "Backend oracle matrix" table to `Poly/Interpretation/Vm/README.md`: per feature — VM / Linq / C# / cross-validated / VM-only.
  - **Maps:** K-043, K-049, §4.11

- [ ] **P2-051** Document 8 VM-only features as intentional (no Linq oracle): bitwise, shifts, NewArray, PopCount, StridedSetBits.
  - **blocked — Q10** if wiring Linq instead

#### P2-F — Type promotion / DCE (lower priority)

- [ ] **P2-060** Shared type-promotion utility or analysis pass output consumed by VM — **optional**, per §4.13.5 Phase 2.
  - **Maps:** K-025

- [ ] **P2-061** DCE cross-validation test: `if(false) sideEffect()` — both engines skip dead branch.
  - **Maps:** K-025, §4.13.5 Phase 3

**Phase 2 exit checklist:**
- [ ] P2-001–P2-003 harness done — P2-001/002 ✅; P2-003 open
- [ ] ≥8 new MatchLinq tests (P2-010–P2-019 minimum) — 2/8 (P2-010, P2-012)
- [ ] P2-030–P2-033 after Phase 1
- [ ] C-016, C-026 updated in review

---

### Phase 3 — VM correctness hardening (`P3-`)

#### P3-A — Nested function calls (ring save)

- [x] **P3-001** Design fix for `CtxPushRegisters`/`CtxPopRegisters` flat overwrite — options:
  - (a) `Registers` as stack of save areas
  - (b) save area indexed by call depth on `VmState`
  - (c) document nested calls unsupported + runtime guard
  - **Files:** `ProgramCompiler.cs` 549–563, `VmState.cs`
  - **Maps:** C-022, K-032
  - **Done:** chose **(b)** — `SavedSp` offsets ring save area per nested call (`03d9985`)

- [x] **P3-002** Implement chosen design from P3-001.
  - **Done:** `CtxPushRegisters`/`CtxPopRegisters` keyed by `ctx.SavedSp` (`ProgramCompiler.cs`)

- [ ] **P3-003** Test `NestedLambda_CallPreservesOuterRing` — outer calls inner `Func<long,long>`; outer locals intact after return.
  - **Files:** new test in `VmCorrectnessTests.cs` or `ClosureVmTests.cs`

#### P3-B — Ring verification

- [ ] **P3-010** Add `#if DEBUG` method `VerifyRingDepths(primitives, ringDepths, branchTargets)` after `ComputePrimitiveRingDepths`.
  - Assert all predecessors agree at each Phi/branch target (K-034)
  - **Files:** `ProgramCompiler.cs`
  - **Maps:** C-014, K-034
  - **In progress:** DEBUG stub validates Goto/CondGoto target PCs only — depth convergence (K-034) not yet asserted (uncommitted)

- [ ] **P3-011** Call verifier from `CompilePrimitives` in DEBUG builds only.
  - **In progress:** `#if DEBUG VerifyRingDepths(...)` call added — uncommitted; full K-034 checks remain

- [x] **P3-012** Remove `KNOWN BUG` comment from `Fuzz_Phi_NestedConditional_DifferentRingDepths`; add "fixed by ring-based BuildTargetDepth".
  - **Files:** `VmCorrectnessTests.cs` ~670
  - **Maps:** C-015
  - **Done:** comment updated; test expects 3L (`03d9985`)

#### P3-C — Closures / upvalues

- [ ] **P3-020** Test `Closure_LoadUpvalue_ReadsCapturedLocal` — outer `let x = 42` in lambda `() => x`.
- [ ] **P3-021** Test `Closure_StoreUpvalue_WritesCapturedLocal`
- [ ] **P3-022** Test `Closure_MultipleUpvalues`
- [ ] **P3-023** `MatchLinq_Lambda_WithCapture` (after P2-001)
  - Full pipeline `Interpreter.Compile` + `Execute`
  - **Maps:** K-058

#### P3-D — TypeIs VM path

- [x] **P3-030** Rename `Expand_TypeIs_StringRefType` → `Expand_TypeIs_WithoutAnalysis_FailsClosed`.
  - **Files:** `Poly.Tests/Interpretation/PrimitiveExpandTests.cs` ~96
  - **Maps:** C-011
  - **Done:** uncommitted

- [ ] **P3-031** Test `TypeIs_HeapRef_Match` — string on heap, `is string` → true through VM.
- [ ] **P3-032** Test `TypeIs_HeapRef_Mismatch` → false
- [ ] **P3-033** Test `TypeIs_HeapRef_Null` → false
- [x] **P3-034** Test `TypeIs_Scalar_StaticMatch` — full pipeline, `StaticTypeIsMatch` path
  - **Maps:** K-015
  - **Done:** `TypeIsVmTests.cs` — 3 scalar tests (string/int/null constants)

#### P3-E — InterpretResult ABI

- [x] **P3-040** Add `InterpretResultAbiTests.cs`:
  - `BlockRootedScalar_ReturnsInt` (may exist — extend)
  - `HeapRef_ReturnsDereferencedObject`
  - `Void_ReturnsDefault`
  - Programs use `exec.Result` / `GetValue<T>()`, not `RawValue`
  - **Maps:** K-059, INT-002
  - **Partial:** 3/4 scenarios (`ScalarReturn`, `BoolReturn`, `HeapStringReturn`); `Void_ReturnsDefault` still open

- [ ] **P3-041** Document in `Vm/README.md`: `RawValue` for low-level tests only; production uses `InterpretResult`.

#### P3-F — PolicyEvaluator

- [x] **P3-050** Replace `Debug.Assert(result == result2)` with `if (result != result2) throw new InvalidOperationException(...)` or structured diagnostic.
  - **Files:** `Poly/DomainModeling/Lowering/PolicyEvaluator.cs` ~62
  - **Maps:** C-021
  - **Done:** `Evaluate<TEntity>` throws `InvalidOperationException` on mismatch — uncommitted

#### P3-G — Expansion infrastructure

- [x] **P3-060** Wrap `ExpansionPass` depth increment in try/finally (or `IDisposable` guard) so `state.Depth` restores on `ToPrimitives` exception.
  - **Files:** `Poly/Interpretation/Analysis/ExpansionPass.cs`
  - **Maps:** K-060
  - **Done:** try/finally around depth increment — uncommitted

- [x] **P3-061** Replace `TryResolveSlotByNodeId` manual iteration with `_slots.TryGetValue(nodeId, out slot)`.
  - **Files:** `Poly/Syntax/Primitives/ExpansionEnvironment.cs`
  - **Maps:** K-061
  - **Done:** `03d9985`

#### P3-H — Ring depth limit

- [ ] **P3-070** Add test exercising ring depth >32 (INT-006 spill/overflow path).
  - **Files:** new test in `VmCorrectnessTests.cs`
  - **Maps:** INT-006, §4.22.2
  - **May expose bug** — fix in same task if fails

**Phase 3 exit:** P3-002+003, P3-010, P3-012, P3-020+, P3-031+, P3-050, P3-060, P3-061 done.
  - **Progress:** P3-002/012/030/034/050/060/061 ✅; P3-003/010/020/031+ open

---

### Phase 4 — Portable IR (`P4-`) — INT-019

**Prerequisites:** Q5, Q6 answered.

#### P4-A — CallSiteCompiler decision

- [ ] **P4-001** **blocked — Q5** Decision record: delete `CallSiteCompiler` OR keep for deserialization with ring ABI bridge.
  - **Files:** `Poly/Interpretation/Vm/CallSiteCompiler.cs`, `docs/decisions/2026-06-08-bytecode-serialization.md`
  - **Maps:** C-013, C-006, K-020

- [ ] **P4-002** If delete: remove `CallSiteCompiler.cs`; grep docs/tests; amend bytecode-serialization ADR to `EmitCallExternalDirect` only.
  - **Depends:** P4-001 = delete

- [ ] **P4-003** If keep: adapt to emit ring-compatible code OR document two-phase load (deserialize → recompile via ProgramCompiler).
  - **Depends:** P4-001 = keep

#### P4-B — Catalog-only emission

- [ ] **P4-010** Audit all `CallExternal` creation sites: `Invoke.ToPrimitives`, `Member.ToPrimitives`, `New.ToPrimitives` — verify `SiteIndex` always set when catalog entry exists.
  - **Files:** `Poly/Syntax/Nodes/Invoke.cs`, `Member.cs`, `New.cs`

- [ ] **P4-011** In `EmitCallExternalDirect`: when `SiteIndex` present, resolve target **only** from `VmProgram.CallSites`; ignore embedded `MethodBase` at runtime.
  - **Files:** `ProgramCompiler.cs`
  - **Maps:** K-019

- [ ] **P4-012** Add test: compile with catalog only (strip `MethodBase` from serialized form) — **blocked** until serializer exists.

- [ ] **P4-013** **blocked — Q6** Make `CallExternal.Target` optional or remove from primitive record; compiler requires `SiteIndex` for external calls.
  - **Maps:** K-011, INT-019

#### P4-C — TypeCheck portability

- [ ] **P4-020** Replace `TypeCheck.TargetType` (`System.Type`) with assembly-qualified type name string or stable type id from analysis.
  - **Files:** `Poly/Syntax/Primitives/TypeCheck.cs`, `ProgramCompiler.EmitTypeCheckOp`
  - **Maps:** K-016

- [ ] **P4-021** Test round-trip: TypeIs on heap ref after deserialize.

#### P4-D — Serializer MVP

- [ ] **P4-030** Define wire format (no `BinaryFormatter`): version header + primitive tag stream + catalog table + optional `ExceptionRegionTable` + `RootValueKind`.
  - **Files:** new `Poly/Interpretation/Vm/BytecodeSerializer.cs` (or `Poly/Interpretation/Serialization/`)
  - **Maps:** INT-019, K-040

- [ ] **P4-031** Implement `Serialize(VmProgram, PrimitiveNode[], AnalysisResult?) → byte[]`.

- [ ] **P4-032** Implement `Deserialize(byte[]) → (VmProgram, PrimitiveNode[], CallSiteCatalog)`.

- [ ] **P4-033** Deserialize path: resolve call sites via `TypeAndMemberResolver` or embedded identity strings → `MethodBase` at load time in **fresh process**.

- [ ] **P4-034** Integration test `BytecodeSerializer_RoundTrip_ExecuteSameResult` — analyze → compile → serialize → deserialize → execute; compare to original.
  - **Maps:** INT-019 acceptance

- [ ] **P4-035** Include `ExceptionRegionTable` in serialized payload (from P1C).
  - **Depends:** P1C-002

#### P4-E — Catalog gaps

- [ ] **P4-040** **blocked — Q7** Either extend `ProcessMember` for `ClrMethod` standalone `Member` nodes OR document omission in `CallSiteCatalogPass` + ADR.
  - **Files:** `CallSiteCatalogPass.cs` 122–135
  - **Maps:** C-008

**Phase 4 exit:** P4-002 or P4-003, P4-011, P4-030–P4-034, INT-019 `done`.

---

### Phase 5 — Domain → VM bridge (`P5-`)

**Note:** Not urgent unless you reprioritize (Q8).

- [ ] **P5-001** Add "transitional dual-path" section to domain modeling docs or amend domain-lowering-boundary ADR: V2 = full lowering; V3 = expressions only (1/14).
  - **Maps:** C-019, K-029, K-030

- [ ] **P5-002** Copy V3 lowering 14-file plan status into `docs/plans/v2-to-v3/workstreams/` with checkboxes (1 done: `DomainExpressionLoweringPass`).
  - **Maps:** K-036

- [ ] **P5-003** **blocked — Q8** Decision record: action bodies — C#-only forever vs future VM path.
  - **Maps:** K-031, C-020, architecture review Q12

- [ ] **P5-004** When next V3 lowering file lands: add matching test in `DomainExpressionVmExecutionTests.cs`.
  - **Depends:** V3 workstream priority

- [ ] **P5-005** When V3 effect lowering exists: end-to-end VM test for `Assign`/`Publish`/`Transition` effect — **maps C-020**.

- [ ] **P5-006** Decouple `PolicyEvaluator.CompileVMPredicate<T>` from `TypeReference.To<T>()` — symbolic entity parameter + late CLR bind.
  - **Files:** `PolicyEvaluator.cs` 30–42
  - **Maps:** K-037
  - **Can defer** until INT-019 needs portability for domain policies

---

### Phase 6 — Hygiene and deferred (`P6-`)

#### P6-A — Dead code removal (independent small PRs)

- [ ] **P6-001** Delete `FunctionEntry.cs` if still zero usages.
  - **Maps:** K-057

- [ ] **P6-002** Delete `Closure.cs` OR refactor `EmitAllocClosure` to use it — grep `new Closure(` first.
  - **Maps:** K-054

- [x] **P6-003** Remove `CSharpGenerator.WriteTestTopLevelStatement` (~line 46).
  - **Maps:** K-053
  - **Done:** removed in `03d9985`; zero grep hits

- [ ] **P6-004** Remove `NodeExtensions.Null`, `.True`, `.False`, `.Wrap()`.
  - **Files:** `Poly/Syntax/NodeExtensions.cs`
  - **Maps:** K-062
  - **Open:** factories refactored to `readonly` but still present (`TypeIsVmTests` uses `Null`)

- [ ] **P6-005** Remove `PendingFunction.CapturedInfo` field if still unread; fix tuple naming doc if needed.
  - **Maps:** K-056

#### P6-B — Visualization and docs

- [ ] **P6-010** Add `GetChildren` cases in `MermaidAstGenerator` for `TryCatchFinally`, `SwitchStatement`, `UsingStatement`.
  - **Files:** `Poly/Interpretation/Mermaid/MermaidAstGenerator.cs`
  - **Maps:** K-063

- [x] **P6-011** Fix Phi `StackEffect` in `Poly/Syntax/Primitives/README.md` → `(0,0)`.
  - **Maps:** K-033
  - **Done:** `03d9985`

- [ ] **P6-012** Create `Poly/Interpretation/CSharp/README.md` — input contract, ToString fallback list, production entry via `DomainTools`.
  - **Maps:** K-052

- [ ] **P6-013** Document ring-vs-ValueStack ghost model in `Poly/Interpretation/Vm/README.md`.
  - **Maps:** K-035

#### P6-C — Heap API

- [ ] **P6-020** Add `Heap.Free(int handle)` and optionally `Take(int handle)`.
  - **Files:** `Poly/Interpretation/Vm/Heap.cs`
  - **Maps:** K-055

- [ ] **P6-021** Tests for Free/Take; document null-as-deleted semantics.

#### P6-D — Tracker items (as scheduled)

- [ ] **P6-030** **INT-002** — complete remaining `InterpretResult` edge cases per tracker (coordinate with P3-040).
- [ ] **P6-031** **INT-028** — per tracker spec.
- [ ] **P6-032** **INT-006** — propagate `MaxActiveLocalsDepth` from ring analysis (coordinate P3-070).
- [ ] **P6-033** **INT-007** — **blocked — Q9** incremental analysis for expressions: tests first or document domain-only.
  - **Maps:** K-050

#### P6-E — Analysis pipeline tooling

- [ ] **P6-040** Add `RequiredMetadata` or `[DependsOn]` to `INodeAnalyzer`; validate order at `AnalyzerBuilder.Build()`.
  - **Maps:** K-051

- [ ] **P6-041** Mirror §4.14.2 dependency table in `Analysis/README.md` with explicit edges.

#### P6-F — Deferred until first consumer

- [ ] **P6-050** INT-008 peephole optimizer — no implementation until consumer identified.
- [ ] **P6-051** Sandboxing `PermissionSet` — deferred (K-012).
- [ ] **P6-052** Breakpoint `BreakpointPCs` — optional quick win per Q2.
- [ ] **P6-053** INT-009 SSA slots / `CompileModule` — blocked on first consumer (K-003).

#### P6-G — Open ANA-FIX tracker items (still relevant)

- [ ] **P6-060** **ANA-FIX-008** — CFG-unreachable catch regions: filter or document in expansion.
  - **Status:** `blocked` in tracker

- [ ] **P6-061** **ANA-FIX-009** — stable `CatchTypeId` instead of `GetHashCode()`.
  - **Status:** `blocked`

- [ ] **P6-062** **ANA-FIX-010** — EH test suite (may be satisfied by P1C test matrix — close when covered).

- [ ] **P6-063** **ANA-FIX-013** — `CallSiteEntry` overload collision identity string.

---

## Open decisions for the owner

| # | Question | Default | Answer | Unblocks tasks |
|---|----------|---------|--------|----------------|
| **Q1** | Module/BasicBlock: amend ADR vs implement? | Amend to partial | **Defer until real need arises** — avoid implementing speculative abstraction. Keep flat `CompilePrimitives` as-is. | P0-025, INT-021 → drop both |
| **Q2** | Unimplemented ADRs: defer all vs breakpoint `BreakpointPCs` now? | Defer + annotate | **Defer all.** Bytecode serialization not important. Peephole useful later, not now. Sandboxing after current problem set. | P0-026, P6-052 |
| **Q3** | Confirm EH Strategy B? | Yes | **Yes** — ADR accepted (`2026-07-05-vm-exception-handling-strategy-b.md`). | P1B-001 ✅, P1C in progress |
| **Q4** | Short-circuit: VM `AndAlso`/`OrElse` vs expansion branches? | VM emit | _Open_ | P2-040 |
| **Q5** | Delete `CallSiteCompiler`? | Delete | _Open_ | P4-001–003 |
| **Q6** | Drop `MethodBase` from IR when? | After serializer sketch | _Open_ | P4-013 |
| **Q7** | Catalog standalone `Member`→method? | Document omit | _Open_ | P4-040 |
| **Q8** | Domain action bodies through VM ever? | C#-only for now | _Open_ | P5-003, C-020 |
| **Q9** | Incremental analysis on expression ASTs? | Domain-only | _Open_ | P6-033, K-050 |
| **Q10** | Wire 8 VM-only ops into Linq for oracle? | VM-only doc | _Open_ | P2-051 |

**It is okay to answer partially** — e.g. "Q3 yes, Q5 delete, Q8 defer" is enough to start Phase 1 and 4.

---

## Suggested execution order

| Sprint | Tasks | Deliverable | Status |
|--------|-------|-------------|--------|
| **S1** | P0-001–P0-023, P1A-001–P1A-003 | Docs synced; throw wired + test | ✅ **mostly done** — P0-003/010–012/021/024–026 open |
| **S2** | P1B-001, P1C-001–P1C-012 | EH ADR; region table; handlers compile | ✅ **done** — P1B-003 open |
| **S3** | P1C-020–P1C-042, P1C-062–P1C-064 | Full EH; INT-018 done | **in progress** — try/catch MVP; finally/using/nested remain |
| **S4** | P2-001–P2-019, P2-030–P2-033 | Parameterized MatchLinq + EH parity | **in progress** — harness + 2 MatchLinq tests (uncommitted) |
| **S5** | P3-001–P3-034, P3-050, P3-060–P3-061 | Ring fix; closures; TypeIs | **in progress** — ring save, scalar TypeIs, ABI partial |
| **S6** | P4-001–P4-034 | INT-019 MVP | not started |
| **ongoing** | P6-* between sprints | Hygiene | **in progress** — P6-003, P6-011 done |

---

## Register closure checklist

| Register | Close when task(s) done | Status |
|----------|-------------------------|--------|
| C-004 | P0-002 | ✅ closed |
| C-002, C-012 | P1C-063 (not P1A alone) | ✅ closed in review; tracker pending P1C-063 |
| C-017 | P1C-024 | ✅ closed in review |
| C-018, C-023 | P1C-062 | open |
| C-010 | P0-020 | ✅ closed |
| C-023 (ADR) | P1C-061 | open |
| C-025, K-041 | P0-022 | ✅ closed |
| C-005 | P0-023 | ✅ closed |
| C-009 | P0-012 | open |
| C-001 | P0-025 or INT-021 | open (Q1=defer) |
| C-016, C-026 | P2 exit | open |
| C-022, K-032 | P3-003 | partial — ring save done; nested-call test open |
| C-014, C-015 | P3-010, P3-012 | partial — C-015 narrowed (P3-012 ✅); C-014 awaits full verifier |
| C-021 | P3-050 | narrowed — PolicyEvaluator throws on mismatch |
| C-011, K-015 | P3-030–P3-034 | partial — scalar TypeIs done; heap-ref (P3-031+) open |
| C-013, C-006 | P4-001–002 | open |
| C-008 | P4-040 | open |
| C-019, C-020 | P5-001, P5-003, P5-005 | open |
| K-046 | P0-004 | ✅ closed |
| K-018 | P1A+P1C try/catch tests | narrowed in review |
| K-058 | P3-020–P3-023 | open — `ClosureVmTests` has no-capture/param only |
| K-059 | P3-040 | partial — 3 ABI tests; Void edge case open |
| K-060 | P3-060 | narrowed |
| K-061 | P3-061 | narrowed |

---

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| EH Strategy B >10 days | Timebox P1C-020–022 dispatch MVP; nested EH (P1C-040) can follow |
| P1C-004 marker/PC mapping wrong | P1C-005 shape test before execution tests |
| MatchLinq explosion | P2-010–019 breadth-first only; no cartesian fuzz |
| INT-019 scope creep | P4-030 MVP format only; metadata bundle v2 later |
| Wrong task order | Working agreement + Q1–Q10 |

---

## Related links

- [Architecture review](../interpretation-system-architecture-review.md)
- [Issue tracker](interpretation-system-issues.md)
- [Analysis README](../../Poly/Interpretation/Analysis/README.md)
- [VM README](../../Poly/Interpretation/Vm/README.md)

---

*Task count: 154 total · **46 done** · **16 in progress** (P1C + P2/P3 partial) · **108 open** — last synced 2026-07-06.*