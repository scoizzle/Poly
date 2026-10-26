# TASK abi-scalar-null - one comparison protocol for optionals, null, and mixed numeric domains
Status: planned. Branch fix/330-abi-scalar-null. PR 331 → master. Implement mill: OpenCode (review on Grok).

## Constraints

- Scalars are never nullable. A scalar compared to null folds to a constant (`== null` false, `!= null` true); the scalar operand still runs its effects.
- A domain scalar gets a value at construction, from an argument or a default. Omitting a required scalar is an analysis error, never a zero or null word.
- An optional scalar is heap-resident under the VM null sentinel: absent is handle 0, present zero is a heap cell holding 0.
- The two-word ring and the in-word present/absent flag are not used. A ring-local nullable is allowed only when the present flag and the payload both fit one 64-bit Abi word; nothing here needs it.
- No packing into a sub-word float with spare bits. An optional `float`/`double` is a heap cell like every other optional.
- Equality is value equality, not JavaScript coercion: absent is not zero; a present zero equals zero of the same value; no operand is read as a handle and no handle is read as a number.
- This PR is Interpretation and targets master. The DomainModeling-only suite rule for #305 does not apply.

Derived invariants this plan makes mechanical:

- A node's slot kind comes from `ValueRepresentationClassifier.KindFor`: a nullable is a heap handle (`Poly/Interpretation/Vm/Abi/ValueRepresentation.cs:125-137`). Never call `RingCodec.IsInline` on a nullable to decide a slot; peel it and ask only whether the *underlying* value is numeric.
- A comparison never compares storage words or handles for a value. Both operands are decoded to CLR values and compared as values.
- Operands are emitted in source order, left then right. A root parameter's frame slot is assigned on first use (`Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs:756-767`), and `VmState.SetArgs` binds argument *i* to slot *i* by that order (`Poly/Interpretation/Vm/VmState.cs:97-107`). Any helper that emits the right operand first silently reorders the ABI.

## Failed approaches

| tip/SHA | finding ids | why wrong |
|---------|------------|-----------|
| `c9b89cf2` | R1 | `AreHeapValues` special-cased `StackScalar` vs the null literal and stayed on one word, so an optional present zero and absent were the same slot. Scot rejected the shape. |
| `0216b213` | R2 R3 R4 | Moved optionals to the heap, then routed every lifted operand through `long`: `decimal? 1.5m == 1.9m` answered true, and `DateTime?`/`Guid?`/`TimeSpan?` equality threw. Lifted arithmetic worked only when the nullable was on the left, and `bool` → `bool?` used `Convert(long, bool)`. |
| `11d0c760` + `fb43f265` + `49199f17` | R2 R3 R4 fixed, R5 opened | Each finding got its own `Try*` helper. `TryLiftedComparison` handed a heap-underlying nullable back to `AreHeapValues`, which then dereferenced the inline number as a handle: absent `decimal?` equalled 0 and present `0m` did not equal 0. |
| `14c5e8fa` | R5 fixed; R6 R7 open | `TryDecimalEquality` emits the decimal operand first (`AbiEmitter.Expressions.cs:264-265`), reordering root parameter slots (R6), and converts a float/double/ulong word with `Convert(long, decimal)` (`:284`), reading IEEE bits and a signed ulong as the number (R7). |
| pattern | — | Four rounds of per-finding helpers. Every helper owned a subset of shapes, so the next shape fell through into the previous helper's wrong assumption. This plan replaces the ladder with one operand/domain protocol. |

### Grug follow-ups, decided here

