# Interpretation declare-init + kinds local review — 2026-08-26

- **Target**: local (uncommitted vs `HEAD`)
- **Mode**: multi (Pass A this session + Pass B subagent `01a03e29`, diff-only)
- **Issue counts**: 1 bug, 3 suggestions, 2 nits
- **Verdict**: not ship until F1 (debug-hook span vs monotonic frame slots) is closed
- **Process notes**: Nested `FrameOffset` and declare-init printer fusion were marked closed in prior follow-ups without sibling tests (hook span / nested first-write / unbraced loop body). Dual-path length (`scope.Count` vs `_nextFrameSlot`) is the same class as the original slot-0 aliasing miss.

## Summary

Uncommitted slice: `Variable` is binding-only (declare on `Block.Variables` / foreach; write is `Assignment`); `Lambda` typed as `Func`/`Action`; frame slots monotonic. VM / LINQ / analysis agree on the same-node happy path; `Assignment_UndeclaredVariable_AnalysisErrorAndCompileRejects` forces the undeclared-write fail-closed sibling. Debugger `CurrentLocalCount` still sums live `scope.Count` while slots are sparse; C# `var x = e` fusion only matches a direct-child `Assignment` identity. Pass B found F1–F3 independently; Pass A confirmed F1 against current `GetLocals` / `CompileStatement` / `WithInterrupt`.

Oracle: 2408 tests green after implementer run; no test steps sequential inner blocks under `DebugHook` spans; printer tests cover only direct assign and never-assigned `object x = default!`.

## Issues

### Issue 1 -- Severity: bug
- File: Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:70
- Description: `DeclareVariable` uses monotonic `_nextFrameSlot` (line 232) so nested blocks do not reuse slot 0. `CurrentLocalCount` still sums **live** `_scopeStack` dictionary counts. `CompileStatement` (`DirectVmAbiEmitter.cs:171`) and `WithInterrupt` (`DirectVmAbiEmitter.Invoke.cs:518`) slice `_slots[_fp .. _fp + CurrentLocalCount)`. After the first inner block pops, a second inner block is declared at slot 2 while live count is 2 (outer slot 0 + inner slot 2). The span covers offsets 0–1. `VmDebugger.GetLocals(program, localsSpan)` (`VmDebugger.cs:259`) treats `FrameOffset >= span.Length` as `0L`. Stepping **inside** the second inner block (hooks emitted while that scope is live) presents 0 for the live inner local. `GetLocals(state)` after execute reads the full slot array, so `VmDebugger_NestedBlocks_FrameOffsetsAreDistinct` cannot see this. Sequential inner `Block`s are valid Syntax.
- Suggestion: Debug span length must be the frame high-water (`_nextFrameSlot`), not live `scope.Count`. Add a `DebugHook` test with two sequential inner `Block`s whose inner `FrameOffset`s are 1 and 2 and assert hook-span locals, not post-execute `GetLocals(state)`.
- Status: open
- Found by: pass B (confirmed pass A)

