# Interpretation capture + analysis kinds — 2026-08-25

- **Target**: local uncommitted (vs `HEAD` `c537cd7e`; untracked `ClosureCaptureTests.cs`, `LambdaCaptureCollector.cs`)
- **Mode**: multi (Pass A implementer-context + Pass B split-context subagent)
- **Issue counts**: 3 bugs, 4 suggestions, 1 nit
- **Verdict**: not ship until F1–F3 closed (valid programs compile-reject or unwrap the wrong ABI word)
- **Process notes**: `[x]` on follow-ups from a single sibling (root `Block`, immediate `Invoke(Lambda)`, nested lambda with no inner locals). Recurring class: last-node / last-stmt used as “the value” while `Return` and nested scopes disagree.

## Summary

Uncommitted work records lambda free bindings and stored-lambda metadata, types `Invoke(fn)` from the body, fail-closes some illegal `Invoke` shapes, and makes `InterpretResult` honor `Void` vs leftover stack. Tests are strong on the siblings they construct (late-bind matrix, sticky initializer, root-block `Return`). They do not force nested-lambda *locals*, lambda-body `Return`+void-tail, or stored higher-order `BindLambdaArguments`. Pass B independently found the same HOF bind hole and a nested-capture collector bug; merge keeps the stronger severity.

Oracle: Interpretation suite was green in this session (726 Interpretation / 2395 full) — green does not cover the untested siblings below.

## Issues

### Issue 1 -- Severity: bug
- File: Poly/Interpretation/Analysis/Semantics/LambdaCaptureCollector.cs:72
- Description: Outer `Collect` walks nested lambda bodies with the **outer** `declared` set. `CollectDeclaredLocals` returns immediately on `Lambda` (lines 45–46), so nested `Block.Variables`, declare-init statements, and `ForEachLoop.LoopVariable` are not marked declared while those uses are still collected as the outer lambda’s free bindings. `Assignment.Children` is `[Value, Destination]` (`Poly/Ast/Nodes/Assignment.cs:11`); `Block.Children` is `[..Variables, ..Nodes]` (`Poly/Ast/Nodes/Block.cs:60`). Interpreter.Compile always attaches `LambdaCaptureMetadata`, so emit (`DirectVmAbiEmitter.Statements.cs:515-521`) never uses the residual scan. `EmitLoadOrCreateUpvalueCell` (`DirectVmAbiEmitter.Statements.cs:490-493`) then throws `captured variable '…' has no upvalue cell` for a **valid** nested local. Sibling `FindBodyCapturesRecursive` (`DirectVmAbiEmitter.Statements.cs:558-563`) is the same walk. `FindCaptures` (ctx `TryGetVariable`) would not have captured nested locals and is now unreferenced. Tests `NestedStoredClosure_*` only close over a grandparent var with no inner locals.
- Suggestion: On nested `Lambda`, copy `declared` and run `CollectDeclaredLocals(nested.Body)` before recursing (one shared helper for analysis + emit fallback). Compile+execute a nested lambda with its own `Block` locals (and foreach loop var).
- Status: open

### Issue 2 -- Severity: bug
- File: Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs:256
- Description: `ClassifyBlock` uses `FindValuedReturnKind` when the last node is void (tested only as a **root** `Block`). `ClassifyInvoke` peels with `BodyValue` (last stmt of a `Block`) and `PropagateChild`s that. `Invoke(Lambda(Block([If(true, Return(7L)), Comment])))`: the `Block` would be StackScalar, but `BodyValue` is `Comment` → Void. `ClassifyInvoke` prefers `ClassifyFromResolvedType` first; Comment has no type → Unknown → Void. `InterpretResult` (`Interpreter.cs:166-167`) then returns Void even though `EmitReturn` wrote 7. Siblings that still use last-node, not the block/`Return` rule: `LambdaReturnTypeAnalyzer.ResolveBodyType` (`LambdaReturnTypeAnalyzer.cs:29-35`), `NoteProducedLambda` (`TypeAndMemberResolutionPass.cs:348-349`), `ResolveBlockType` last node (`TypeAndMemberResolutionPass.cs:323-324`).
- Suggestion: `ClassifyInvoke` should propagate the body **node** (the `Block`) so `ClassifyBlock` applies. Align `ResolveBodyType` / `NoteProducedLambda`. Test `Invoke(Lambda(Block([If(true, Return(7L)), Comment])))` analyze + execute.
- Status: open

