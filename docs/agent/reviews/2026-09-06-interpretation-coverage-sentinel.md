# Interpretation coverage inventory (Sentinel) — 2026-09-06

- **Target**: paths `Poly/Interpretation/**` + `Poly/Ast/Nodes/TypeDefinitions/**` (as consumed by interpretation) vs `Poly.Tests/Interpretation/**`; branch `review/interpretation-coverage` reset from `origin/master` (`a6735beb`)
- **Mode**: coverage inventory (phenomenal-review stance: assume wrong; primary evidence; no rubber-stamp of Aug-25/26 reviews)
- **Issue counts**: 2 bugs, 21 suggestions, 2 nits
- **Verdict**: not ship as “interpretation is well covered.” Language-VM happy-path oracles are real; several named tests do not exercise the path they claim; analysis diagnostics, catch-var/throw siblings, suspend-resume, and AST-type VM are missing or thin.
- **Process notes**: `UseAllAnalyzers` / `AnalyzeNode` omit TypeDefinition, SyntaxTypeCompatibility, and ExceptionRegion (`Poly.Tests/TestHelpers/NodeTestHelpers.cs`). Dual-oracle (`BuildExpression` + `Interpreter.Compile`) is valid when both assertions stay. CFG/`BuildExpression`/C# string emit is **not** the VM oracle (`Poly/Interpretation/Analysis/README.md:48`). Follow-ups: [`2026-09-06-interpretation-coverage-sentinel-followups.md`](2026-09-06-interpretation-coverage-sentinel-followups.md).

## Summary

Recomputed: **46** production `.cs` files under `Poly/Interpretation/`, **18** under `Poly/Ast/Nodes/TypeDefinitions/`, **48** test files / **827** `[Test]` methods under `Poly.Tests/Interpretation/`. Language surface inventory is complete: **84** concrete `Node` kinds (60 Executable / 7 CompileReject / 17 AnalysisOnly) locked by `EveryConcreteNodeType_IsInventoried`. Every Executable kind has at least one `Interpreter.Compile`+execute (or compile-reject) in `LanguageVmTests` (88 tests). Historical Aug-25 theater (stub `ExceptionHandlingVmTests`, foreach LINQ-only, `Compile` not fail-closed, using empty finally, switch running every body, `Heap.Allocate(null)` live handle) is **gone on current master** — re-verified, not chain-trusted.

That does not make the suite a coverage story. Two reachable VM siblings are untested and wrong (throw of a non-`New` operand; catch variable name). `ExecutionResult.Resume` has **zero** callers in tests. Several analysis passes produce metadata or diagnostic codes that no Interpretation test asserts. Named tests (`SimpleInvoke_GetsSiteIndex`, `If_ConstFalse_ElidesElseBranch`, `TypeCast_GetTypeDefinition_ReturnsTargetType`, `Block_GetTypeDefinition_ReturnsLastExpressionType`, `Member_OnNull_FailsLoud`) pass for the wrong reason.

## Counts (primary evidence)

| Item | Count | Evidence |
|------|------:|----------|
| `Poly/Interpretation/**/*.cs` | 46 | `find` |
| Type definition nodes | 18 | `find Poly/Ast/Nodes/TypeDefinitions` |
| `Poly.Tests/Interpretation/*.cs` | 48 | `find` (not ~49) |
| `[Test]` methods | 827 | per-file `rg -c` sum |
| LanguageSurface `Kinds` | 84 (60/7/17) | `LanguageSurfaceTests.cs:23-110` |
| Standard analyzer passes | 14 | `Interpreter.cs:19-35`; lock `InterpretationStabilizationTests.cs:449-466` |
| Files that never call `Interpreter.Compile` | 14 | analysis/C#/LINQ-only files (ThrowVm uses `Execute(Node)` which compiles) |
| `ExecutionResult.Resume(` in Interpretation tests | 0 | `rg` |
| `DEAD_CODE_ELIDABLE` / `DefiniteAssignmentMetadata` in tests | 0 | `rg` |

## Checklist

