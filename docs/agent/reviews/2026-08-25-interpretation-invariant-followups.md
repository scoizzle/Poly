# Interpretation invariant follow-ups — 2026-08-25

Review: [`2026-08-25-interpretation-invariant-sweep.md`](./2026-08-25-interpretation-invariant-sweep.md)

Owning stream: Interpretation only.

## Closed this change

- [x] Mixed string+number `Add` fail-closed at emit (not `UnsafeGet` of a scalar).
- [x] Lambda explicit-arg arity must match.
- [x] Nested lambda parameters are not outer captures.
- [x] Comment-only block is void; empty-string coalesce; non-nullable zero coalesce; PrimitiveTypeReference TypeCast; int−double promote.
- [x] **U2** — `FoldCoalesce` no longer treats `0L` as empty; `0` is a value for non-nullable coalesce (matches emit).
- [x] **U5** — `CompileFunctionBody` is the function-table entry for stored-lambda `Invoke(Variable)` / `Invoke(Parameter)`.
- [x] **U1** — `ulong` is 64-bit bitcast into the ring (`unchecked (long)` / `(ulong)`). `ulong.MaxValue` round-trips via `GetValue<ulong>()`.
- [x] **U3** — 0-arg `new` matches all-optional ctors and applies defaults; no matching ctor compile-rejects (no dummy `object[0]`). Value types with no ctor still `default(T)`.
- [x] **U4** — Closure-path `Parameter` lookup falls back to same name (stored lambda + inline).

- [x] Stored closures late-bind: analysis records free bindings; emit shares a heap `long[1]` cell (inline `Invoke(Lambda)` and `Invoke(fn)` agree).
- [x] Capture matrix: non-`long` ABI words, stored capture+args, stored parameter, loop/foreach last-value, re-invoke, declare-init, branch create.
- [x] `Invoke(Variable)` / `Invoke(Parameter)` take the assigned lambda body's value kind. No `UpvalueCell` wrapper. Sticky `Initializer` is a capture when the name is already in scope. Debugger presents `cell[0]` for captured slots.
- [x] `Assignment` / `Switch` / `TryCatchFinally` / valued `Return` value kinds on the **tested** siblings (root `Block`, immediate `Invoke(Lambda)`). See reopenings below.

## Open

- Nested frame-slot aliasing (inner/outer `FrameOffset` 0) still open.
- Declare-init as `Assignment` (~50 sites) parked.
- Capture-analysis review F1–F9 closed (siblings listed in [`2026-08-25-interpretation-capture-analysis-followups.md`](./2026-08-25-interpretation-capture-analysis-followups.md)). Nested `FrameOffset` 0 and declare-init-as-`Assignment` remain parked.

## Process

- [x] LINQ remains same-tree semantic checker (CORE / Interpretation README). Dual-oracle is the validation method; disagreements fix the VM or fail-closed.
