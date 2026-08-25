# Interpretation stabilization / correctness — 2026-08-25

- **Target**: paths `Poly/Interpretation/` (+ `Poly.Tests/Interpretation/` oracles)
- **Mode**: multi (four independent Pass B contexts; parent merged)
- **Issue counts**: 12 bugs, 8 suggestions, 2 nits
- **Verdict**: not ship as “Interpretation is stable.” Product domain IList-of-instances + Number-as-Int64 can run; canonical VM still disagrees with C#/LINQ on foreach, switch, using, void return, and ForLoop continue. `CompileChecked` is not a general fail-closed compile gate.
- **Process notes**: `ForEachLoopTests` and `ExceptionHandlingVmTests` are oracles that do not execute the VM. Recurring class: LINQ `BuildExpression` green treated as VM contract.

## Summary

Four Pass B reviewers (emitter ABI, analysis pipeline, Heap/State marshalling, C# vs LINQ vs VM siblings) independently hit the same failures: unresolved member **read** passthrough, `CompileChecked` only honoring `VmTypeCompatibility`, foreach always `Heap.Allocate`ing items while tests run LINQ, switch evaluating every case body, `using` with empty `finally`. Float/double/decimal ABI uses numeric `Convert` to `long` while constants use bit-casts. Handle `0` is ABI null, but `Heap.Allocate(null)` yields a live handle.

This session previously implemented domain for-invoke on the emitter; Pass B did not receive that implementer chat.

## Issues

### Issue 1 -- Severity: bug
- File: `Poly/Interpretation/Interpreter.cs:89`
- Description: `FailLoudOnTypeErrors` throws only when `d.Code == SyntaxTypeCompatibilityAnalyzer.DiagnosticCode`. Missing-member `ReportStructuralFailure`, `TH0001`/`TH0002`, scope, and definite-assignment errors do not stop emit. `Compile` never inspects diagnostics. Domain `ExecuteEffect` / `EvaluatePolicy` use `CompileChecked`; several runtime paths still use `Compile`. XML on `SyntaxTypeCompatibilityAnalyzer` claiming `Interpreter.Compile` fails loud is false. Zero tests reference `CompileChecked`.
- Suggestion: Fail closed on any `DiagnosticSeverity.Error` in `CompileChecked` (and stop using `Compile` on domain runtime). Test: misspelled member on a typed entity throws. Move detection to analyze-time refuse-emit.
- Status: open
- Found by: emitter Pass B, analysis Pass B

### Issue 2 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Expressions.cs:392`
- Description: Unresolved `Member` **read** returns `instanceExpr` (receiver handle). Assignment of unresolved member throws (`Statements.cs:82-85`). After Issue 1, `Member(entity, "Typo")` compiles; comparisons treat the handle as a long. LINQ uses `PropertyOrField` (fails); C# prints the name.
- Suggestion: Fail closed on unresolved/unreadable members, same as assignment. Test strips the member and asserts throw, not a handle.
- Status: open
- Found by: emitter Pass B, analysis Pass B

### Issue 3 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:567`
- Description: ForEach loop variable is always `Heap.Allocate(current)`. For `int[]`, `Add(sum, item)` adds handles. LINQ assigns typed `Current`. `ForEachLoopTests` only `node.BuildExpression()` (`ForEachLoopTests.cs:40`). Product for-invoke uses instance lists (handles are right) and hides this. C# emits language `foreach`.
- Suggestion: Long-representable elements as stack scalars; references as handles. Drive existing int[] / continue / break trees through `Interpreter.Compile`.
- Status: open
- Found by: emitter Pass B, sibling Pass B

### Issue 4 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:584`
- Description: VM `AsIListOrThrow` rejects non-`IList` (including null). LINQ uses `IEnumerable.GetEnumerator` + Dispose. C# `foreach` accepts any enumerable. Domain OneToMany happens to be `IList`. Customer C# over `ISet<>` can run while simulate/VM throws.
- Suggestion: One contract: IEnumerable+Dispose like C#, or reject non-IList at analyze so all backends agree. VM tests for null and HashSet.
- Status: open
- Found by: emitter Pass B, sibling Pass B

### Issue 5 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:275`
- Description: `EmitSwitch` `Block`s every pattern and body, then `Condition` picks a ring slot. Side-effecting bodies all run. LINQ `Expression.Switch`; C# `switch`. Direct tests only constant bodies.
- Suggestion: Nested `IfThenElse` / `Expression.Switch`. Test assignments/throws in non-taken cases.
- Status: open
- Found by: emitter Pass B

### Issue 6 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:602`
- Description: XML says Dispose. Implementation is `TryFinally(body, Empty())`. LINQ calls `IDisposable.Dispose`.
- Suggestion: Dispose the heap object in `finally` when `IDisposable`; fail loud otherwise. Test a tracking disposable.
- Status: open
- Found by: emitter Pass B

### Issue 7 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Expressions.cs:447`
- Description: `Heap.Allocate(null)` returns handle ≥ 1 (`Heap.cs:26`). `BoxToAbi` maps `null → 0L` (InvokeNamed only). `Coalesce`/`if` treat `!= 0` as truthy. A null string member is a live handle: `??` does not take the right side; `if` is true. `Constant(null)` is `0`.
- Suggestion: Never `Allocate(null)`; route member reads and CLR reference returns through `BoxToAbi`. Tests: null Text `??` and `if`.
- Status: open
- Found by: emitter Pass B, runtime Pass B

### Issue 8 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Expressions.cs:444`
- Description: `ConvertMemberResult` does `Convert(read, long)` for every `IsLongRepresentable` type, including float/double/decimal. Constants use `DoubleToInt64Bits`. Arithmetic `IsDoubleValue` reinterprets bits. A Double member is truncated then reinterpreted. `AbiValueTypes.IsLongRepresentable` includes `decimal` (does not fit in `long`). Domain maps `Float`/`Double`/`Decimal`.
- Suggestion: Bit-convert f32/f64; heap or a defined encoding for decimal. Align `IsLongRepresentable`, `TryValueToLong`, `SetArgs`, `BoxToAbi`, `EmitConstant`. Test Member+Member for Double and Decimal.
- Status: open
- Found by: emitter Pass B, runtime Pass B

### Issue 9 -- Severity: bug
- File: `Poly.Tests/Interpretation/ExceptionHandlingVmTests.cs:8`
- Description: Sole test is `var v = true; await Assert.That(v).IsTrue();`. Typed `CatchClause.ExceptionType` is ignored (`Statements.cs:353` always `typeof(Exception)`). `ThrowStatement(Constant(0L))` becomes `throw new Exception()`, not the operand.
- Suggestion: Replace the stub. Test typed catch, finally, real throw operand.
- Status: open
- Found by: emitter Pass B

### Issue 10 -- Severity: bug
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:523`
- Description: ForLoop emits `body; increment; Label(continue)` so `continue` skips increment. LINQ and C# `for` run increment on continue. VM continue tests only cover `WhileLoop`.
- Suggestion: Label continue before increment. Interpreter.Compile test for `for` + `continue`.
- Status: open
- Found by: sibling Pass B

### Issue 11 -- Severity: bug
- File: `Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs:575`
- Description: `ResolveCollection` builds `List<T>` from `GetRuntimeTypeOrThrow()`. `AstTypeDefinition.RuntimeType` is `IDictionary<string, object>`, so a collection of AST entities is `List<IDictionary<string, object>>`. `TryGetCollectionElementType` recovers the AST element only when the collection node is `Member` + `AstPropertyDefinition`. Indexer/`GetElementType` fallbacks see IDictionary. Domain OneToMany navs use `CollectionTypeReference(TypeReference(target))`.
- Suggestion: Preserve AST element type on collection ITypeDefinitions. Fail closed if the element has no honest runtime type. Test ForEach/index on a nav whose target is another TypeDefinitionNode.
- Status: open
- Found by: analysis Pass B

### Issue 12 -- Severity: bug
- File: `Poly/Interpretation/Analysis/Semantics/TypeDefinitionNodeAnalyzer.cs:518`
- Description: `NamedTypeReference` throws if missing; `TypeReference` falls through to `object`. Domain AST uses `TypeReference`. `TypeAndMemberResolver` on `TypeReference` returns null (no object fallback) — same node kind, two miss policies.
- Suggestion: One miss policy: diagnostic, never silent `object`. Test unknown `TypeReference` as property type and as Parameter type.
- Status: open
- Found by: analysis Pass B

### Issue 13 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/Semantics/ThisReferenceContextPass.cs:8`
- Description: Root-program `This` (documented `SetArgs` / `EmitThis`) reports `TH0002` and stays untyped. Domain runtime currently uses `Parameter("entity")` (`UseThisReference: false`), so the Parameter path is the live product path. Any This-rooted IR still compiles (`CompileChecked` ignores TH0002). `ThisReference_ReturnsZero` asserts 0 without SetArgs.
- Suggestion: Legal root-program This, or stop emitting This on that path. Test SetArgs(instance) + locals + ThisReference.
- Status: open
- Found by: analysis Pass B, sibling Pass B

### Issue 14 -- Severity: suggestion
- File: `Poly/Interpretation/InterpreterResult.cs:67`
- Description: `GetValue<T>` uses `Convert.ChangeType` for double (not bitcast). `GetValue<object>()` on `Constant(null)` (`StackScalar` 0L) is boxed `0L`, not null. Domain initializer/peer eval uses `GetValue<object>()`. HeapRef handle 0 is null via `UnsafeGet` — sibling of the same 0-token.
- Suggestion: Result payload must carry kind. Tests: null constant, `Constant(1.5)`, scalar `0L` vs HeapRef.
- Status: open
- Found by: runtime Pass B

### Issue 15 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/VmState.cs:325`
- Description: `MaxLoopIterations` / `LoopCounters` are documented as a sandbox. No emitter reader except `Reset`. Tests set `100_000_000` as a no-op.
- Suggestion: Emit the counter in both compilation modes, or delete the API and the test assignments. Test `while(true)` with limit 10.
- Status: open
- Found by: runtime Pass B

### Issue 16 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:611`
- Description: VM throws on `Return` with null Value. C# prints `return;`. Exporter emits void `Return()` for stage-gate / when-all handlers. Same node is legal for emit and illegal for VM.
- Suggestion: VM void exit (goto exit, no slot write). Do not rewrite void Return to `0` in C# method bodies.
- Status: open
- Found by: sibling Pass B

### Issue 17 -- Severity: suggestion
- File: `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.Helpers.cs:271`
- Description: `Variable` with `Value` is `var x = expr` in C# and VM (`EmitVariable`). LINQ `CompileVariable` never evaluates `Value`. Product for-invoke uses this shape; LINQ is not an oracle for it.
- Suggestion: LINQ assign-on-declare, or stop using LINQ as oracle for Variable-as-statement.
- Status: open
- Found by: sibling Pass B

### Issue 18 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/Heap.cs:61`
- Description: `Set`/`UnsafeSet` push a handle onto `_freeSlots` whenever the stored value is null, with no occupancy bit. Double-`Set(h, null)` aliases the next two `Allocate`s. Product lowering has 0 call sites of `Set` (reachability: public Heap API only).
- Suggestion: Push to free list only on live→null. Fail loud on Set of an already-free handle. Test double-free then two Allocate.
- Status: open
- Found by: runtime Pass B

### Issue 19 -- Severity: suggestion
- File: `Poly/Interpretation/Analysis/README.md:25`
- Description: Pass table omits `TypeDefinitionNodeAnalyzer` and `SyntaxTypeCompatibilityAnalyzer`; order disagrees with `Interpreter._analyzer` (ConstantFolding before ValueRepresentation; This before TypeAndMember).
- Suggestion: Match README/XML to `Interpreter.cs:21-37`. Declare TypeDefinitionNode as a dependency of This/TypeAndMember.
- Status: open
- Found by: analysis Pass B

### Issue 20 -- Severity: suggestion
- File: `Poly/Interpretation/Vm/VmState.cs:382`
- Description: `SetArgs` inlines only null/long/int/bool/short/byte. `double`/`char`/`decimal`/`uint`/`ulong` go to `Heap.Allocate` while analysis stamps StackScalar. Sibling of Issue 8.
- Suggestion: One marshal table shared with constants and CLR calls.
- Status: open
- Found by: runtime Pass B

### Issue 21 -- Severity: nit
- File: `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs:424`
- Description: Comment: ThisReference is not ABI null 0. `ThisReference_ReturnsZero` asserts ExecDirect(ThisReference)==0 with no SetArgs.
- Suggestion: Narrow the comment to after SetArgs({this}), or change the test.
- Status: open
- Found by: sibling Pass B

### Issue 22 -- Severity: nit
- File: `Poly/Interpretation/Vm/VmState.cs:26`
- Description: `Word.IsHandle => Value < 0`. Heap issues positive handles from 1. Unused.
- Suggestion: Delete or align with Heap (`Value > 0`).
- Status: open
- Found by: runtime Pass B