### Issue 2 -- Severity: suggestion
- File: Poly/Interpretation/CSharp/CSharpGenerator.cs:279
- Description: Printer fusion (`var x = e`) only treats a `Block.Nodes` **direct** `Assignment` whose dest is reference-equal to the `Block.Variables` entry. Otherwise it always emits `object x = default!`, ignoring `_analysisResult`. `Variable`’s comment (“C# `var x = e` is printer fusion of declare plus the first assignment”) is false for first-write inside `If` / loop / `Try`, and unassigned reads disagree across projections: VM zero-inits the long slot (`EmitBlock`); C# `object` null; LINQ `default(T)` / `object`. Definite-assignment analysis records names but does not fail-closed uninitialized reads. Tests: `Generate_BlockWithVariables_DeclaresInsideBlock` (direct assign) and `Generate_BlockWithUnassignedVariable_DeclaresWithoutInit` only. Domain lowering currently emits inits as direct children; export compile oracles passed.
- Suggestion: Fuse using the first assignment in the block tree (or analysis CLR type + DA); fail-closed unassigned reads at analysis if that is the language rule. Tests: nested first-write; use-before-assign; dual-oracle unassigned local.
- Status: open
- Found by: pass B (pass A same-shape VM 0 vs C# null)

### Issue 3 -- Severity: suggestion
- File: Poly/Interpretation/CSharp/CSharpGenerator.cs:381
- Description: `WriteAssignment` fuses `var` in any statement/expression context. `WriteIfBody` braces non-`Block` bodies; `WriteWhileLoop` / `WriteDoWhileLoop` / `WriteForLoop` / `WriteForEachLoop` do not. If dest is in the parent fuse set (a later direct-child `Assignment` exists) and the first `WriteAssignment` is a naked while/foreach body, the printer can emit `while (c) var x = e;`, which is not legal C#. Identity fusion vs top-level name fusion (`_fuseUndeclaredName`) is a dual path; no test forces the loop-body sibling. `WriteForLoop` initializer uses `WriteExpression` → `WriteAssignment` (can steal fuse into `for (var i = 0; …)` which is loop-scoped in C#, not the `Block.Variables` slot).
- Suggestion: Only fuse in a braced statement context (or wrap loop bodies like `WriteIfBody`). Tests: `While(Assignment(x,…))` plus a later direct assign to the same dest; for-header assignment of a block-declared var.
- Status: open
- Found by: pass B

### Issue 4 -- Severity: suggestion
- File: Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs:282
- Description: `ResolveAssignmentType` sets `StoredLambdaMetadata` when the RHS is a `Lambda` (or produces one) and never clears it on a later non-lambda assignment to the same dest. `SyntaxTypeCompatibilityAnalyzer.CheckInvokeTarget` (`SyntaxTypeCompatibilityAnalyzer.cs:137`) allows `Invoke(Variable)` whenever that metadata is present. Reassign to a scalar then `Invoke` can pass analysis and fail at runtime. Reachability: legal Syntax; not in current domain lowering. First-assignment scan in `ResolveBlockType` (direct children only) is overwritten when the later `Assignment` node is analyzed, so type updates; metadata does not.
- Suggestion: Clear `StoredLambdaMetadata` on non-closure assignment. Test: assign lambda, assign `1L`, `Invoke` — analysis error.
- Status: open
- Found by: pass A

### Issue 5 -- Severity: nit
- File: Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.Helpers.cs:263
- Description: `CompileVariable` xmldoc still says it will declare a new local on first use. The method only looks up, then throws `Variable '…' is not declared in this scope`. Fail-closed matches VM/analysis; the comment is a lying invariant.
- Suggestion: Narrow the comment to lookup-only / fail-closed.
- Status: open
- Found by: pass B

### Issue 6 -- Severity: nit
- File: Poly/Ast/Nodes/Variable.cs:5
- Description: Comment states `Assignment` is the only write. Foreach emit writes `LoopVariable` without an `Assignment` node (`DirectVmAbiEmitter.Statements.cs` foreach path). CORE/foreach declare is the other declared-binding form.
- Suggestion: Narrow to “user writes are `Assignment`; foreach writes the loop variable.”
- Status: open
- Found by: pass A

## Checklist

- [x] Diff collected; scope drift noted (Interpretation + DomainModeling lowering + MinimalApi C# IR + tests/docs; no binaries)
- [x] Stance: adversarial; mode multi; Pass B used §3.7.1
- [x] Producer/consumer keys traced (`Block.Variables` identity vs `VariablesByName` name vs printer fuse identity)
- [x] Null / not-found: undeclared Assignment analysis error; LINQ throw; VM `VariableWrite` throw
- [x] Sibling-path check: VM / LINQ / analysis declare; printer fusion vs unassigned `object`; debug span vs slot high-water
- [x] Fail-loud reachability: F1 reachable on valid sequential inner blocks in Normal mode
- [x] Invariant comments checked (`Variable` fusion; `CompileVariable` declare-on-use)
- [x] Counts from this-session greps (`CurrentLocalCount` 3 call sites; no remaining `new Variable(name, init)`)
- [x] Oracles: suite green; hook-span sibling untested
- [x] Prior follow-ups dispositioned in sibling follow-ups file
