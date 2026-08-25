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

- [x] **F13** — Root-program `ThisReference` is legal SetArgs slot 0 (no TH0002). Test SetArgs(instance)+locals+This.
- [x] **F14** — `InterpreterResult.GetValue<T>`: null payload, long→double/float bitcast, 0L stays 0L.
- [x] **F15** — `MaxLoopIterations` emitted in While/DoWhile/For/ForEach headers (`LoopTicks`). `-1` unlimited. Throws `"MaxLoopIterations exceeded."`
- [x] **F16** — Void `Return`: VM `Goto(ExitLabel)` with no slot write. C# method body `{ return; }` not `=> 0`.
- [x] **F17** — LINQ `CompileVariableUse` assigns `Variable.Value` on first encounter.
- [x] **F18** — `Heap.Set`/`UnsafeSet` recycle only live→null; already-free handle throws.
- [x] **F19** — `Analysis/README.md` pass order matches `Interpreter._analyzer` (incl. TypeDefinitionNode + SyntaxTypeCompatibility).
- [x] **F20** — `SetArgs`/`ToRing` marshal table aligned with `BoxToAbi` (incl. float/double bits).

## Process

- [x] **F21** — Gate recorded in `Analysis/README.md`: shipped Syntax node meaning is proven by `Interpreter.Compile` on the same tree; CFG/`BuildExpression`/LINQ alone is not the oracle.
- [x] **F22** — `EmitThis` comment: after SetArgs, This is that handle; unset slot 0 is ABI null 0. `Word.IsHandle => Value > 0`.

## Out of scope here

- Domain create/create-in EffectExecutor
- MCP `simulate_policy` session simulate (already labeled as bag oracle)
- Implementing host-ABI CallExternal