| follow-up | decision |
|-----------|----------|
| Ordering an optional decimal/DateTime/Guid/TimeSpan | **Fix.** The protocol unboxes, requires both present, and orders by value; absent is false. |
| Optional decimal arithmetic | **Keep fail-closed.** Emit still rejects with `arithmetic on an optional Decimal is not supported` (`AbiEmitter.cs:657-659`); pin it with a test. Comparison and arithmetic are separate contracts. |
| Optional ulong compared as signed | **Fix.** Decode the word through `ulong`; compare a mixed ulong/signed pair in decimal, which is the constant fold's rule. |
| `DateTime?` vs `DateTime` | **Keep fail-closed at analysis** (``comparison between incompatible types 'Nullable`1' and 'DateTime'``). The emitter protocol would handle it, but the compatibility rule rejects first; relaxing it is a separate slice. |
| `not` on `bool?` | **Keep rejected.** Three-valued logic has no `bool` result; `AbiEmitterNullScalarTests.OptionalScalar_LogicalNot_IsCompileRejected` (`:310`) already pins it. |

## Target representation

**Keep the heap null sentinel.** This PR changes no representation.

- An optional value type resolves to `Nullable<T>` (`Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs:207-211`) and a nullable node's slot is a heap handle (`ValueRepresentation.cs:125-137`). Absent is handle 0; present is a handle to the boxed underlying value, so present zero is a cell, not the sentinel (`RingCodec.BoxNullable`, `Poly/Interpretation/Vm/Abi/RingCodec.cs:244-252`). `SetArgs` boxes a nullable argument by the declared parameter type.
- Why not two-word: it changes the slot ABI in `SlotLocation`, the `long[]` value stack, frames, arrays, captures, call frames, and every producer artifact, for a shape the heap already carries correctly. No consumer asks for it.
- Why not an in-word flag: `long`/`double` fill the 64-bit payload, and the ruling already rejects packing a float with spare bits. A per-shape split (`bool?` inline, `long?` heap) would put two representations behind one protocol, which is the bug class this plan removes.
- One slot per value everywhere means the protocol has exactly one decode per shape.

Not touched: `KindFor`, `UnderlyingKindFor`, `SlotTypeOf`, `TryInlineConstant`, `ToRing`, `FromClr`/`ToClr`, `BoxNullable`, arrays, captures, frames.

## Equality / compare protocol

All comparison meaning moves into `CompileCompareTest` in a new `AbiEmitter.Compare.cs`. `Equal` and `NotEqual` build one equality test, and `NotEqual` is `Not(equality)`. Ordering uses the domain's compare. No per-shape `Try*` helper remains.

### 1. Decide shapes, emit nothing

`CompareOperandOf(node, ctx)` reads `ctx.RepresentationOf(node)` and returns `(Shape, ClrType, Underlying?)`:

| shape | when | CLR type |
|-------|------|----------|
| `Null` | `Constant { Value: null }` | — |
| `Inline` | `Kind == StackScalar` | the resolved type: `int`, `long`, `ulong`, `float`, `double`, `bool`, `char`, enum |
| `HeapValue` | `Kind == HeapRef`, `Clr.IsValueType`, not nullable | `decimal`, `DateTime`, `Guid`, `TimeSpan`, `DateOnly`, `TimeOnly`, or an enum constant (the constant encoder boxes enums) |
| `Optional` | `Kind == HeapRef`, `Nullable.GetUnderlyingType(Clr) != null` | the underlying type |
| `Reference` | `Kind == HeapRef`, reference type | `string`, entity, `object` |

`Kind == Unknown` or a null CLR type fails closed: `VM compile rejected: cannot compare '<node>' because its type did not resolve.` This loses nothing that is exercised: with the tip instrumented to throw on an `Unknown` comparison, **0 of 4636** suite comparisons reached the fallback. `AreHeapValues` and its Member heuristic (`AbiEmitter.Expressions.cs:311-331`) and `HeapValueToObject` are deleted with it.

### 2. Fold null first

When one side is `Constant { Value: null }` — no relational operator accepts null; if one reaches, fail closed:

| other shape | answer | emitted |
|-------------|--------|---------|
| `Optional` | `==` is absent, `!=` is present | the one handle, bound once |
| `Reference` | `handle == 0` / `!= 0` | the one handle |
| `Inline` or `HeapValue` | constant `false` / `true` | `Block(ValueOf(other), Constant(...))`; effects still run |
| `Null` (both sides) | `true` / `false` | constant |

