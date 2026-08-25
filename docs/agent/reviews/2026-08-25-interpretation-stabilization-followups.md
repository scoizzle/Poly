# Interpretation stabilization follow-ups — 2026-08-25

Review: [`2026-08-25-interpretation-stabilization-review.md`](./2026-08-25-interpretation-stabilization-review.md)

Owning stream: Interpretation (exclusive files under `Poly/Interpretation/` and `Poly.Tests/Interpretation/`). Do not mix with DomainModeling create/create-in.

## Bugs

- [x] **F1** — `CompileChecked` fails on every `DiagnosticSeverity.Error`.
- [x] **F2** — Unresolved Member read fail-closed at emit.
- [x] **F3** — Foreach items via `BoxToAbi` (int[] sums on VM).
- [x] **F4** — Foreach is IEnumerable + Dispose; null/non-enumerable throw.
- [x] **F5** — Switch runs only the matching body.
- [x] **F6** — `using` finally Disposes IDisposable.
- [x] **F7** — `Heap.Allocate(null)` returns handle 0.
- [x] **F8** — f32/f64 bitcast; decimal is heap (not `Convert` to long).
- [x] **F9** — Typed catch/finally tests; `CatchClause.ExceptionType` honored.
- [x] **F10** — ForLoop `continue` runs increment.
- [x] **F11** — AST entity collections keep element type (`AstCollectionTypeDefinition`).
- [x] **F12** — `TypeReference` miss throws; `void` still resolves.

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
