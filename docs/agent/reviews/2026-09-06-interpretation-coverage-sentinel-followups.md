# Interpretation coverage follow-ups (Sentinel) — 2026-09-06

Source review: [`2026-09-06-interpretation-coverage-sentinel.md`](2026-09-06-interpretation-coverage-sentinel.md).

**Owner:** Nested implements tests only. Do not change `Poly/**` product code in the coverage-close slice unless a test cannot be written without a hook — then stop and file, do not silently “fix while testing.”

**Oracle:** `Interpreter.Compile` + execute (or compile-reject) on a Syntax tree. `BuildExpression` / LINQ / C# string / CFG metadata is not a substitute unless the F# item is explicitly an analysis-metadata or printer obligation.

**Prior Aug-25/26 interpretation follow-ups:** dispositioned from **current** master (`a6735beb`). Historical EH stub, foreach LINQ-only, Compile fail-closed, using dispose, switch-all-bodies, Heap null-handle are **fixed** in product + tests. Do not re-open those as F#.

---

## Open (Nested)

- [ ] **F1** — `Poly.Tests/Interpretation/ThrowVmTests.cs` — Compile+Execute `ThrowStatement` / `ThrowExpression` whose operand is **not** `New` (`Constant` of an `Exception` instance, or `Parameter`/`Variable` holding one). Assert the thrown object is that instance (type + message), not a fresh `Exception()`. Production: `DirectVmAbiEmitter.Statements.cs:324-340`. Today this sibling discards the operand.

- [ ] **F2** — `Poly.Tests/Interpretation/ExceptionHandlingVmTests.cs` — Catch body **reads** `CatchClause.VariableName` (exception message or type). Do not count “catch sets a flag” as coverage. Production: `DirectVmAbiEmitter.Statements.cs:353-368`; ScopeValidator has no catch binding (`VariableLifetimePass.cs`). Expect fail-loud or a bound value — assert whichever the current tree actually does, loudly.

- [ ] **F3** — `Poly.Tests/Interpretation/SuspendResumeVmTests.cs` (new) — `SuspendNode` then `ExecutionResult.Resume` continues; `Resume()` when not suspended throws (`ExecutionResult.cs:54-55`); `InterpreterStatus.Resuming` / PC dispatch (`DirectVmAbiEmitter.AbiCtx.cs:95-108`) actually runs remaining statements.

- [ ] **F4** — `Poly.Tests/Interpretation/LanguageSurfaceTests.cs` — Add `new ClrTypeReference(typeof(string))` to `CompileRejectKinds_FailLoud` samples (`:127-137`). Mirror in `LanguageVmTests` or `StaticMemberVmTests` as compile-reject of ClrTypeReference **as a value** (receiver path already exists).

- [ ] **F5** — `Poly.Tests/Interpretation/LanguageSurfaceTests.cs` — Construct `ResolvedTypeReference` in `AnalysisOnlyKinds_AreNotScriptEntry` (`:146-163`). Inventory lists it (`:105`) but never builds one.

- [ ] **F6** — `Poly.Tests/Interpretation/InvalidProgramTests.cs` (or new `JumpTargetTests.cs`) — Assert diagnostic **code JT0003** (labeled continue, unknown label) and **JT0005** (goto unknown; today `Goto_UnknownLabel` uses `expectedCode: null` at `:71-75`). Pin JT0005 vs sibling CF0001 (`ControlFlowAnalysisPass.cs:46`).

- [ ] **F7** — `Poly.Tests/Interpretation/ControlFlowAnalysisTests.cs` — Remaining codes **CF0001, CF0004, CF0006, CF0010, CF0013**. Rename `If_ConstFalse_ElidesElseBranch` (`:350`) to match then-elision. Assert `MustExecuteMetadata` in `MustExecute_BasicEntryStmts` (`:402`) instead of discarding `IsMustExecute`.

- [ ] **F8** — `Poly.Tests/Interpretation/SideEffectAnalysisTests.cs` (new) — Enable `SideEffectAnalysisOptions.EmitElisionDiagnostics` and assert `DEAD_CODE_ELIDABLE` plus `SideEffectMetadata` / `ElisionMetadata` / `AssignmentValueUsedMetadata` (`SideEffectAnalysisPass.cs:8-54`). Default options emit nothing — the test must turn the flag on.

