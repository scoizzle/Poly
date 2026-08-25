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

## Open

- [ ] **U1** — `ulong` values that do not fit in signed `long` (`ulong.MaxValue`): define ABI (heap vs reject) and test.
- [ ] **U3** — `new` all-optional ctor with 0 args; unresolved ctor fail-closed (no dummy `object[0]`).
- [ ] **U4** — Closure-path `Parameter` identity vs same-name different instance (fuzz hides via inline).

## Process

- [x] LINQ remains same-tree semantic checker (CORE / Interpretation README). Dual-oracle is the validation method; disagreements fix the VM or fail-closed.
