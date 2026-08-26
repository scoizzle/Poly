# Follow-ups — capture + analysis kinds local review — 2026-08-25

Review: [`2026-08-25-interpretation-capture-analysis-local-review.md`](./2026-08-25-interpretation-capture-analysis-local-review.md)

Owning stream: Interpretation only.

## Prior follow-ups ([`2026-08-25-interpretation-invariant-followups.md`](./2026-08-25-interpretation-invariant-followups.md))

- Nested frame-slot aliasing — **closed** (monotonic `_nextFrameSlot`).
- Declare-init as `Assignment` — **closed**: `Variable` is binding only; declare on `Block.Variables` / foreach; write is `Assignment`; C# `var x = e` is printer fusion.
- `[x]` invoke body kinds / illegal Invoke-at-analysis / void-ended Return / lambda-arg bind — **overclaimed**; reopen until F1–F4 below have tests (Issue 7).

## Closed this change

Siblings named per F9.

- [x] **F1** — Nested `CollectRecursive` copies `declared` and runs `CollectDeclaredLocals(nested.Body)` before walking a nested `Lambda`. Emit uses `LambdaCaptureCollector.Collect` (no second walker). Siblings: nested with inner `Block.Variables` (`NestedLambda_OwnBlockLocals_NotOuterCaptures`); nested foreach loop var (`NestedLambda_OwnForeachLocal_NotOuterCapture`); nested without inner locals (existing `NestedStoredClosure_*`).

- [x] **F2** — `ClassifyInvoke` propagates the body **node** (so `ClassifyBlock` applies). `ResolveBodyType` / `NoteProducedLambda` / `ResolveYieldType` use `YieldNode` (last non-void, else dominating `Return`). Siblings: root `Block` + void tail (`Block_EarlyReturn_HasReturnKindWhenLastIsVoid`); `Invoke(Lambda)` body (`Invoke_LambdaBlock_ReturnThenComment_*`).

- [x] **F3** — `BindLambdaArguments` on stored `Invoke(Variable)` / `Invoke(Parameter)`. Siblings: immediate `Invoke(Lambda, add1)` (`Invoke_ParameterHoldingLambda_CallsThrough`); stored HOF + bool callee (`StoredHof_InvokeVariable_BindsCalleeAndBoolResult`).

- [x] **F4** — Analysis rejects `Variable`/`Parameter` invoke targets without `StoredLambdaMetadata` (and without a resolved method). Siblings: node-shape (`Invoke` of `Constant` / `Invoke` / `IndexAccess`); non-closure `Variable` (`Invoke_OfIntVariable_AnalysisErrorAndCompileRejects`); stored closure `Variable` (existing late-bind tests still compile).

- [x] **F5** — Dominating `Return` (`If(true, Return)`) wins over a dead non-void tail; `If(false, Return)` does not steal void fall-through. Tests: `Block_DominatingReturn_WinsOverDeadNonVoidTail`; `Block_IfFalseReturn_VoidFallthrough`.

- [x] **F6** — Declare-init registers by **node identity** (`VariableDeclarationScope`), not name. Sibling: inner declare-init same name as outer (`InnerDeclareInit_SameNameAsOuter_IsOwnLocal`); same-node sticky capture (existing `Stored_DeclareInitVariable_MutateAfterStore`).

- [x] **F7** — Overclaimed `[x]` bullets reopened in the invariant follow-ups file, then closed here once F1–F4 siblings had tests.

- [x] **F8** — `FindCaptures` / `FindBodyCapturesRecursive` / emit `CollectDeclaredLocals` deleted. `FindBodyCaptures` always uses `LambdaCaptureCollector`. `CapturedVariables` / `CapturedParameters` consumed in `DirectVmAbiEmitter.Emit`.

- [x] **F9** — This list names immediate vs stored, root `Block` vs `Invoke` body, nested with vs without inner locals.

Declare-init as `Assignment` and nested `FrameOffset` 0 are closed.