- [ ] **F9** — `Poly.Tests/Interpretation/DefiniteAssignmentAnalyzerTests.cs` (new) — Assert `DefiniteAssignmentMetadata` on a lambda body (`DefiniteAssignmentAnalyzer.cs:23-24`); if/else merge; loop assigns do not leak. Pass publishes metadata only — do not invent a diagnostic the pass does not emit.

- [ ] **F10** — `Poly.Tests/Interpretation/LambdaReturnTypeAnalyzerTests.cs` (new) — `Invoke(Lambda)` and stored `Invoke(Variable)` resolved type is the **body** type; the `Lambda` node stays heap/function (`LambdaReturnTypeAnalyzer.cs:8-23`).

- [ ] **F11** — `Poly.Tests/Interpretation/ConstantFoldingTests.cs` — At least one `Interpreter.Compile`+Execute of a folded tree, and an assertion that `GetNodeReplacement` is what the emitter compiles (not Analyze-only `GetConstantValue`).

- [ ] **F12** — `Poly.Tests/Interpretation/MethodInvocationSemanticResolutionTests.cs` — `Interpreter.Compile`+Execute of `IndexOf(char)` vs string overload (VM result, not just `GetResolvedMember`). Full-pipeline `Interpreter.Analyze`+Compile-reject of the no-match `Substring(1.5)` tree (`:39-45` currently AnalyzeNode only; `UseAllAnalyzers` omits SyntaxTypeCompatibility).

- [ ] **F13** — `Poly.Tests/Interpretation/TypeDefinitionNodeAnalyzerTests.cs` (or new `TypeRefResolutionTests.cs`) — Analyze a type with `OptionalTypeReference` and `MapTypeReference` member types (not merely LanguageSurface compile-as-root). Collection element type already has `InterpretationStabilizationTests.cs:142`.

- [ ] **F14** — `Poly.Tests/Interpretation/AstConstructorDefinitionTests.cs` + new `AstMemberVmTests.cs` — `Interpreter.Compile` of `New` of an AST-defined type must fail loud today (`EmitNew` is CLR-ctor only, `DirectVmAbiEmitter.cs:274-332`). Compile `Member`/`Assignment` on a dict-backed AST property/field through `Interpreter.Compile` with `TypeDefinitionNodeAnalyzer` as provider — isolated `EmitRead` LINQ (`TypeDefinitionNodeAnalyzerTests.cs:183-301`) is not VM coverage.

- [ ] **F15** — `Poly.Tests/Interpretation/InvokeMemberInstanceTests.cs` (or `AstMethodBodyVmTests.cs`) — Invoke of an AST `MethodDefinitionNode` with a non-empty Body and **no** CLR/`InvokeNamed` host method: fail-loud (or execute the body if that is already the contract). `Execute_HeapInstanceWithNotify_InvokesMethod` (`:88`) is host CLR `Notify` plus empty `Body`.

- [ ] **F16** — `Poly.Tests/Interpretation/CSharpGeneratorTests.cs` — Printer cases currently missing: Comment, Default, TypeOf, NewArray, SuspendNode, PopCount, StridedSetBits, NullForgiving, BitwiseNot, Await; type-ref Map / Optional / Union. Union has no `WriteExpression` arm — assert whatever Generate does (throw or skip), do not assume a string.

- [ ] **F17** — `Poly.Tests/Interpretation/UsingStatementVmTests.cs` / `ForEachEnumeratorDisposeTests.cs` (new or extend LanguageVm) — Using of a non-`IDisposable` resource (fail-loud vs skip). Custom `IEnumerable` whose enumerator is `IDisposable`: Dispose after normal completion, `break`, and throw (`DirectVmAbiEmitter.Statements.cs:631-635`, `:650-667`). Happy-path using dispose already exists (`LanguageVmTests.cs:747`).

- [ ] **F18** — `Poly.Tests/Interpretation/ExceptionHandlingVmTests.cs` — Nested try execute (inner catch vs outer); throw inside catch. Analysis-only nested try (`ExceptionRegionAnalysisTests.cs:207`) is not the VM oracle.

- [ ] **F19** — `Poly.Tests/Interpretation/InterpretResultAbiTests.cs` — After `BreakStatement` / `ContinueStatement` / `ThrowStatement`, `InterpreterResult.Kind` is **not** `Break`/`Continue`/`Throw` (those factories are unused; VM uses CLR exceptions and native labels). Complements F3 for Suspend vs Resume.