- [x] Stance: adversarial / assume wrong; coverage inventory not a PR-diff bug hunt
- [x] Sibling-path check for dual backends (VM vs LINQ vs C#) and dual emit arms (throw `New` vs not; catch name vs read)
- [x] Fail-loud / throw paths have reachability notes
- [x] Counts recomputed; Aug-25 claims re-verified against current source
- [x] Test theater hunted (name vs body)
- [x] Follow-ups written with Nested test obligations

---

## Issues

### Issue 1 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:324-340`
- Description: `EmitThrow` only preserves the operand when `ts.Exception is New`. Any other operand (`Constant` of an exception instance, `Variable`/`Parameter` holding one, `ThrowExpression` wrapping those) is compiled for side effects then replaced with `throw new Exception()`. `ThrowExpression` shares this arm (`DirectVmAbiEmitter.cs:217`). Reachability: legal Syntax; `LanguageVmTests.ThrowExpression_ThrowsOperand` (`LanguageVmTests.cs:158`) and all four `ThrowVmTests` (`ThrowVmTests.cs:7-38`) only construct `New(...)`. Sibling-path drift: the documented “throw the operand” contract is true only for the `New` shape.
- Suggestion: Nested test in `ThrowVmTests.cs`: `ThrowStatement` / `ThrowExpression` of a `Constant`/`Parameter` exception must propagate **that** instance (message/type), not a fresh `Exception`. Product fix is Nested-out-of-scope.
- Status: open

### Issue 2 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:353-368`; `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs` (no `CatchClause` arm); `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:178` (Variable keys are `ReferenceEqualityComparer`)
- Description: `CatchClause.VariableName` is a string. Emitter allocates a **new** `Variable(clause.VariableName)`, `DeclareVariable`s it, writes the heap handle, then compiles the catch body. ScopeValidator never treats the catch name as a declared binding. A body `Variable("ex")` is a different instance → undeclared at analysis, or a live slot that is not the exception. Tests name `"ex"` and never read it (`ExceptionHandlingVmTests.cs:15-18`, `LanguageVmTests.cs:738`, `InterpreterLanguageGotchaTests.cs:425-427`). C# print uses the name (`CSharpGeneratorTests.cs:527`). Claimed catch-variable surface is untested and currently unusable from AST.
- Suggestion: Nested test in `ExceptionHandlingVmTests.cs` that **reads** the catch variable (message / type) and asserts either a typed value or a loud undeclared/`VM compile rejected` (today: fail). Do not treat “catch sets a flag” as catch-var coverage.
- Status: open

### Issue 3 -- Severity: suggestion
- File: `Poly/Interpretation/ExecutionResult.cs:52-63`; `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs:95-108`
- Description: `SuspendNode` sets `IsSuspended` (`LanguageVmTests.cs:755-758`; `DirectVmAbiEmitterTests.cs:228-234`). Zero Interpretation tests call `ExecutionResult.Resume`. `InterpreterStatus.Resuming` and `EmitPcDispatch` are unexercised. `DirectVmAbiEmitterTests.FormatComparison_DirectEmitterTreeForSuspendCase` (`:245`) compiles a tree and asserts the program is not null — theater relative to resume.
- Suggestion: `Poly.Tests/Interpretation/SuspendResumeVmTests.cs` — suspend, resume, assert continuation + `Resume()` when not suspended throws (`ExecutionResult.cs:54-55`).
- Status: open

### Issue 4 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/LanguageSurfaceTests.cs:89` vs `:127-137`; `Poly/Ast/Nodes/TypeReference.cs:4`
- Description: `ClrTypeReference` is inventoried CompileReject. `CompileRejectKinds_FailLoud` samples omit it. It subclasses `TypeReference`, so `CompileNodeInner` (`DirectVmAbiEmitter.cs:210-211`) would `RejectCompile` — but that is unasserted. Receiver use is covered (`StaticMemberVmTests.cs:41`).
- Suggestion: Add `new ClrTypeReference(typeof(string))` to `CompileRejectKinds_FailLoud` and a `LanguageVmTests` compile-reject (or extend `StaticMemberVmTests.cs`).
- Status: open

### Issue 5 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/LanguageSurfaceTests.cs:105` vs `:146-163`
- Description: `ResolvedTypeReference` is AnalysisOnly in the inventory and **omitted** from `AnalysisOnlyKinds_AreNotScriptEntry` samples. No Interpretation test constructs it. Unused by TypeDefinition/TypeAndMember/C# generator.
- Suggestion: Construct + compile-as-root in `LanguageSurfaceTests.cs`; document AnalysisOnly vs dead-to-interpretation.
- Status: open

### Issue 6 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/Semantics/JumpTargetPass.cs:237-258`; `Poly.Tests/Interpretation/InvalidProgramTests.cs:71-75`
- Description: JT0001/JT0002/JT0004 are asserted (`InvalidProgramTests.cs:55-67,102-106`). **JT0003** (labeled continue miss) has no error test (success path exists: `InterpreterLanguageGotchaTests.cs:312`). **JT0005** (goto unknown) is the same message as **CF0001** (`ControlFlowAnalysisPass.cs:46`); `Goto_UnknownLabel` uses `expectedCode: null` so neither code is pinned. README (`Analysis/README.md:15`) still says JT0001–JT0004.
- Suggestion: `InvalidProgramTests.cs` (or new `JumpTargetTests.cs`): JT0003 error + JT0005 code; optionally isolate from CF0001.
- Status: open

### Issue 7 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/ControlFlow/ControlFlowAnalysisPass.cs`; `Poly.Tests/Interpretation/ControlFlowAnalysisTests.cs`
- Description: Tests hit CF0002, CF0003, CF0005 (via a misnamed test), CF0011/CF0012. Untested codes: **CF0001**, **CF0004**, **CF0006**, **CF0010**, **CF0013**. No CF0007–CF0009 exist (README `CF0001-CF0013` overstates). Theater: `If_ConstFalse_ElidesElseBranch` (`ControlFlowAnalysisTests.cs:350`) elides **then** (comment at `:367` admits it). `MustExecute_BasicEntryStmts` (`:402`) discards `IsMustExecute`. Analyze-only; no Compile of dead-code programs.
- Suggestion: Extend `ControlFlowAnalysisTests.cs`; rename the const-false test; assert MustExecuteMetadata; add remaining codes.
- Status: open

### Issue 8 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/Semantics/SideEffectAnalysisPass.cs:8-54`
- Description: `DEAD_CODE_ELIDABLE` emits only when `SideEffectAnalysisOptions.EmitElisionDiagnostics` is true; default is false. Zero Interpretation tests reference the code, `SideEffectMetadata`, `AssignmentValueUsedMetadata`, or the options type. Pass is a pipeline dependency, not an oracle.
- Suggestion: new `Poly.Tests/Interpretation/SideEffectAnalysisTests.cs`.
- Status: open

### Issue 9 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/Semantics/DefiniteAssignmentAnalyzer.cs:5-24`
- Description: Writes `DefiniteAssignmentMetadata` only on `lambda.Body`. Does not diagnose uninitialized reads. Zero tests assert the metadata (`rg DefiniteAssignmentMetadata` in tests = empty). Pipeline registration is not coverage.
- Suggestion: new `Poly.Tests/Interpretation/DefiniteAssignmentAnalyzerTests.cs`.
- Status: open

### Issue 10 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/Semantics/LambdaReturnTypeAnalyzer.cs:11-23`
- Description: Stamps Invoke resolved type from lambda/stored-lambda body when current type is null/`object`. No dedicated test. Implicit only via `ValueRepresentationTests.Invoke_StoredLambdaReturningBool_IsBool` (`ValueRepresentationTests.cs:95`).
- Suggestion: new `Poly.Tests/Interpretation/LambdaReturnTypeAnalyzerTests.cs`.
- Status: open

### Issue 11 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/ConstantFoldingTests.cs` (26 tests); `Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs`
- Description: Dedicated file is Analyze-only (metadata + `GetConstantValue`). Zero `Interpreter.Compile` in that file. VM may execute replacements via the full pipeline, but no test asserts `GetNodeReplacement` is what `DirectVmAbiEmitter` compiles. LINQ sibling does (`LinqExpressionGeneratorTests.cs:76`).
- Suggestion: Extend `ConstantFoldingTests.cs` with Compile+Execute of a folded tree and a stripped-original / replacement assertion.
- Status: open

### Issue 12 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/MethodInvocationSemanticResolutionTests.cs:8-46`
- Description: Three AnalyzeNode tests (char overload, exact vs assignable, no-match → null). None `Interpreter.Compile`. Unmatched overload is silent null unless SyntaxTypeCompatibility later rejects — `UseAllAnalyzers` omits that pass. No VM oracle that `IndexOf('e')` vs `IndexOf("e")` actually dispatches.
- Suggestion: Extend `MethodInvocationSemanticResolutionTests.cs` with Compile+Execute of the char overload and Compile-reject of the no-match tree through `Interpreter.Analyze` (full 14).
- Status: open

### Issue 13 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/LanguageSurfaceTests.cs:160-162`; type-ref resolvers in TypeDefinition analysis
- Description: `OptionalTypeReference` / `MapTypeReference` appear only as AnalysisOnly compile-as-root samples. No Interpretation test analyzes them as member types. `CollectionTypeReference` has one element-type test (`InterpretationStabilizationTests.cs:142`). Union collapse is covered (`TypeDefinitionNodeAnalyzerTests.cs:66`).
- Suggestion: Extend `TypeDefinitionNodeAnalyzerTests.cs` (or new `TypeRefResolutionTests.cs`) for Optional/Map property types.
- Status: open

### Issue 14 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs:274-332`; `Poly.Tests/Interpretation/AstConstructorDefinitionTests.cs:44-74`; `TypeDefinitionNodeAnalyzerTests.cs:183-301`
- Description: Analysis **does** resolve `New` of an AST type (`AstConstructorDefinitionTests.cs:44`). `EmitNew` requires `ClrConstructor`; AST ctor → `"no matching constructor"`. Property/field `EmitRead`/`EmitWrite` are tested as isolated LINQ lambdas, not `Interpreter.Compile(new Member(...))`. TEST THEATER: resolve-only New looks like AST-object VM coverage.
- Suggestion: `AstTypeNewVmTests.cs` / extend `AstConstructorDefinitionTests.cs`: Compile of `New(AST type)` must fail loud today. `AstMemberVmTests.cs`: Compile `Member`/`Assignment` on a dict-backed AST property/field with `TypeDefinitionNodeAnalyzer` as provider.
- Status: open

### Issue 15 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/InvokeMemberInstanceTests.cs:57-94`; `Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs`
- Description: `Execute_HeapInstanceWithNotify_InvokesMethod` compiles with a `MethodDefinitionNode` whose `Body` is `new Block([])` (`:62`) and succeeds because the host CLR type has `Notify`. AST method bodies are never VM-executed; unresolved members fall through to `InvokeNamed`.
- Suggestion: Honesty test in `InvokeMemberInstanceTests.cs` (or `AstMethodBodyVmTests.cs`): Invoke of an AST-only method (no CLR `Notify`, no `InvokeNamed`) fail-loud; do not count host CLR dispatch as method-body coverage.
- Status: open

### Issue 16 -- Severity: suggestion
- File: `Poly/Interpretation/CSharp/CSharpGenerator.cs`; `Poly.Tests/Interpretation/CSharpGeneratorTests.cs` (75 tests)
- Description: Strong printer coverage for arithmetic, control flow, type-def lineage/record/auto-prop/ctor. Missing Interpretation tests for printer of: Comment, Default, TypeOf, NewArray, SuspendNode, PopCount, StridedSetBits, NullForgiving, BitwiseNot, Await, Map/Optional/Union type refs. Union has **no** `WriteExpression` arm (fail-loud untested).
- Suggestion: Extend `CSharpGeneratorTests.cs` (and `CSharpTypeRefEmitTests.cs` if split).
- Status: open

### Issue 17 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:584-667`
- Description: Using dispose happy path + throw-still-disposes are covered (`LanguageVmTests.cs:747`, `InterpreterLanguageGotchaTests.cs:441`). Missing: non-`IDisposable` resource; nested using; foreach enumerator `IDisposable` (`Statements.cs:631-635`) — HashSet foreach sums items (`InterpretationStabilizationTests.cs:47`) but does not assert enumerator Dispose.
- Suggestion: `UsingStatementVmTests.cs` / `ForEachEnumeratorDisposeTests.cs`.
- Status: open

### Issue 18 -- Severity: suggestion
- File: `Poly.Tests/Interpretation/ExceptionRegionAnalysisTests.cs:207` (nested try is analysis-only); `ExceptionHandlingVmTests.cs` (3 tests)
- Description: Typed catch match/skip and finally are VM-covered. Nested try **execute**, throw-in-catch, catch-variable read (Issue 2) are not.
- Suggestion: Extend `ExceptionHandlingVmTests.cs` (nested try execute).
- Status: open

### Issue 19 -- Severity: suggestion
- File: `Poly/Interpretation/InterpreterResult.cs:12-27,91-103`; `Interpreter.cs:150-194`
- Description: `InterpretResult` only produces Void / Value / Suspend. Factories `Return` / `Break` / `Continue` / `Throw` are dead API from a prior interpreter. No test asserts that a `BreakStatement` program is **not** `ResultKind.Break` (it completes as void/value; throw is a CLR exception).
- Suggestion: Extend `InterpretResultAbiTests.cs`.
- Status: open

### Issue 20 -- Severity: suggestion
- File: multiple Interpretation tests (theater)
- Description: Name vs body:
  1. `CallSiteCatalogTests.SimpleInvoke_GetsSiteIndex` (`:35-43`) — asserts catalog **Count == 0**.
  2. `ControlFlowAnalysisTests.If_ConstFalse_ElidesElseBranch` (`:350`) — elides **then**.
  3. `TypeCastTests.TypeCast_GetTypeDefinition_ReturnsTargetType` (`:141-149`) — `BuildExpression` then `Assert node != null`.
  4. `BlockTests.Block_GetTypeDefinition_ReturnsLastExpressionType` (`:116-124`) — same.
  5. `InterpreterLanguageGotchaTests.Member_OnNull_FailsLoud` (`:477-488`) — empty `catch (InvalidOperationException)` so compile-reject **or** execute-throw both pass.
  6. `ControlFlowAnalysisTests.MustExecute_BasicEntryStmts` (`:402`) — discards `IsMustExecute`.
  7. `ThisReferenceTests.Analyze_RootProgramThisReference_IsLegal` (`:159`) — guards **TH0002**, which this pass does not emit.
  8. `PropertyDefinitionNodeTests` (`:6`) — AST `DefaultValue` normalize; never Analyze/Compile.
- Suggestion: Rename or replace each oracle so the body matches the name (still in the existing files).
- Status: open

### Issue 21 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs:34-58,179`; `Poly.Tests/Interpretation/BlockScopeTests.cs`
- Description: Undeclared-variable fail-closed exists (`InvalidProgramTests.cs:273`). No test asserts `VariableAnalysisMetadata`, escaped/captured sets (one `LambdaCaptureMetadata` read in `ValueRepresentationTests.cs:110`), or the shadow warning (`:163`). `BlockScopeTests` names claim scope; bodies assert last-expression values via LINQ+VM.
- Suggestion: new `Poly.Tests/Interpretation/VariableScopeTests.cs`.
- Status: open

### Issue 22 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/VmHeapComparison.cs`; `Poly/Interpretation/Vm/VmValueMarshaller.cs`; `Poly/Interpretation/Vm/ValueStack.cs`
- Description: `VmHeapComparison` (DateTime/DateOnly/Guid order) has no Interpretation test (no DateTime `LessThan`). `VmValueMarshaller` is **unreferenced** outside its file (dead helper; emitter inlines). `ValueStack` has no unit tests. `FunctionEntry` / `VmTrace.LogUop` have no callers outside their files.
- Suggestion: `VmHeapComparisonTests.cs` for ordered heap compares; do not write tests that only instantiate dead helpers.
- Status: open

### Issue 23 -- Severity: suggestion
- File: `Poly/Interpretation/Mermaid/MermaidAstGenerator.cs`
- Description: Mermaid tests live in `Poly.Tests/Integration/MermaidAstVisualizationTests.cs`, **not** under `Poly.Tests/Interpretation/`. In-scope Interpretation folder has zero Mermaid tests.
- Suggestion: `Poly.Tests/Interpretation/MermaidAstGeneratorTests.cs` (smoke of one executable + one AnalysisOnly node) **or** document Integration as the oracle and drop Interpretation expectation.
- Status: open

### Issue 24 -- Severity: nit
- File: `Poly.Tests/Interpretation/NodeIdTests.cs:7-34`
- Description: `NodeId` equality is AST, not interpretation. Misplaced; not a coverage hole in the VM.
- Suggestion: Move out of Interpretation tests when Nested is touching hygiene; no Nested product test required for coverage of interpretation.
- Status: open

### Issue 25 -- Severity: nit
- File: `Poly/Interpretation/Analysis/README.md:15,17`; `ThisReferenceTests.cs:159`
- Description: README omits JT0005; implies CF0007–CF0009; TH0002 is asserted in a test but not produced by `ThisReferenceContextPass`.
- Suggestion: Docs/test cleanup (Nested may skip nits unless already in a test file they touch).
- Status: open

---

## Area-by-area inventory

Legend: **COVERED** = tests construct the interesting state and assert the contract (VM Compile+Execute or analysis diagnostic/metadata). **THIN** = tests exist but happy-path / LINQ / wrong sibling / name≠body. **MISSING** = no Interpretation test exercises the pass/path.

### Analysis passes (14)

Built order (`InterpretationStabilizationTests.cs:449-466`): TypeDefinitionNode → ThisReference → TypeAndMember → LambdaReturnType → VariableScope → SideEffect → ConstantFolding → JumpTarget → ControlFlow → ExceptionRegion → ValueRepresentation → SyntaxTypeCompatibility → CallSiteCatalog → DefiniteAssignment.

| Pass | Verdict | Evidence | Nested test file |
|------|---------|----------|------------------|
| TypeDefinitionNodeAnalyzer | COVERED mapping; THIN VM | `TypeDefinitionNodeAnalyzerTests.cs` Analyze + isolated EmitRead LINQ; no `Interpreter.Compile` of AST New/Member | extend same + F14 |
| ThisReferenceContext | COVERED | TH0001 analyze + `InvalidProgramTests.cs:87` Compile reject; root this VM `LanguageVmTests.cs:525` | F20 TH0002 |
| TypeAndMemberResolver | COVERED resolve; THIN codes | Member miss `InvalidProgramTests.cs:79` (`expectedCode: null`); overload tests Analyze-only | F12 |
| ScopeValidator (`VariableLifetimePass.cs`, class `ScopeValidator`) | THIN | undeclared Compile-reject; no metadata/shadow | F21 `VariableScopeTests.cs` |
| SideEffectAnalyzer | MISSING | options default off; 0 tests | F8 `SideEffectAnalysisTests.cs` |
| JumpTargetAnalyzer | THIN | JT0001/2/4 yes; JT0003 error + JT0005 code no | F6 |
| LambdaReturnTypeAnalyzer | THIN | implicit VR only | F10 |
| SyntaxTypeCompatibilityAnalyzer | COVERED | `InvalidProgramTests` 20+ Analyze+Compile | — |
| ValueRepresentationAnalyzer | COVERED | 44 tests `ValueRepresentationTests.cs` | — |
| CallSiteCatalogAnalyzer | COVERED + theater | 11 tests; `SimpleInvoke_GetsSiteIndex` asserts empty | F20 + Member getter |
| DefiniteAssignmentAnalyzer | MISSING | metadata never asserted | F9 |
| ExceptionRegionAnalyzer | COVERED metadata; THIN VM nest | 11 Analyze tests; VM in ExceptionHandling (3) | F18 |
| ConstantFoldingPass | COVERED metadata; THIN Compile | 26 Analyze tests | F11 |
| ControlFlowAnalysisPass | THIN | CF0002/3/5/11/12; theater names; missing codes | F7 |

`LambdaCaptureCollector` is a helper (`VariableLifetimePass.cs:58`), not a 15th pass. Captures are COVERED at VM (`ClosureCaptureTests.cs`, 22 tests) and THIN as metadata.

### VM / ABI / Interpreter façade

| Area | Verdict | Evidence |
|------|---------|----------|
| `Interpreter.Compile` fail-closed | COVERED | `Interpreter.cs:77-81`; `InvalidProgramTests`; `CompileChecked` alias `Interpreter.cs:59-61`; `InterpretationStabilizationTests.cs:14` |
| `CompileNodeInner` executable kinds | COVERED | `DirectVmAbiEmitter.cs:187-252` vs `LanguageVmTests` 88 oracles |
| CompileReject (Await, ParameterReference, type names as values) | COVERED except ClrTypeReference sample | F4 |
| AnalysisOnly as script entry | COVERED samples except ResolvedTypeReference | F5 |
| Closures / stored lambda / arity | COVERED | `ClosureCaptureTests`, `LanguageVmTests.cs:371+`, `InvalidProgramTests.cs:300-339` stored arity |
| Exceptions (typed catch, finally, using dispose) | COVERED happy; THIN nest/catch-var | Issues 2, 18, 17 |
| Throw operand | THIN / bug | Issue 1 |
| Control flow (if/while/do/for/foreach/switch/goto/break/continue) | COVERED | LanguageVm + gotchas + DirectVm; foreach now dual `ForEachLoopTests.cs:45-47` |
| Labeled break/continue success | COVERED | `InterpreterLanguageGotchaTests.cs:312,332` |
| And/Or/Conditional short-circuit | COVERED | `InterpreterLanguageGotchaTests.cs:13-80` |
| Suspend | THIN (status only) | Issue 3 |
| Resume / Resuming / PC dispatch | MISSING | Issue 3 |
| Debugger | COVERED | `VmDebuggerTests.cs` 37 tests |
| NoDebug vs Normal | COVERED | sieve, `VmCorrectnessTests.cs:606`, debugger |
| Heap null handle 0 | COVERED | `Heap.cs:26-27`; stabilization tests |
| `VmHeapComparison` | MISSING | Issue 22 |
| `VmValueMarshaller` | MISSING (dead) | unreferenced |
| `InterpretResult` Void/Value/heap | COVERED | `InterpretResultAbiTests.cs`, integration |
| `InterpretResult` Break/Continue/Throw kinds | MISSING (dead API) | Issue 19 |
| InvokeNamed / host Notify | COVERED as host dispatch, not AST body | Issue 15 |

### Generators

| Backend | Verdict | Evidence |
|---------|---------|----------|
| DirectVm (canonical) | COVERED kinds; holes above | LanguageVm + VmCorrectness (106) + DirectVmAbiEmitterTests (33) |
| LINQ | COVERED as sibling checker | `LinqExpressionGeneratorTests.cs` 9; dual-oracle helper `NodeTestHelpers.cs:130-141` |
| C# | COVERED core print; THIN remaining nodes / Map/Union | Issue 16 |
| Mermaid | MISSING in Interpretation tests | Issue 23 (exists under Integration) |

### Language surface

| Class | Verdict |
|-------|---------|
| Executable 60 | COVERED by LanguageVm Compile+Execute |
| CompileReject 7 | COVERED samples minus ClrTypeReference |
| AnalysisOnly 17 | COVERED samples minus ResolvedTypeReference |
| Inventory lock | COVERED `EveryConcreteNodeType_IsInventoried` |

### Type definitions (consumed by interpretation)

| Artifact | Analyzer | C# | VM | Tests |
|----------|----------|----|----|-------|
| TypeDefinitionNode | yes | yes | not script entry | Analyze COVERED; Compile-as-root reject COVERED |
| ConstructorDefinitionNode | yes | yes | EmitNew CLR-only | Analyze New resolve THIN/theater (F14) |
| FieldDefinitionNode | yes + EmitRead/Write dict | yes | Member hook untested via Compile | LINQ EmitRead THIN (F14) |
| MethodDefinitionNode | yes; Body stored | yes | InvokeNamed / CLR | host Notify COVERED; AST body MISSING (F15) |
| Property + getter/setter/initializer | yes; bodies not used for EmitRead | auto-prop C# | dict EmitRead | This in getter Analyze COVERED; setter body MISSING |
| Primitive/Named/TypeReference | yes | yes | reject as value; receiver/New CLR | COVERED |
| CollectionTypeReference | yes | yes | n/a | one element-type test |
| Optional/Map | yes in resolver | Map TypeRefName thin | n/a | MISSING as member types (F13) |
| UnionTypeReference | collapse COVERED | no WriteExpression arm | n/a | Analyze COVERED; C# MISSING (F16) |
| TypeDefinitionSemantics/Mutability/Equality | record/immutable C# + primary ctor | yes | unused | C# record COVERED |

### Errors / fail-closed

`InvalidProgramTests.cs` (35) is the systematic Analyze-then-Compile-reject suite: type compatibility, JT0001/2/4, TH0001, undeclared vars, mixed assign, stored-lambda arity, illegal Invoke targets. Gaps: JT0003/JT0005 codes, unmatched overload through full pipeline, throw/catch-var siblings, `AssertCompileOrExecuteRejectsReadable` (`:415`) allows either compile or execute — weaker than fail-closed-at-analyze.

### Test theater (do not treat as coverage)

| Test | path:line | Actual |
|------|-----------|--------|
| SimpleInvoke_GetsSiteIndex | `CallSiteCatalogTests.cs:35` | catalog empty |
| If_ConstFalse_ElidesElseBranch | `ControlFlowAnalysisTests.cs:350` | elides then |
| TypeCast_GetTypeDefinition_ReturnsTargetType | `TypeCastTests.cs:141` | node not null |
| Block_GetTypeDefinition_ReturnsLastExpressionType | `BlockTests.cs:116` | node not null |
| Member_OnNull_FailsLoud | `InterpreterLanguageGotchaTests.cs:477` | empty catch |
| MustExecute_BasicEntryStmts | `ControlFlowAnalysisTests.cs:402` | discarded |
| Analyze_RootProgramThisReference_IsLegal | `ThisReferenceTests.cs:159` | TH0002 ghost |
| AnalyzeNode_New_ResolvesAstDefinedConstructor | `AstConstructorDefinitionTests.cs:44` | no Compile |
| PropertyDefinitionNode_DefaultValue_NormalizesToInitializer | `PropertyDefinitionNodeTests.cs:6` | AST only |
| FormatComparison_DirectEmitterTreeForSuspendCase | `DirectVmAbiEmitterTests.cs:245` | program not null |
| Execute_HeapInstanceWithNotify_InvokesMethod | `InvokeMemberInstanceTests.cs:88` | CLR host, empty AST body |

### Historical Aug-25 claims (re-verified; do not reopen)

| Claim | Current |
|-------|---------|
| ExceptionHandlingVmTests stub | **Fixed.** 3 real Compile+Execute tests |
| ForEachLoopTests LINQ-only | **Fixed.** Dual oracle + LanguageVm + HashSet/null |
| Compile / CompileChecked not fail-closed | **Fixed.** All Error diagnostics (`Interpreter.cs:77-81`) |
| Using empty finally | **Fixed.** Dispose in finally |
| Switch evaluates all bodies | **Fixed at runtime** (IfThenElse); patterns still always built |
| Heap.Allocate(null) live handle | **Fixed.** returns 0 |

---

## Open F# list

All open missing/thin items (and both bugs as failing tests Nested must write) are checkable tasks in [`2026-09-06-interpretation-coverage-sentinel-followups.md`](2026-09-06-interpretation-coverage-sentinel-followups.md) as **F1–F23**. Sentinel does not implement.