### Issue 3 -- Severity: bug
- File: Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs:192
- Description: `BindLambdaArguments` (`TypeAndMemberResolutionPass.cs:335-345`) runs only for `invoke.Delegate is Lambda`. The stored sibling (`Delegate is Variable or Parameter`, lines 197-204) reads `StoredLambdaMetadata` and returns the body type but does **not** stamp callee parameters from this invoke’s arguments. Immediate `Invoke(apply, add1)` is tested. `Assignment(applyVar, apply); Invoke(applyVar, add1)` never binds `f`. Inner `Invoke(f)` stays Unknown; `InterpretResult` Unknown/HeapRef can treat ABI `1` as heap handle 1 — the unwrap this change fixed for **direct** stored lambdas returning bool.
- Suggestion: `BindLambdaArguments(context, stored.Lambda, invoke.Arguments)` on the stored Variable/Parameter branch. Stored-HOF execute tests, including a callee that returns `bool`.
- Status: open

### Issue 4 -- Severity: suggestion
- File: Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs:133
- Description: `CheckInvokeTarget` accepts any `Member` / `Lambda` / `Variable` / `Parameter`. `Invoke` of a `Variable` holding a `long` compiles; runtime `InvokeHeapClosure` (`DirectVmAbiEmitter.Invoke.cs:359-360`) throws `invoke target is not a closure`. New tests only cover `Constant`, `Invoke(Invoke)`, and `IndexAccess`. Follow-ups `[x]` “illegal Invoke targets fail at analysis” is node-shape only, not “is a stored closure.”
- Suggestion: With analysis present, reject `Variable`/`Parameter` targets that lack `StoredLambdaMetadata` (and are not a resolved method). Test `Invoke` of an int variable: analysis error + compile reject.
- Status: open

### Issue 5 -- Severity: suggestion
- File: Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs:333
- Description: `FindValuedReturnKind` runs only when the last node’s kind is Void. `Block([If(true, Return(7L)), Constant("miss")])` classifies HeapRef (dead tail) while emit’s `Goto(ExitLabel)` leaves 7; `InterpretResult` unwraps 7 as a handle. The scan is a tree walk, not CFG, despite VR depending on `ControlFlowAnalysisPass`. Mixed-kind valued Returns: first tree-order Return wins when last is void.
- Suggestion: Dominating `Return` should win over a dead tail of any kind (use CFG if that bag exists). Test mixed-kind tail and `If(false, Return(value))` + void fall-through.
- Status: open

### Issue 6 -- Severity: suggestion
- File: Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:81
- Description: Sticky-init capture is specified as same-node free use (`EmitVariable` capture-before-initializer, `DirectVmAbiEmitter.Expressions.cs:656-661`). ScopeValidator uses **name** in scope (`NameIsInScope`) to decide declare vs reference. A different `Variable` node with the same name and an `Initializer` inside a lambda is treated as a reference to the outer binding, while `CollectDeclaredLocals` adds that declare-init statement as a lambda-local by node identity.
- Suggestion: Gate the “reference, not declare” path on node identity with the in-scope declaration, not name alone. Test inner declare-init with the same name as an outer variable.
- Status: open

### Issue 7 -- Severity: suggestion
- File: docs/agent/reviews/2026-08-25-interpretation-invariant-followups.md:22
- Description: The `[x]` line claims invoke body kinds, illegal invoke-at-analysis, void-ended block+Return, and lambda-arg `StoredLambdaMetadata` as done. Issues 1–5 show those contracts hold only on the tested sibling.
- Suggestion: Reopen those bullets until F1–F3 (and F4 if claimed as analysis-complete) have tests. Do not `[x]` from a single sibling.
- Status: open

### Issue 8 -- Severity: nit
- File: Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:580
- Description: `FindCaptures` / `FindCapturesRecursive` have no call sites. `VariableAnalysisMetadata.CapturedVariables` / `CapturedParameters` are written and never read (`NeedsCell` uses emit’s `CapturedBindings`). Two copies of `CollectDeclaredLocals` will drift; the fallback is unreachable on `Interpreter.Compile` because empty `LambdaCaptureMetadata` is still non-null.
- Suggestion: Delete dead `FindCaptures*`; consume or drop the unused hash sets; share one capture helper so a fix for Issue 1 cannot land on only one copy.
- Status: open

## Checklist

- [x] Diff collected; scope drift noted (Interpretation + tests + CORE/README/follow-ups only)
- [x] Stance: adversarial; split-context Pass B applied
- [x] Producer/consumer keys traced (`LambdaCaptureMetadata`, `StoredLambdaMetadata`)
- [x] Sibling-path check (analysis vs emit fallback; Invoke(Lambda) vs Invoke(Variable); root Block vs lambda body)
- [x] Fail-loud reachability (Issue 1 throw on valid nested locals; Issue 4 throw only at runtime for int Variable)
- [x] Invariant comments checked (sticky initializer; illegal Invoke)
- [x] Counts from current greps (`FindCaptures` defs only; `CapturedVariables` write-only)
- [x] Oracles not weakened (tests added, none deleted in this uncommitted set)
- [x] Review + follow-ups written under `docs/`
