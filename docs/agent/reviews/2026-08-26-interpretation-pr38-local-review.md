# Interpretation PR #38 — 2026-08-26

- **Target**: PR #38 (`cleanup/interpretation-u1-u3` vs `origin/master`; 58 files, +2668/−556)
- **Mode**: standard (single adversarial context; split-context Pass B not run — see Process notes)
- **Issue counts**: 1 bug, 3 suggestions, 2 nits
- **Verdict**: **not ship** until F1 (stored-closure arity mismatch) is closed; F2–F3 are suggestions with coverage gaps, not blockers.

## Summary

PR #38 lands the capture-analysis + declare-init + closure-cell work reviewed as local slices on 2026-08-25/26: `Variable` is a binding (declare on `Block.Variables` / foreach; write is `Assignment`), `LambaCaptureCollector` records free bindings, stored closures late-bind via shared heap `long[1]` cells, `Invoke` types from the lambda body, `ValueRepresentation` covers more node kinds, the C# printer fuses `var x = e` only for a direct first assign, the debugger uses the frame high-water span, and platform ABI fixes (ulong bitcast, optional-ctor `new`). The PR closes F1–F9 from the two prior reviews. A full `dotnet build` and the whole suite (2436 passed / 0 failed / 0 skipped) are green — but that green does **not** cover the stored-closure **arity-mismatch** sibling, which I reproduced as a silent wrong result. That is the dominant risk: `Invoke` through a **stored** closure does not fail closed on argument-count mismatch, while the **immediate `Invoke(Lambda)`** path does. Fixing it detects the error at analyze-time (feedback ladder rung 2) rather than at runtime.

Oracle strength: strong on the closed siblings (late-bind matrix, capture+args, debugger high-water, printer fusion). Coverage gaps: no test forces stored-closure arity mismatch; `CheckInvokeTarget` resolved-member path; `CheckVariableAssign` unknown-RHS install; mono slot inference; ulong int-cast.

## Issues

### Issue 1 -- Severity: bug
- File: Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs:326 (`EmitInvokeIndirect`)
- Description: **Stored-closure arity mismatch is not fail-closed.** `EmitInvokeIndirect` (used for `Invoke(Variable)` / `Invoke(Parameter)`) does not check `invoke.Arguments.Length` against the stored lambda's parameter count. Arguments are bound by fixed frame offset (`_fp = callSp + header + max(args.Length, 1)`, `EmitParameter` reads `_slots[_fp - ParamSlotOffset + paramIdx]`), so with 0 args into an arity-1 lambda, `param0` **reads the leftover value in that slot** instead of failing. Empirically reproduced (/tmp probe, then removed): `{ captured=7; fn = Lambda([x], x+1); Invoke(fn) }` (0 args) returns `RawValue=1`; with 1 correct arg it returns `100`. Direct `Invoke(Lambda)` checks arity at `Invoke.cs:182`; the **stored sibling does not** — sibling-path drift after a claimed fail-closed fix. Reachability: legal Syntax; not exercised by the green suite.
- Suggestion: In `EmitInvokeIndirect` (and the indirect function-table path), resolve the stored lambda from `StoredLambdaMetadata` at compile time and reject `Arguments.Length != Parameters.Count` the same way the direct path does (`VM compile rejected: lambda has N parameter(s) but invoke has M argument(s).`). Optionally catch `paramIdx` bounds in `EmitParameter`. Add a test that constructs the illegal arity and asserts `Compile` rejects (not just runtime).
- Status: open
- Evidence: probe with `captured=7`, arity-1, 0 args → `RawValue=1`; control with 1 arg → `RawValue=100`. Oracle green (2436) does not force this sibling.

### Issue 2 -- Severity: suggestion
- File: Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:139-153
- Description: `CheckInvokeTarget` accepts `Variable`/`Parameter` targets when `context.GetResolvedMember(invoke) is not null`. `GetResolvedMember` is populated for invokes whose delegate resolves to a CLR member/method (`TypeAndMemberResolutionPass` `ResolveInvokeType` member branch) — so a `Variable` holding a **method group / delegate** can pass analysis and hit the runtime `InvokeHeapClosure` "invoke target is not a closure" only later. The intended contract (per README/CORE: "Illegal `Invoke` targets fail at analysis") is only enforced on the node-shape siblings, not on the resolved-member sibling. Not empirically confirmed as reachable in this tree (the suite is green) — mark reachability via call graph (a `Variable` used as an invoke delegate with a resolved member) not proven here.
- Suggestion: Decide whether `Invoke(Variable)` should be allowed only for a stored lambda (not a CLR member/delegate), and if so, reject the resolved-member path too, with a test that constructs the tree and asserts analysis error + compile reject.
- Status: open
- Reachability: plausibly unreachable on current valid lowering; needs proof.