- [ ] **F20** — Test-theater replacements (same files; body must match name):
  - `CallSiteCatalogTests.SimpleInvoke_GetsSiteIndex` (`:35`) — rename or add a CLR invoke that **does** get an index; keep the lambda-empty case under an honest name.
  - `ControlFlowAnalysisTests.If_ConstFalse_ElidesElseBranch` — rename (see F7).
  - `TypeCastTests.TypeCast_GetTypeDefinition_ReturnsTargetType` (`:141`) — assert resolved type is `double`, not `node != null`.
  - `BlockTests.Block_GetTypeDefinition_ReturnsLastExpressionType` (`:116`) — assert last-expression type.
  - `InterpreterLanguageGotchaTests.Member_OnNull_FailsLoud` (`:477`) — do not empty-catch compile reject; force the execute path or split compile vs execute.
  - `ThisReferenceTests.Analyze_RootProgramThisReference_IsLegal` (`:159`) — drop TH0002 (pass does not emit it).
  - `PropertyDefinitionNodeTests` — Analyze (or move out of Interpretation); construction+ToString is not interpretation.

- [ ] **F21** — `Poly.Tests/Interpretation/VariableScopeTests.cs` (new) — Assert `VariableAnalysisMetadata` on root; shadow warning (`VariableLifetimePass.cs:163`); captured/escaped sets used by emit. Undeclared fail-closed already in `InvalidProgramTests.cs:273` — do not duplicate without a new oracle (metadata).

- [ ] **F22** — `Poly.Tests/Interpretation/VmHeapComparisonTests.cs` (new) — `LessThan`/`Equal` on DateTime, DateOnly, Guid heap objects (`VmHeapComparison.cs`). Mixed incomparable types fail loud. Do **not** add tests that only `new` unused `VmValueMarshaller` / `FunctionEntry` / `VmTrace`.

- [ ] **F23** — `Poly.Tests/Interpretation/MermaidAstGeneratorTests.cs` (new) — Smoke Generate on one Executable node and one AnalysisOnly type-def. Integration already has `Poly.Tests/Integration/MermaidAstVisualizationTests.cs`; this F# is in-scope because Interpretation tests currently have zero Mermaid oracles.

Optional / nit (not blocking F1–F23 unless Nested is already in the file):

- NodeIdTests do not belong under Interpretation (`NodeIdTests.cs`).
- Analysis README: JT0005; no CF0007–CF0009 (`Poly/Interpretation/Analysis/README.md`).

---

## Disposition of prior interpretation follow-ups (current source)

Re-read current tree; do not chain-trust Aug-25 review quotes.

| Prior item (Aug-25 stabilization) | Disposition |
|-----------------------------------|-------------|
| ExceptionHandlingVmTests stub `var v = true` | **fixed** — `ExceptionHandlingVmTests.cs:7-57` three real VM tests |
| ForEachLoopTests LINQ-only | **fixed** — dual Compile+Execute in each behavior test |
| Compile / CompileChecked ignores non-type errors | **fixed** — `Interpreter.cs:77-81` all `DiagnosticSeverity.Error` |
| UsingStatement empty finally | **fixed** — Dispose in `EmitUsingStatement` |
| Switch evaluates every case body | **fixed at runtime** — IfThenElse (`Statements.cs:249-263`); remaining thin = pattern side effects, not this F# |
| Heap.Allocate(null) live handle | **fixed** — `Heap.cs:26-27` returns 0 |
| Unresolved member read passthrough | **fixed** — `InterpretationStabilizationTests.cs:23` Compile throws |
| HashSet / null foreach | **fixed** — `InterpretationStabilizationTests.cs:47-70` |
| Stored-closure arity (PR38 F1) | **fixed in tests** — `InvalidProgramTests.cs:300-339` |

Still open from this inventory: F1–F23 above.

---

## Process (if Nested sees the same class again)

`UseAllAnalyzers` omitting SyntaxTypeCompatibility / TypeDefinition / ExceptionRegion is how AnalyzeNode tests claim coverage they do not have. Prefer `Interpreter.Analyze` / `Interpreter.Compile` for fail-closed claims. Rename tests whose body asserts the opposite of the name (F20) rather than adding a second test that leaves the lie in place.