This extends today's scalar-only fold (`AbiEmitter.Expressions.cs:114-127`) to every non-optional value. It fixes `required decimal == null` on an unseeded handle, which answers **true** at the tip (probe: `req dec == null unseeded=1`).

### 3. Read both operands, left then right, each once

```
var leftRaw  = ValueOf(left, ctx);    // emitted first: parameter slots follow source order
var rightRaw = ValueOf(right, ctx);
var l = Variable(leftRaw.Type, "_cmpL");
var r = Variable(rightRaw.Type, "_cmpR");
return Block([l, r], Assign(l, leftRaw), Assign(r, rightRaw), test(l, r));
```

Why this is structural, not incidental: `TryDecimalEquality` calls `ValueOf(decimalNode)` before `ValueOf(otherNode)`. `EmitParameter` resolves the parameter on first use (`AbiEmitter.Statements.cs:651-660`), `Context.DeclareParameter` gives it the next frame slot (`AbiEmitter.Context.cs:756-768`), and `SetArgs` binds argument *i* to slot *i*, so the decimal parameter takes the int argument's frame slot and `int == decimal` dereferences an integer as a handle. Locals do not show the bug: the tip answers `local int==dec=0` correctly, while `int==dec` throws `NullReferenceException`. Shapes and domains are decided without emitting, then exactly two `ValueOf` calls run in source order.

### 4. Pick one domain

`CompareDomainOf(left, right)` mirrors the constant-folding oracle, because a variable must answer what the same comparison written as literals answers (`Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs:698-731`):

| operands | domain | compare |
|----------|--------|---------|
| both `bool` (including one optional) | `Boolean` | `cf(l, r)`; ordering is rejected by analysis |
| either is `float`/`double` | `double` | `Convert(value, double)`, then `cf` |
| otherwise, either is `decimal` | `decimal` | `Convert(value, decimal)`, then `cf` |
| otherwise, either is `ulong` and the other is unsigned/`ulong` | `ulong` | `Convert(value, ulong)`, then `cf` |
| otherwise, either is `ulong` and the other is signed | `decimal` | `Convert(value, decimal)`, then `cf` (the fold uses `TryConvertToDecimal`; `-1 == ulong.MaxValue` is false, not true) |
| all integral/`char`, or both the same enum type | `long` | `Convert(value, long)`, then `cf` |
| both the same non-numeric value type | `Value` | equality `object.Equals(box(l), box(r))`; ordering `VmHeapComparison.Compare(box(l), box(r))` |
| both references | `Reference` | equality `object.Equals(l, r)`; ordering `VmHeapComparison.Compare(l, r)` |
| anything else | — | fail closed: `comparison between '<X>' and '<Y>' has no common value` |

Rule order matters: `decimal` vs `double` is `double` (the fold checks IEEE first), not the arithmetic rank's `decimal`. Say so in the function's comment; the next reader will otherwise "fix" it.

### 5. Decode one value per operand

