# Follow-ups — declare-init + kinds local review — 2026-08-26

Review: [`2026-08-26-interpretation-declare-init-local-review.md`](./2026-08-26-interpretation-declare-init-local-review.md)

Owning stream: Interpretation (printer/debug) plus DomainModeling lowering only where flatten/fusion must stay honest.

## Prior follow-ups

From [`2026-08-25-interpretation-capture-analysis-followups.md`](./2026-08-25-interpretation-capture-analysis-followups.md) and [`2026-08-25-interpretation-invariant-followups.md`](./2026-08-25-interpretation-invariant-followups.md):

- F1–F9 capture/kinds siblings — **still closed** (current `LambdaCaptureCollector.CollectDeclaredLocals` copies nested declared; `BindLambdaArguments` on stored invoke; `CheckInvokeTarget` rejects non-closure `Variable`).
- Nested `FrameOffset` 0 — **overclaimed closed**. Monotonic `_nextFrameSlot` landed; debug-hook span still uses live `scope.Count` (this review F1).
- Declare-init as `Assignment` — **landed** for VM/LINQ/analysis/same-node construction; printer fusion siblings remain (this review F2–F3). Sticky-`Initializer` wording in the invariant follow-ups file is stale.
- F6 (declare by identity not name) — **still holds** via `Block.Variables` + `InnerDeclareInit_SameNameAsOuter_IsOwnLocal`; the Initializer registration path is gone.

## Closed this change

- [x] **F1** — Debug-hook span uses `FrameSlotHighWater` (`_nextFrameSlot`). Sibling test: `DebugHook_SequentialInnerBlocks_SpanCoversHighWaterSlot`.
- [x] **F2 (declare-only type)** — Unfused locals print `var x = default(T);`. Nested first-write is declare-only + `x = e`.
- [x] **F3** — Fusion only when the first mention of a block-declared dest is that direct-child `Assignment`. Loop bodies use `WriteBracedBody`. Tests: `Generate_WhileThenDirectAssign_DoesNotFuseVarInWhile`, `Generate_ForHeaderOfBlockLocal_DoesNotStealVarFusion`.
- [x] **F4** — Non-closure assignment removes `StoredLambdaMetadata`. Test: `Invoke_AfterReassignFromLambdaToInt_AnalysisErrorAndCompileRejects`.
- [x] **F5** — Process: slot-layout consumers include emit, post-execute `GetLocals(state)`, and **hook span**; F1 test forces the sparse sequential-inner sibling.
- [x] **N1** — `CompileVariable` is lookup-only / fail-closed.
- [x] **N2** — `Variable` comment: user writes are `Assignment`; foreach writes the loop variable.

## Closed this change (continued)

- [x] **Printer infer T** — Declare-only without analysis uses the first assignment RHS (`Constant` / `New` / `NewArray` / `TypeCast` / `TypeAs`) so `var x = default(long)` matches VM 0 for scalar inits. Tests: `Generate_NestedFirstAssign_WithoutAnalysis_InfersConstantType`; while/for declare-only now `default(long)`.

## Open

None. Never-assigned locals with no inferable RHS still print `default(object)` (VM slot 0). That tree is declare-without-write; C# `object` vs ABI `long` only if the local is read untyped.