### Issue 3 -- Severity: suggestion
- File: Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:155-175
- Description: `CheckVariableAssign` only installs `VariableAssignedTypeMetadata` when the RHS has a non-Unknown, non-Void `ValueRepresentationMetadata`. A first write whose RHS is **untyped** (e.g. an `Invoke` whose callee type is Unknown, a `Member` with no resolved type, an untyped lambda produce) installs nothing, so a second, differently-typed write is **not** rejected — the mixed-assign fail-closed rule silently doesn't apply to the first-write-untyped sibling. The green suite forces the typed-first-write siblings (`Assignment_LongThenString_*` etc.) only.
- Suggestion: When the RHS is Unknown/Void, either record a sentinel "untyped" prior so a later typed write is still checked, or fail closed with a clear message; add a test with a first write whose RHS type is unresolved and a second write to the same variable.
- Status: open

### Issue 4 -- Severity: suggestion
- File: Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs:326, 355-390
- Description: `InvokeHeapClosure` validates upvalue cells but `EmitInvokeIndirect` does not. `Variable` slots holding a **monomorphic** upvalue cell (`long[1]`) are read via `EmitVariable` → `EmitCaptureCellValue`; but if a slot holds a different representation (e.g. a scalar `long` from a non-closure assign), the closure cell check at runtime (`closure[u] is not long[]`) throws — a **runtime** failure, not analyze-time. This is partly deliberate (analysis refuses mixed assignments) but an untyped write or a re-assigned slot that analysis does not catch can produce a runtime throw that the green suite does not force. Not a new behavior vs the deleted `UpvalueCell` wrapper, but the counterpart of Issue 1's "which sibling is covered."
- Suggestion: With `CapturedBindings`/`StoredLambdaMetadata` known at compile time, reject at compile time any `Invoke(Variable)` whose binding is not a stored lambda (Issue 2 subsumes this) — making the runtime `InvokeHeapClosure` "not a closure"/"not a cell" throws unreachable on valid trees. Tests: capture the same variable in two closures and confirm cell sharing; assign a non-lambda to a captured slot and confirm analysis rejects.
- Status: open

### Issue 5 -- Severity: nit
- File: Poly/Interpretation/Vm/DirectVmAbiEmitter.cs:436, DirectVmAbiEmitter.Invoke.cs:710
- Description: `TryValueToLong` and `EmitValueToSlot` convert `ulong` via `unchecked((long)ul)`. That is correct for the ABI bitcast, but `GetValue<ulong>()` returns `unchecked((ulong)l)` — which is fine for values that fit `long` but is a no-op for `ulong.MaxValue` (round-trips bits). The conversion is consistent; the nit is that `EmitValueToSlot` at `Invoke.cs:710` (`ulong ul => unchecked((long)ul)`) duplicates the same constant-fold in `AbiValueTypes`/`VmState.SetArgs` (both changed to `unchecked`). No bug observed (suite covers `ulong.MaxValue`).
- Suggestion: Confirm `GetValue<ulong>()` is only used at a `ulong`-typed root (never mixed into a `long` arithmetic); add one dual-oracle test of `ulong` arithmetic promotion if not present.
- Status: open

### Issue 6 -- Severity: nit
- File: docs/agent/reviews/2026-08-26-interpretation-declare-init-followups.md (Open section) and 2026-08-25-interpretation-invariant-followups.md
- Description: The follow-ups docs claim "none open" for declare-init, but the printer-infer-T residual ("never-assigned locals with no inferable RHS still print `default(object)`") is left untracked in the same doc's "Open" section. It's a known residual that the C# projection can disagree with the VM slot (VM zero-init's `long`). Minor but the "Open: None" is a docs overstatement.
- Suggestion: Either move that residual into a checkable `- [ ]` follow-up (e.g. "fail-closed untyped declare-only or nail the `default(object)` vs 0 semantic") or narrow the "Open: None" claim to "none tracked."
- Status: open

## Checklist

- [x] Diff collected; scope noted (Interpretation + DomainModeling lowering + MinimalApi C# IR + tests/docs; uncommitted local WIP not in PR: Introspection typing, ExpressionTypeAnalyzer)
- [x] Stance: adversarial; single pass; prior reviews re-verified against current source (F1–F9 claimed-closed confirmed on the tested siblings, but Issue 1 exposes a new untested stored-closure arity sibling)
- [x] Producer/consumer keys traced (`LambdaCaptureMetadata`, `StoredLambdaMetadata`, `VariableAssignedTypeMetadata`, `CapturedBindings`)
- [x] Sibling-path check: direct `Invoke(Lambda)` arity-checked vs stored `Invoke(fn)` not; `CheckInvokeTarget` node-shape vs resolved-member; `CheckVariableAssign` typed vs untyped first write
- [x] Fail-loud reachability: Issue 1 empirically confirmed on a legal tree; Issues 2–4 reachability argued honestly (unknowns stated)
- [x] Oracle strength: full suite 2436 passed, 0 skipped; gap = stored-arity sibling + resolved-member + untyped-first-write
- [x] Prior follow-ups dispositioned (F1–F9 still closed; declare-init Open "None" overstated — nit)
- [x] Review + follow-ups written under `docs/`