- `Inline`: `ScalarValue(word, Clr)` (`RingCodec.cs:291-296`, made internal) is the one word-to-CLR decoder: IEEE via `FromIeee`, `bool` via `!= 0`, everything else one `Convert`. Normalize with `ToWord` first (skip it when the local's own type already is `Clr`), so a `double` constant (typed slot) and a `double` parameter (word) take the same path. `ulong` is `Convert(long, ulong)`, an unchecked bitcast, so `ulong.MaxValue` survives.
- `HeapValue`: `RequiredValue(heap, handle, Clr)`, a new runtime helper next to `UnboxNullableAs` (`RingCodec.cs:364-369`): handle 0 throws `VM: required <type> value is absent`; otherwise `ChangeType` to the declared type. A required decimal is never silently 0.
- `Optional`: bind the handle once; `present = handle != 0`; `value = present ? Unbox(handle, Underlying) : default(Underlying)`.
- `Reference`: `value = handle == 0 ? null : Unbox(handle)`.

Then one `Convert` to the domain type. `Boolean` values are already `bool` and need no conversion; every numeric value is already at its declared CLR type, so the conversion is the single widening to the domain.

### 6. Presence, then the answer

- Equality: `(bothPresent && valuesEqual) || (bothAbsent)`. With one optional side: `present && valuesEqual`. With none: `valuesEqual`.
- `NotEqual` is `Not(equality)`, never a second path.
- Ordering: `bothPresent && valuesOrder`; absent is always false. With no optional side: `valuesOrder`.

That is C#'s lifted rule: absent is false for ordering, equal only to absent, and present zero is a value.

### 7. Never

- `Convert(long, decimal)` on an IEEE word or a `ulong` word (R7).
- Dereference an inline word as a handle, or compare a handle to a number (R5).
- `Convert(long, bool)`; decode a bool as `!= 0` (R4).
- Route a nullable with a non-inline underlying through `long` (R2).
- Emit an operand out of source order (R6).
- Add a `Try*Comparison` for one shape. Add a domain row and a test instead.

## Shape matrix

Before values are measured at `14c5e8fa` with root parameters passed to `SetArgs` in source order (frozen probe, 2026-10-26). Every "before" that is wrong becomes a test that is watched red before the fix.

| input kind | before @ `14c5e8fa` | after | test that proves it |
|------------|---------------------|-------|---------------------|
| `long` vs `null` | false/true, folded, no heap deref (R1 closed) | same | `AbiEmitterNullScalarTests.RequiredScalar_*` |
| `decimal` vs `null`, unseeded | **true** (handle 0 read as null) | false, folded, no heap deref | `RequiredDecimal_EqualNull_Unseeded_IsFalse`, `RequiredDecimal_EqualNull_EmitsNoHeapDeref` |
| `long? != null`, present 0 / absent | true / false | same | `OptionalScalar_PresentZero_NotEqualNull_IsTrue`, `OptionalScalar_Absent_NotEqualNull_IsFalse` |
| `int == decimal` (params, 4 / 4.5m) | throws `NullReferenceException` | false | `IntegerParameter_Left_DecimalParameter_Right_DifferentValues_IsFalse` |
| `int == decimal` (4 / 4.0m) | throws | true | `IntegerParameter_Left_DecimalParameter_Right_SameValue_IsTrue` |
| `decimal 4.5 == int 4` (decimal left) | false (correct by accident of order) | false | `DecimalParameter_Left_IntegerParameter_Right_UsesValues` |
| `int? 4 == decimal? 4.5m` | **true** | false | `OptionalInt_Left_OptionalDecimal_Right_DifferentValues_IsFalse` |
| `int? 4 == decimal? 4.0m` | true | true | `OptionalInt_Left_OptionalDecimal_Right_SameValue_IsTrue` |
| two absent optionals | true | true | `OptionalInt_Left_OptionalDecimal_Right_BothAbsent_IsTrue` |
| left present, right absent | false | false | `..._LeftAbsent_IsFalse` |
| `decimal 1.5 == double 1.5` | **false** (R7) | true | `DecimalParameter_Equal_DoubleParameter_UsesValue` |
| `decimal 1.5 == float 1.5f` | **false** | true | `DecimalParameter_Equal_FloatParameter_UsesValue` |
| `decimal 1.5 == double 1.9` | false | false | `DecimalParameter_Equal_DoubleParameter_DifferentValues_IsFalse` |
| `decimal 5 == ulong 5` | true | true | `DecimalParameter_Equal_ULongParameter_SameValue_IsTrue` |
| `(decimal)ulong.MaxValue == ulong.MaxValue` | **false** | true | `DecimalParameter_Equal_ULongMax_UsesUnsignedValue` |
| same, literal on the right | **false** (fold is true) | true | `DecimalParameter_Equal_ULongMax_MatchesFoldedConstant` |
| `ulong.MaxValue > 1` | **false** (signed compare) | true | `ULongParameter_GreaterThan_One_IsTrue` |
| `-1 == ulong.MaxValue` | **true** (absurd) | false | `ULongParameter_Equal_SignedMinusOne_IsFalse` |
| `ulong? MaxValue > 1` | **false** | true | `OptionalULong_GreaterThan_One_IsTrue` |
| `5.5m < 5` (required decimal vs inline) | **true** (handle compare) | false | `DecimalParameter_LessThan_InlineFive_IsFalse` |
| `5.5m > 5` | **false** | true | `DecimalParameter_GreaterThan_InlineFive_IsTrue` |
| `decimal? 5.5 < decimal? 6` | throws "ordering comparison of an optional heap value is not supported" | true | `OptionalDecimal_Ordering_Present_UsesValues` |
| `decimal? < decimal?`, absent | throws | false | `OptionalDecimal_Ordering_Absent_IsFalse` |
| `DateTime? < DateTime?`, present / absent | throws / throws | true / false | `OptionalDateTime_Ordering_Present_UsesValues`, `..._Absent_IsFalse` |
| `string == string`, equal / different / null | true / false / sentinel | same | `String_Equal_UsesValue`, existing string tests |
| `bool? == true`, present / absent | true / false | same | `AbiEmitterNullScalarTests.OptionalScalar_Bool_*` |
| `int? 4 == 4L` | true | true | `OptionalInt_Equal_Long_UsesValue` |
| side effects in a mixed decimal comparison | right (decimal) runs first | left runs first | `MixedDecimal_Comparison_EvaluatesLeftThenRight` |
| `DateTime? == DateTime` | analysis reject | analysis reject (pinned) | `DateTimeOptional_Equal_RequiredDateTime_IsRejected` |
| `decimal? + 1m` | emit reject | emit reject (pinned) | `OptionalDecimal_Arithmetic_IsCompileRejected` |
| unseeded `decimal == decimal` | **true** (two null handles) | throws `required Decimal value is absent` | `RequiredDecimal_Unseeded_Equality_Throws` |

## Files

Product:

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Compare.cs` — new partial: `CompileCompareTest`, `EmitComparisonValue`, `EmitComparison`, `FoldNullComparison`, `CompareOperandOf`, `CompareDomainOf`, `ReadCompareOperand`, `CompareValues`, the domain table and its comment.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Expressions.cs` — remove `CompileCompareTest`'s body and the comparison-only helpers (`HeapValueToObject`, `FoldScalarNullTest`, `IsNonNullableHeap`, `TryLiftedComparison`, `TryDecimalEquality`, `AreHeapValues`). Keep `NullableUnderlyingOf`, `NullablePresent`, `UnboxNullable`, `LiftedOperand`, `IsIeeeType`, `RejectNullableLogical` and the arithmetic/unary/logical arms.
- `Poly/Interpretation/Vm/Abi/RingCodec.cs` — make `ScalarValue` internal (the one word-to-CLR decoder) and add `RequiredValue(heap, handle, type)` next to `UnboxNullableAs`.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` — add the `RequiredValue` `MethodInfo` beside `UnboxNullableAsInfo`.

Tests:

- `Poly.Tests/Interpretation/Vm/Abi/AbiEmitterCompareDomainTests.cs` — new; the shape matrix rows, `RequiredValue` failure, and the evaluation-order probe.
- `Poly.Tests/Interpretation/Vm/Abi/AbiEmitterOptionalArithmeticTests.cs` — add `OptionalDecimal_Arithmetic_IsCompileRejected`.
- Existing nets stay unchanged: `AbiEmitterOptionalHeapNullableCompareTests.cs` (R2/R5), `AbiEmitterNullScalarTests.cs` (R1/R4 fold), `AbiEmitterOptionalScalarTests.cs` (construction), `AbiEmitterOptionalBoolAssignTests.cs` (R4).

Docs (same change):

- `Poly/Interpretation/Vm/README.md` — file-map row for `Abi/AbiEmitter.Compare.cs`; one paragraph: a comparison reads values, never slots, and the domain follows the fold.
- `docs/interpretation/vm-abi-reference.md` — correct the representation table's "Value types" row (inline primitives are words; `decimal`/`DateTime`/… and every nullable are handles) and add a short "Comparisons" subsection.
- `docs/plans/poly-eng/tasks/abi-scalar-null.md` — this file.
- `docs/CORE.md` — no change; it only points at the emitter.

## Done when

1. The R6 rows pass: `int == decimal` (both parameters, either order) uses the values and does not throw; `int? 4 == decimal? 4.5m` is false, `4 == 4.0m` is true, both absent is true, one absent is false.
2. The R7 rows pass: `decimal 1.5 == double 1.5` and `== float 1.5f` are true; `(decimal)ulong.MaxValue == ulong.MaxValue` is true for a parameter, a literal, and a fold; `ulong.MaxValue > 1` is true; `-1 == ulong.MaxValue` is false.
3. Required decimal ordering against an inline number and optional decimal/DateTime ordering use the values; absent ordering is false.
4. `decimal?`/`DateTime?`/`Guid?`/`TimeSpan?` equality still uses values (R2), and a required scalar vs null still folds with no heap deref (R1).
5. `TryDecimalEquality`, `TryLiftedComparison`, `AreHeapValues`, `IsNonNullableHeap`, `HeapValueToObject` are gone; one protocol decides shapes, one chooses a domain, and exactly two ordered `ValueOf` calls emit the operands.
6. `Unknown` comparison operands fail closed (measured: 0/4636 at the tip).
7. `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` is 0 warnings / 0 errors; the full suite is green on the tip with the count recorded in the PR comment.
8. No test is deleted, skipped, or weakened; the existing optional/heap tests pass unchanged.
9. Comments and XML docs state only what the code does; the protocol fits in one file and reads as a table.

## SHIP if / NOT SHIP if

**SHIP if**

- Every "after" in the shape matrix holds on the tip, and each wrong "before" was watched red first with the command and output recorded in the PR comment.
- The old comparison helpers are gone and no new `Try*` comparison helper exists.
- The build is clean, the changed-area filters pass, the full suite passes, and CI is green on the reviewed SHA.
- Grug has no blocker, major, or suggestion open, and the hand-edit gate is yes.

**NOT SHIP if**

- R6 or R7 is only correct for constants, or only when the decimal is the first-read parameter.
- Any operand is still compared as a handle, a word, or a `long` conversion of an IEEE/ulong value.
- A lifted operation routes a non-inline underlying through `long` (R2), a bool is converted with `Convert(long, bool)` (R4), or a scalar-vs-null test emits a heap deref (R1).
- Disproving a finding required a new special case for that one shape.
- A test was deleted, skipped, or weakened, or the full suite was not run at the pre-ship gate.

## Tests

Changed-area, per class (TUnit; run from the worktree root):

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AbiEmitterCompareDomainTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AbiEmitterOptionalHeapNullableCompareTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AbiEmitterNullScalarTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AbiEmitterOptionalScalarTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AbiEmitterOptionalArithmeticTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AbiEmitterOptionalBoolAssignTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DirectVmAbiEmitterTests/*'
```

Build and full suite (parent runs the suite at the pre-ship gate):

```
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Baseline at `14c5e8fa`: 4636 passed, 0 failed, 0 skipped. The new file adds cases; record the new total.

## Hand-edit

- The whole protocol is one file with one shape switch, one domain table, and one decode function per storage kind. Scot can add a shape or a domain row without touching emission.
- The evaluation order is two adjacent `ValueOf` calls in one function; there is no helper that can quietly reorder them.
- `RingCodec.ScalarValue` is the single word-to-CLR reader and `RequiredValue` is the single heap-value reader. A misread word has one place to fix.
- The comments name the ruling behind each rule (heap sentinel, fold, source order) and cite the oracle; no comment describes a path the code does not take.
- No reflection at emit beyond the existing `Ref` lookups, no new `#region`, no agent paperwork in product paths.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-26 | planner | opencode/opencode-go/deepseek-v4.1-flash | 14c5e8fa | planned | R6 R7 open; R1–R5 lessons |
