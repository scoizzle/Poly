# Interpretation stabilization follow-ups — 2026-08-25

Review: [`2026-08-25-interpretation-stabilization-review.md`](./2026-08-25-interpretation-stabilization-review.md)

Owning stream: Interpretation (exclusive files under `Poly/Interpretation/` and `Poly.Tests/Interpretation/`). Do not mix with DomainModeling create/create-in.

## Bugs

- [ ] **F1** — `Interpreter.cs:89` — `CompileChecked` fail on every `DiagnosticSeverity.Error`, not only `VmTypeCompatibility`. Stop domain runtime from using lenient `Compile`. Test: misspelled member on a typed entity throws.
- [ ] **F2** — `DirectVmAbiEmitter.Expressions.cs:392` — Unresolved Member **read** must fail closed (same as assignment). Test: strip/misspell member, assert throw not instance handle.
- [ ] **F3** — `DirectVmAbiEmitter.Statements.cs:567` + `ForEachLoopTests.cs:40` — Foreach item ABI: stack scalar for long-representable elements, handle for refs. Drive existing int[] / continue / break trees through `Interpreter.Compile`.
- [ ] **F4** — `DirectVmAbiEmitter.Statements.cs:584` — One foreach collection contract (IEnumerable+Dispose vs IList-only). Either VM matches C# or analyze rejects non-IList. Tests: null, HashSet.
- [ ] **F5** — `DirectVmAbiEmitter.Statements.cs:275` — Switch must not execute non-taken bodies. Test side-effecting cases.
- [ ] **F6** — `DirectVmAbiEmitter.Statements.cs:602` — `using` finally must Dispose. Test tracking disposable.
- [ ] **F7** — `DirectVmAbiEmitter.Expressions.cs:447` + `Heap.cs` — Never `Allocate(null)`; CLR/member null is handle 0 (`BoxToAbi`). Tests: null Text `??` and `if`.
- [ ] **F8** — `DirectVmAbiEmitter.Expressions.cs:444` + `AbiValueTypes.cs:17` — One numeric ABI: bitcast f32/f64; decimal not `Convert` to long. Align `SetArgs` / `BoxToAbi` / `EmitConstant` / `IsLongRepresentable`.
- [ ] **F9** — `ExceptionHandlingVmTests.cs:8` — Replace constant-true stub. Honor `CatchClause.ExceptionType`. Test typed catch/finally/throw operand.
- [ ] **F10** — `DirectVmAbiEmitter.Statements.cs:523` — ForLoop `continue` must run increment (match C#/LINQ). Interpreter.Compile test.
- [ ] **F11** — `TypeDefinitionNodeAnalyzer.cs` `ResolveCollection` — Preserve AST element type; do not collapse entity collections to `List<IDictionary>`.
- [ ] **F12** — `AstTypeReferenceResolver` — `TypeReference` miss must not silently become `object`; align with `NamedTypeReference` and `TypeAndMemberResolver`.

## Suggestions

- [ ] **F13** — Root-program `ThisReference`: legal SetArgs this, or do not emit This on that path. Test SetArgs(instance)+locals+This.
- [ ] **F14** — `InterpreterResult.GetValue<T>`: kind-aware (null vs 0L vs double bits).
- [ ] **F15** — `MaxLoopIterations`: emit checks or delete the API.
- [ ] **F16** — Void `Return`: VM exit without value; C# must not rewrite to `0`.
- [ ] **F17** — LINQ `CompileVariable` must honor `Variable.Value`, or LINQ is not an oracle for Variable-as-statement.
- [ ] **F18** — `Heap.Set` free-list occupancy (public API; no product caller today).
- [ ] **F19** — `Analysis/README.md` pass order vs `Interpreter._analyzer`.
- [ ] **F20** — `SetArgs` marshal table (sibling of F8).

## Process

- [ ] **F21** — Gate: Interpretation control-flow tests that only `BuildExpression()` or CFG-analyze must not be the sole oracle for a shipped Syntax node. Prefer `Interpreter.Compile` on the same tree.
- [ ] **F22** — Nits F21/F22 in the review (ThisReference comment vs `ThisReference_ReturnsZero`; `Word.IsHandle` negative vs Heap positive).

## Out of scope here

- Domain create/create-in EffectExecutor
- MCP `simulate_policy` session simulate (already labeled as bag oracle)
- Implementing host-ABI CallExternal
