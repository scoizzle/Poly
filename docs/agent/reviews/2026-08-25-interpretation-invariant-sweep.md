# Interpretation invariant sweep — 2026-08-25 (post language-VM)

Target: `Poly/Interpretation/` + `Poly.Tests/Interpretation/` after ile-0/1/2 and VM↔LINQ parity fixes.  
Stance: assume wrong until a test names the invariant. LINQ is same-tree semantic checker, not a second language.

## Verdict

Canonical VM is stronger than the F1–F22 cut, but not fully a language VM. Dual-oracle proved several ABI holes; this sweep found more **uncodified** contracts and a few **fail-open** emit paths. Tests added in the same change for the highest-severity gaps.

## Issues

### Issue 1 — Severity: bug (emit fail-open)
- File: `DirectVmAbiEmitter.cs` `TryEmitStringConcat`
- Description: Concat fired if **either** operand was string; a number was `UnsafeGet` as a handle. Analyze usually rejects Text+Number; `Compile(node, analysis)` after a custom pipeline would not.
- Fix this sweep: both operands must be strings; mixed throws `VM compile rejected`.
- Test: `LanguageVmTests.Add_StringAndNumber_CompileRejected`

### Issue 2 — Severity: bug (fail-open)
- File: `DirectVmAbiEmitter.Invoke.cs` `EmitInvoke`
- Description: Lambda arity was not checked. Extra args: inline `IndexOutOfRange`; too few: unmapped params / garbage.
- Fix this sweep: explicit args must match parameter count (0-arg `SetArgs` still allowed).
- Test: `LanguageVmTests.Lambda_ArityMismatch_CompileRejected`

### Issue 3 — Severity: bug
- File: `FindCapturesRecursive`
- Description: Nested `Lambda.Parameters` with a **different name** were treated as outer free/captures. Nested tests all used `"x"` on both levels, hiding it.
- Fix this sweep: entering a nested `Lambda` treats its parameters as own; walk only its body.
- Test: `LanguageVmTests.NestedLambda_InnerParamDifferentName_CallsThrough`

### Issue 4 — Severity: suggestion (ABI)
- File: `TryValueToLong` `ulong`
- Description: `ulong` values `> long.MaxValue` cast to signed `-1`. `GetValue<ulong>` cannot round-trip `ulong.MaxValue`. Small values (`100UL+50`) pass.
- Status: open (not closed this sweep — needs a defined ulong ABI or compile-reject).

### Issue 5 — Severity: suggestion
- File: `EmitCoalesceValue` vs `ConstantFoldingPass.FoldCoalesce`
- Description: Fold treats **`long` `0L` only** as empty (`Coalesce_Zeroish_ReturnsRight` = 99). Emit keeps non-nullable `0` (`Coalesce_NonNullableZero_KeepsZero`). Nested `CompileValue` does not apply replacements.
- Status: fold vs emit disagreement for `0L ?? x` documented; `int` `0` now tested as keep-left.

### Issue 6 — Severity: nit
- File: `EmitNew`
- Description: 0-arg `new` on a type whose only ctor is all-optional does not apply defaults (`GetConstructor(EmptyTypes)`). Dummy `object[0]` if ctor unresolved (fail-open). Happy omit of one optional is tested.

## Codified this sweep

- Comment-only `Block` is void (`Comment_OnlyInBlock_IsVoid`)
- `"" ?? fallback` keeps `""`
- Non-nullable `0 ?? 99` keeps `0`
- `TypeCast` via `PrimitiveTypeReference(Int32)`
- `int - double` IEEE promote
- Empty-string / zero coalesce; mixed add; arity; nested lambda name

## Follow-ups

[`2026-08-25-interpretation-invariant-followups.md`](./2026-08-25-interpretation-invariant-followups.md)
