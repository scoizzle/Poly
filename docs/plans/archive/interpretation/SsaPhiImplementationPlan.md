# Phi Implementation Plan

## Option A — Branch-Aware Ring Depth (NOW)

**Goal:** Eliminate `StoreLocal(t)` / `LoadLocal(t)` in expression-context control-flow merges.

**Mechanism:** The linear ring simulation snapshot-restores at `CondGoto`→`Label(else)` boundaries so both branches start from the same ring depth. Then both branches leave their result at the same depth, and `Phi(0,1)` picks it up without a StoreLocal/LoadLocal round-trip.

**Saves:** 3 µops per expression-context IfStatement/Conditional merge (StoreLocal + Goto + LoadLocal → Phi only).

**Changes:** `ComputePrimitiveRingDepths` + `ComputePrimitiveConsumedPcs` (10 lines), `Phi.StackEffect` → `(0,1)`, `EmitPhi` → single ring assign, `IfStatement`/`Conditional` expansion removes StoreLocal/LoadLocal.

**Risk:** Very low — ring restore only triggers at CondGoto+Goto+Label patterns.

## Option B — ValueSlot Wiring (NEXT)

**Goal:** Explicit dataflow edges on primitives so `Phi` carries real `ValueSlot[]` references.

**Mechanism:** `ExpansionEnvironment` allocates `ValueSlot` per expression result. `BinaryOp`, `PushConstant`, etc. carry `ResultSlot`. `Phi(Incoming)` references the merge candidates by slot. `CompilePrimitives` uses `InputSlots`/`ResultSlot` instead of `consumedPcs` when present.

**Saves:** Nothing directly — wire format only. Enables future optimizations.

**Changes:** ~30 expansion methods get slot wiring. `ProgramCompiler` gets a dataflow-aware emission path alongside the ring path.

## Option C — Phi-Based Dead-Store Elimination (FUTURE)

**Goal:** Remove redundant `StoreLocal(t)` / `LoadLocal(t)` pairs that survive after Option A by analyzing Phi annotations.

**Mechanism:** Post-emission scan: `StoreLocal(t) ; Goto ; ... StoreLocal(t) ; Phi ; LoadLocal(t)` → eliminate the stores+load, let Phi handle the merge.

**Saves:** Same as Option A but as a pure optimization pass instead of at expansion time. Useful for hand-written or third-party primitive sequences.
