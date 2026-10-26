# TASK abi-scalar-null - one comparison protocol for optionals, null, and mixed numeric domains
Status: revised after PLAN-REVIEW REVISE (P1 P2 P3; Scot C# nullable ruling). Branch fix/330-abi-scalar-null. PR 331 → master. Implement mill: OpenCode (review on Grok).

## Constraints

- Scalars are never nullable. A scalar compared to null folds to a constant (`== null` false, `!= null` true); the scalar operand still runs its effects.
- A domain scalar gets a value at construction, from an argument or a default. Omitting a required scalar is an analysis error, never a zero or null word.
- An optional scalar's frame slot is heap-resident under the VM null sentinel: absent is handle 0, present zero is a heap cell holding 0. The expression local and a register are `Nullable<T>`. Box and unbox only where a `long` frame, capture cell, call argument, debug snapshot, or return has to hold the value.
- The two-word frame is not used. `decimal?`, `DateTime?`, and `Guid?` do not fit in two longs, and a second slot would break "argument i is slot i".
- No packing into a sub-word float with spare bits. An optional `float`/`double` frame slot is a heap cell like every other optional.
- Equality compares decoded values, not storage: no operand is read as a handle and no handle is read as a number. There is no JavaScript-style coercion.
- **Equality follows C# nullable semantics.** Operators lift: `null == null` is true, an absent operand never equals a present value (zero included), a present zero equals zero of the same value, and ordering with an absent operand is false.
- **Mixed numeric comparisons follow C# binary numeric promotion plus the nullable lift** (`int`/`uint`/`ulong`/`byte`/`short`/`char`/`float`/`double`/`decimal`). The constant fold uses that same promotion. Where C# has no operator for a pair the analysis admits (`decimal` vs `float`/`double`; `ulong` vs a signed type), §4 states the exact rule and its precision.
- **An enum operand is its underlying integer.** `TryInlineConstant`, `ToRing`, and `FromClr` store `RingCodec.EnumWord` (a `ulong` underlying is the unchecked bitcast). Comparison converts that word back to the enum. An optional enum stays a heap box.
- **A `Constant { Value: null }` is shape `Null` before any type check.** `Classify` gives it `HeapRef` with a null CLR type (`Poly/Interpretation/Vm/Abi/ValueRepresentation.cs:55-57`), so a "null CLR type fails closed" check placed first would reject every `== null`, including R1.
- This PR is Interpretation and targets master. The DomainModeling-only suite rule for #305 does not apply.

Derived invariants this plan makes mechanical:

- A node's frame-slot kind comes from `ValueRepresentationClassifier.KindFor`: a nullable is a heap handle (`Poly/Interpretation/Vm/Abi/ValueRepresentation.cs:125-137`). Never call `RingCodec.IsInline` on a nullable to decide that frame slot; peel it and ask only whether the *underlying* value is numeric. The expression local is `Nullable<T>` (`RingCodec.SlotTypeOf`), which is not the frame kind. An enum's slot agrees with `KindFor`: both are the underlying integer (`RingCodec.EnumWord`). Its shape still comes from `Clr.IsEnum`, before the `StackScalar` arm, so the decoder converts the word back to the enum. An optional enum's frame slot is a heap handle; its local is `Nullable<enum>`.
- A null constant is matched as shape `Null` before the type check: it has no CLR type by design (`ValueRepresentation.cs:55-57`).
- A comparison never compares storage words or handles for a value. Both operands are decoded to CLR values and compared as values.
- Operands are emitted in source order, left then right. A root parameter's frame slot is assigned on first use (`Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs:756-767`), and `VmState.SetArgs` binds argument *i* to slot *i* by that order (`Poly/Interpretation/Vm/VmState.cs:97-107`). Any helper that emits the right operand first silently reorders the ABI.

## Failed approaches

| tip/SHA | finding ids | why wrong |
|---------|------------|-----------|
| `c9b89cf2` | R1 | `AreHeapValues` special-cased `StackScalar` vs the null literal and stayed on one word, so an optional present zero and absent were the same slot. Scot rejected the shape. |
| `0216b213` | R2 R3 R4 | Moved optionals to the heap, then routed every lifted operand through `long`: `decimal? 1.5m == 1.9m` answered true, and `DateTime?`/`Guid?`/`TimeSpan?` equality threw. Lifted arithmetic worked only when the nullable was on the left, and `bool` → `bool?` used `Convert(long, bool)`. |
| `11d0c760` + `fb43f265` + `49199f17` | R2 R3 R4 fixed, R5 opened | Each finding got its own `Try*` helper. `TryLiftedComparison` handed a heap-underlying nullable back to `AreHeapValues`, which then dereferenced the inline number as a handle: absent `decimal?` equalled 0 and present `0m` did not equal 0. |
| `14c5e8fa` | R5 fixed; R6 R7 open | `TryDecimalEquality` emits the decimal operand first (`AbiEmitter.Expressions.cs:264-265`), reordering root parameter slots (R6), and converts a float/double/ulong word with `Convert(long, decimal)` (`:284`), reading IEEE bits and a signed ulong as the number (R7). |
| `417b174e` + PLAN-REVIEW | P1 P2 P3 | The domain table claimed to mirror the fold's three steps but sent every float/double pair to `double`, so `2u == 2.0` read true where the fold says false, and one row `Convert`ed `ulong` to `long` (the R7 signed read again). An enum parameter was classified `Inline` although `ToRing` boxes it. `Null` was matched after the null-CLR failure, so `== null` could no longer fold. |
| `6061cadf` + `55da516d` | review: R6 fixed, R1-R5 pins held | The helpers read an operand's *generating code* as its value: `EmitLiftedArithmetic` and `TryLiftedComparison` spliced it into three tree positions, so a nullable call operand re-ran and crashed (`RingCodec.UnboxNullableAs` read the receiver as a handle). `EmitReturn` left a scalar body value unboxed for a nullable-returning method, which the same read then dereferenced. `TryDecimalEquality` emitted by type instead of source order, crossing root parameter slots (R6). `PromoteNumeric` widened only `char`, so `byte? + byte?` truncated to its own type. |
| pattern | — | Four rounds of per-finding helpers. Every helper owned a subset of shapes, so the next shape fell through into the previous helper's wrong assumption. This plan replaces the ladder with one operand/domain protocol. |

### Grug follow-ups, decided here

| follow-up | decision |
|-----------|----------|
| Ordering an optional decimal/DateTime/Guid/TimeSpan | **Fix.** The lifted CLR operator orders by value; an absent operand is false. |
| Optional decimal arithmetic | **Closed.** The emitter runs the lifted operator on `Nullable<decimal>`. Analysis does not rewrite that node to `Decimal.Add`, which would drop the absent case. |
| Optional ulong compared as signed | **Fix.** Decode the word through `ulong`; compare a mixed ulong/signed pair in decimal, which is exact (C# has no such operator, and `FloatingCompare` throws rather than folds). |
| Mixed `uint`/`ulong`/`short`/`byte`/`sbyte`/`ushort` vs `float`/`double` | **Closed.** Runtime and fold both follow C# promotion, including promoting an integral to `float`. |
| `DateTime?` vs `DateTime` | **Keep fail-closed at analysis** (``comparison between incompatible types 'Nullable`1' and 'DateTime'``). The emitter protocol would handle it, but the compatibility rule rejects first; relaxing it is a separate slice. |
| `not` on `bool?` | **Keep rejected.** Three-valued logic has no `bool` result; `AbiEmitterNullScalarTests.OptionalScalar_LogicalNot_IsCompileRejected` (`:310`) already pins it. |

## Target representation

**The frame keeps the heap null sentinel.** The expression local does not.

- An optional value type resolves to `Nullable<T>` (`Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs:207-211`). `KindFor` still calls that a heap handle, and that is the frame slot: absent is handle 0, present is a boxed cell (`RingCodec.BoxNullable`). `SetArgs` boxes at the entry boundary.
- A ring local and a register hold `Nullable<T>`. Compare, arithmetic, unary, and coalesce emit the lifted CLR operator on that local. They do not rebuild a present flag.
- The frame boxes where a `long` has to hold the value: the debug snapshot (`EmitScopeStores` and the block-exit copy), return and the root result, a call argument's word slot, and a capture cell. A parameter and a call result unbox once into the local.
- **Enums are the underlying integer.** `KindFor` calls an enum `StackScalar`, and `TryInlineConstant`, `ToRing`, and `FromClr` store `RingCodec.EnumWord`. Comparison decodes that word. An optional enum's frame slot stays a heap box; its local is `Nullable<enum>`.
- Why not two-word: it changes the slot ABI in `SlotLocation`, the `long[]` value stack, frames, arrays, captures, call frames, and every producer artifact. `decimal?`, `DateTime?`, and `Guid?` do not fit in two longs. No consumer asks for it.
- Why not an in-word flag: `long`/`double` fill the 64-bit payload, and the ruling already rejects packing a float with spare bits. A per-shape split (`bool?` inline, `long?` heap) would put two representations behind one protocol, which is the bug class this plan removes.
- Why not a hand-written lift over the handle: that was a second numeric type system beside the CLR operator, which already implements the lift.

Not touched: `KindFor` (a nullable frame slot stays `HeapRef`), arrays, and `VmHeapComparison`. `SlotTypeOf` returns `Nullable<T>` for a nullable local. The closed enum follow-up writes `EnumWord` from `TryInlineConstant`, `ToRing`, and `FromClr`. `ToClr` converts that word back to the enum (`Expression.Convert` from the long word).

## Equality / compare protocol

All comparison meaning moves into `CompileCompareTest` in a new `AbiEmitter.Compare.cs`. `Equal` and `NotEqual` build one equality test, and `NotEqual` is `Not(equality)`. Ordering uses the domain's compare. No per-shape `Try*` helper remains.

### 1. Decide shapes, emit nothing

`CompareOperandOf(node, ctx)` reads `ctx.RepresentationOf(node)` and returns `(Shape, ClrType, Underlying?)`. The order shown is the match order:

| shape | when | CLR type |
|-------|------|----------|
| `Null` | `Constant { Value: null }` — **before any type check** (`Classify` returns `HeapRef` with a null CLR type, `ValueRepresentation.cs:55-57`) | — |
| `Optional` | `Nullable.GetUnderlyingType(Clr) != null` (including `enum?`) | the underlying type |
| `Enum` | `Clr.IsEnum`. The slot is the underlying integer (`RingCodec.EnumWord`): a constant, a `ToRing` parameter, and a `FromClr` result write the same word. A `ulong` underlying uses the unchecked bitcast | the enum type |
| `Inline` | `Kind == StackScalar` (numeric, `bool`, `char`; not enum), or `Kind == Void` with CLR `void` (a resolved void call; `EmitResult` writes `0L`, so the decoded type is `long`) | the resolved type, or `long` for void |
| `HeapValue` | `Kind == HeapRef`, `Clr.IsValueType`, not nullable, not enum | `decimal`, `DateTime`, `Guid`, `TimeSpan`, `DateOnly`, `TimeOnly` |
| `Reference` | `Kind == HeapRef`, reference type | `string`, entity, `object` |

Only after `Null` is matched: `Kind == Unknown` or a null CLR type fails closed: `VM compile rejected: cannot compare '<node>' because its type did not resolve.` This loses nothing that is exercised: with the tip instrumented to throw on an `Unknown` comparison, **0 of 4636** suite comparisons reached the fallback. A resolved void call is not that case. `Classify` returns `Void` and `typeof(void)`, and `EmitResult` has already written `0L`, so the operand is `Inline` `long`. `DirectVmAbiEmitterTests.VoidClrInvoke_LeavesZero_ForTheNextStatement` compares that word to `0L`. A statement with no CLR type still fails closed. `AreHeapValues` and its Member heuristic (`AbiEmitter.Expressions.cs:311-331`) and `HeapValueToObject` are deleted with it.

### 2. Fold null first

When one side is `Constant { Value: null }` — no relational operator accepts null; if one reaches, fail closed:

| other shape | answer | emitted |
|-------------|--------|---------|
| `Optional` | `==` is absent, `!=` is present | the one handle, bound once |
| `Reference` | `handle == 0` / `!= 0` | the one handle |
| `Inline`, `HeapValue` or `Enum` | constant `false` / `true` | `Block(ValueOf(other), Constant(...))`; effects still run |
| `Null` (both sides) | `true` / `false` | constant |

This extends today's scalar-only fold (`AbiEmitter.Expressions.cs:114-127`) to every non-optional value. It fixes `required decimal == null` on an unseeded handle, which answers **true** at the tip (probe: `req dec == null unseeded=1`). `Null` is matched from the constant's shape before the null-CLR failure, so `== null` still folds (R1, P3).

### 3. Read both operands, left then right, each once

```
var leftRaw  = ValueOf(left, ctx);    // emitted first: parameter slots follow source order
var rightRaw = ValueOf(right, ctx);
var l = Variable(leftRaw.Type, "_cmpL");
var r = Variable(rightRaw.Type, "_cmpR");
return Block([l, r], Assign(l, leftRaw), Assign(r, rightRaw), test(l, r));
```

Already true at `6061cadf` for both existing helpers, each binding its operands to locals in source order; the protocol below inherits the shape. Why it is structural, not incidental: `TryDecimalEquality` used to call `ValueOf(decimalNode)` before `ValueOf(otherNode)`. `EmitParameter` resolves the parameter on first use (`AbiEmitter.Statements.cs:651-660`), `Context.DeclareParameter` gives it the next frame slot (`AbiEmitter.Context.cs:756-768`), and `SetArgs` binds argument *i* to slot *i*, so the decimal parameter takes the int argument's frame slot and `int == decimal` dereferences an integer as a handle. Locals do not show the bug: the tip answers `local int==dec=0` correctly, while `int==dec` throws `NullReferenceException`. Shapes and domains are decided without emitting, then exactly two `ValueOf` calls run in source order.

### 4. Pick one domain

`CompareDomainOf(left, right)` decides on the two decoded underlying CLR types (a nullable's underlying value). It is **C# binary numeric promotion plus the nullable lift**, not a copy of `FloatingEquals` (`ConstantFoldingPass.cs:698-707`). First match wins:

| operands | domain | equality | ordering |
|----------|--------|----------|----------|
| both `bool` | `Boolean` | `l == r` | fail closed: C# has no ordering operator for `bool`, and analysis does not reject it today (`SyntaxTypeCompatibilityAnalyzer.cs:1038-1045`) |
| either is an enum | `Value` | `object.Equals(l, r)` when neither side is optional. An optional enum uses the lifted operator in §6, not `object.Equals` | `cf(VmHeapComparison.Compare(l, r), 0)` when neither side is optional |
| `decimal` and `float`/`double` | `double` | `(double)decimal` vs the IEEE value | same compare |
| either is `double` | `double` | promote both | promote both |
| either is `float` | `float` | promote both (C# rounds an integral operand to `float`: `16777217u == 16777216f` is true) | promote both |
| either is `decimal` | `decimal` | promote both (exact; `ulong` stays unsigned, `char` is its code point) | promote both |
| otherwise both integral/`char` | `decimal` | promote both (exact; no `long` step, so nothing wraps) | promote both |
| both the same non-numeric value type | `Value` | `object.Equals(l, r)` | `cf(VmHeapComparison.Compare(l, r), 0)` |
| both references | `Reference` | `object.Equals(l, r)` | `cf(VmHeapComparison.Compare(l, r), 0)` |
| anything else | — | fail closed: `comparison between '<X>' and '<Y>' has no common value` | fail closed |

The function's comment must carry these rules; each one is a place the next reader could "fix" wrongly:

- **`decimal` vs `float`/`double`.** C# has no operator and rejects the mix. This table compares in `double` because `decimal` → `double` is total (it never throws, unlike `double` → `decimal` for NaN/∞/out-of-range) and it is the fold's existing choice. Precision caveat: above 2^53 the decimal operand compares at double precision, so `9007199254740993m == (double)9007199254740993m` is true — the fold's answer. Exact decimal↔IEEE is a follow-up, not a second rule.
- **No `long` domain.** Every integral pair promotes to `decimal`, which represents every `ulong`, `char`, and signed 64-bit value exactly. `Convert(ulong, long)` (R7) has no row and never appears.
- **Order matters.** A `decimal` operand is decided before `float`, so `decimal` vs `float` is `double`, never `float`.
- **`bool` ordering.** Analysis lets `true > false` through (two `Cat.Boolean` operands are `Compatible`), but C# has no such operator; the table fails closed instead of comparing 0/1.
- **Mixed `ulong`/signed and `char`/`ulong`.** C# has no operator for a non-constant signed operand (the constant `-1` does not convert to `ulong`). The exact `decimal` rule answers, at runtime and in the fold: `-1 == ulong.MaxValue` false, `'a' > ulong.MaxValue` false, `ulong.MaxValue > 1` true.

**Fold relationship.** The constant fold uses the same promotion as the runtime: `float` when either side is `float`, `double` when either side is `double` or a `decimal` meets a real, `decimal` for an integral pair (including mixed `ulong` and signed, which C# rejects). `2u == 2.0`, `ulong.MaxValue == (double)ulong.MaxValue`, and `16777217 == 16777216f` fold true. `-1 == ulong.MaxValue` folds false.

**Why not the review's three-step fold table.** Copying the old `FloatingEquals` guards into the ABI would have answered `2u == 2.0` false while C# answers true. Scot ruled the ABI is C#-sane, and the fold now uses that same promotion. The old `object.Equals` fallback compared boxed runtime types, not values.

### 5. Decode one value per operand

- `Inline`: `ScalarValue(word, Clr)` (`RingCodec.cs:291-296`, made internal) is the one word-to-CLR decoder: IEEE via `FromIeee`, `bool` via `!= 0`, everything else one `Convert`. Normalize with `ToWord` first (skip it when the local's own type already is `Clr`), so a `double` constant (typed slot) and a `double` parameter (word) take the same path. `ulong` is `Convert(long, ulong)`, an unchecked bitcast, so `ulong.MaxValue` survives as the unsigned value.
- `Enum`: `ScalarValue` to the underlying type, then `Convert` to the enum. The word is `RingCodec.EnumWord`, not a box. An optional enum is shape `Optional`: the local is `Nullable<enum>`.
- `HeapValue`: `RequiredValue(heap, handle, Clr)`, next to `UnboxNullableAs`: handle 0 throws `VM: required <type> value is absent`; otherwise return the box when it is already the type, else `ChangeType` to the declared type. A required decimal is never silently 0. Enums do not use this path.
- `Optional`: the local is `Nullable<T>` when the producer wrote one. A frame handle unboxes once (`UnboxToNullable`). There is no present flag.
- `Reference`: `value = handle == 0 ? null : RingCodec.DerefObject(heap, handle)`.

Then one `Convert` to the domain type. `Boolean` values are already `bool` and need no conversion; every numeric value is already at its declared CLR type, so the conversion is the single promotion to the domain. `Enum`, `Value`, and `Reference` stay objects. An optional side promotes to `Nullable<domain>` instead.

### 6. The lifted operator, when either side is optional

When either decoded value is `Nullable<T>`, both sides become `Nullable<domain>` and the CLR operator (`Expression.Equal`, `GreaterThan`, and the rest) answers. That is the lift: absent equals only absent, ordering with an absent operand is false, and present zero is a value. `NotEqual` is `Not(equality)`, never a second path.

A non-optional pair uses the domain operator directly. `Value` and `Reference` stay `object.Equals` / `VmHeapComparison`. An optional enum is the `Value` domain on `Nullable<enum>`, compared with `Expression.Equal`. `object.Equals` compares the boxes and drops the lift.

### 7. Never

- `Convert(ulong, long)` or any signed read of an unsigned operand; there is no `long` domain (R7).
- Classify an enum operand as `Inline` or decode it as an integer word; the constant encoder and `ToRing` box it (P2).
- `Convert(long, decimal)` on an IEEE word (R7).
- Dereference an inline word as a handle, or compare a handle to a number (R5).
- `Convert(long, bool)`; decode a bool as `!= 0` (R4).
- Route a nullable with a non-inline underlying through `long` (R2).
- Emit an operand out of source order (R6).
- Match `Null` after the null-CLR failure, or compare a null constant's type (P3).
- Let `FloatingEquals` decide a domain; the fold is not the contract (P1).
- Add a `Try*Comparison` for one shape. Add a domain row and a test instead.

## Shape matrix

Before values are measured at the tip's code (`14c5e8fa`, which `417b174e` does not change) with root parameters passed to `SetArgs` in source order (frozen probe, 2026-10-26). Every "before" that is wrong becomes a test that is watched red before the fix; equal pairs are regression pins.

| input kind | before | after | test that proves it |
|------------|--------|-------|---------------------|
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
| `2u == 2.0` (params) | true (the IEEE path already promotes the word numerically) | true (C# promotion; **the fold says false** — follow-up) | `UnsignedInt_Equal_Double_UsesNumericPromotion` |
| `2u == 2.0` (literal on the right) | true | true (same answer as the parameter form) | `UnsignedInt_Equal_DoubleLiteral_UsesNumericPromotion` |
| `ulong.MaxValue == (double)ulong.MaxValue` | **false** (the ulong word is read as a signed `long`) | true (C# promotion; fold false — follow-up) | `ULongParameter_Equal_DoubleMax_UsesUnsignedValue` |
| `16777217u == 16777216f` | true | true (C# promotes to `float`, not `double`) | `UnsignedInt_Equal_Float_UsesFloatPromotion` |
| `'a' > ulong.MaxValue` | **true** (signed wrap) | false | `Char_GreaterThan_ULongMax_IsFalse` |
| `9007199254740993m == (double)9007199254740993m` | **false** (rounds the double to decimal) | true (fold true; double precision documented) | `DecimalParameter_Equal_DoubleBeyondPrecision_MatchesFold` |
| enum param == same enum constant | true | true | `EnumParameter_Equal_SameMemberConstant_IsTrue` |
| enum param == different enum constant | false | false | `EnumParameter_Equal_DifferentMemberConstant_IsFalse` |
| `enum?` == enum constant, present / absent | true / false | same | `OptionalEnum_Equal_Constant_PresentAndAbsent` |
| `true > false` (bool params) | true (compares 0/1) | emit reject (C# has no ordering operator) | `Bool_Ordering_IsCompileRejected` |
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
| unseeded `decimal == decimal` | **true** (two null handles) | throws `VM: required Decimal value is absent` | `RequiredDecimal_Unseeded_Equality_Throws` |

## Files

Product:

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Compare.cs` — new partial: `CompileCompareTest`, `EmitComparisonValue`, `EmitComparison`, `FoldNullComparison`, `CompareOperandOf`, `CompareDomainOf`, `ReadCompareOperand`, `CompareValues`, the shape switch, the promotion table, and the fold-relationship comment.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Expressions.cs` — remove `CompileCompareTest`'s body and the comparison-only helpers (`HeapValueToObject`, `FoldScalarNullTest`, `IsNonNullableHeap`, `TryLiftedComparison`, `TryDecimalEquality`, `AreHeapValues`). Keep `NullableUnderlyingOf`, `NullablePresent`, `UnboxNullable`, `LiftedOperand`, `IsIeeeType`, `RejectNullableLogical` and the arithmetic/unary/logical arms.
- `Poly/Interpretation/Vm/Abi/RingCodec.cs` — make `ScalarValue` internal (the one word-to-CLR decoder) and add `RequiredValue(heap, handle, type)` next to `UnboxNullableAs`: handle 0 fails closed; otherwise return the box when it already is the type, else `ChangeType`. Do **not** change `ToRing`, `FromClr`/`ToClr`, or enum classification here (the enum-uniformity follow-up).
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` — add the `RequiredValue` `MethodInfo` beside `UnboxNullableAsInfo`.
- `Poly/Interpretation/Vm/VmHeapComparison.cs` — no change; reused for `Value`/`Reference` ordering (`VmHeapComparison.cs:11-26`).

Tests:

- `Poly.Tests/Interpretation/Vm/Abi/AbiEmitterCompareDomainTests.cs` — new; the shape matrix rows, `RequiredValue` failure, the evaluation-order probe, a private test enum for the enum parameter/constant rows, and `Bool_Ordering_IsCompileRejected`.
- `Poly.Tests/Interpretation/Vm/Abi/AbiEmitterOptionalArithmeticTests.cs` — add `OptionalDecimal_Arithmetic_IsCompileRejected`.
- Existing nets stay unchanged: `AbiEmitterOptionalHeapNullableCompareTests.cs` (R2/R5), `AbiEmitterNullScalarTests.cs` (R1/R4 fold), `AbiEmitterOptionalScalarTests.cs` (construction), `AbiEmitterOptionalBoolAssignTests.cs` (R4).

Docs (same change):

- `Poly/Interpretation/Vm/README.md` — file-map row for `Abi/AbiEmitter.Compare.cs`; one paragraph: a comparison reads values, never slots, and the domain is C# promotion with the nullable lift.
- `docs/interpretation/vm-abi-reference.md` — representation table: inline primitives are words; `decimal`/`DateTime`/… and every nullable are handles; an enum is its underlying integer. The Comparisons subsection states the promotion, the enum word, and unsigned `ulong` arithmetic.
- `docs/plans/poly-eng/tasks/abi-scalar-null.md` — this file.
- `docs/CORE.md` — no change; it only points at the emitter.

## Done when

1. The R6 rows pass (green at `6061cadf`, pinned by `AbiEmitterLiftedOperandOrderTests`): `int == decimal` (both parameters, either order) uses the values and does not throw; `int? 4 == decimal? 4.5m` is false, `4 == 4.0m` is true, both absent is true, one absent is false.
2. The R7 rows pass under **C# promotion, and the fold agrees**: `decimal 1.5 == double 1.5` and `== float 1.5f` are true; `(decimal)ulong.MaxValue == ulong.MaxValue` is true for a parameter, a literal, and a fold; `ulong.MaxValue > 1` is true; `-1 == ulong.MaxValue` and `'a' > ulong.MaxValue` are false; `ulong.MaxValue == (double)ulong.MaxValue` and `2u == 2.0` fold true; `16777217u == 16777216f` and `16777217 == 16777216f` fold true (the domain is `float`); `9007199254740993m == (double)9007199254740993m` is true. No `Convert(ulong, long)` exists on the comparison path. `ulong` division and the right shift run on `ulong`.
3. Required decimal ordering against an inline number and optional decimal/DateTime ordering use the values; absent ordering is false; ordering a `bool` is rejected (C# has no operator).
4. `decimal?`/`DateTime?`/`Guid?`/`TimeSpan?` equality still uses values (R2), a required scalar vs null still folds with no heap deref (R1), and a `Constant { Value: null }` is shape `Null` before the type check so `== null` still folds.
5. An enum parameter, an enum constant, and a `FromClr` enum result compare as the underlying integer word: same member true, different member false, `enum?` present/absent true/false. A `ulong`-backed member round-trips through the unchecked bitcast. An optional enum stays a box.
6. `TryDecimalEquality`, `TryLiftedComparison`, `AreHeapValues`, `IsNonNullableHeap`, `HeapValueToObject` are gone; one protocol decides shapes, one chooses a domain, and exactly two ordered `ValueOf` calls emit the operands.
7. `Unknown` comparison operands, and statements with no CLR type, fail closed (measured: 0/4636 analyzed comparisons at the tip). A resolved void call compares as inline `long`.
8. `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` is 0 warnings / 0 errors; the full suite is green on the tip with the count recorded in the PR comment.
9. No test is deleted, skipped, or weakened; the existing optional/heap tests pass unchanged.
10. Comments and XML docs state only what the code does, including the decimal-versus-real precision caveat; the protocol fits in one file and reads as a table.

## SHIP if / NOT SHIP if

**SHIP if**

- Every "after" in the shape matrix holds on the tip, and each wrong "before" was watched red first with the command and output recorded in the PR comment.
- The runtime and the constant fold follow the same C# promotion and lifted nullable semantics. The decimal-versus-real row's precision loss above 2^53 is the named caveat.
- The old comparison helpers are gone and no new `Try*` comparison helper exists.
- The build is clean, the changed-area filters pass, the full suite passes, and CI is green on the reviewed SHA.
- Grug has no blocker, major, or suggestion open, and the hand-edit gate is yes.

**NOT SHIP if**

- R6 or R7 is only correct for constants, or only when the decimal is the first-read parameter.
- Any operand is still compared as a handle, a word, or a signed `long` conversion of an unsigned/IEEE value.
- An enum slot is a box, or an enum word is read as a signed `long` instead of its underlying type.
- `Null` is matched after the null-CLR failure, or `== null` stops folding (R1).
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
- `RingCodec.ScalarValue` is the single word-to-CLR reader (an enum word goes to its underlying type, then to the enum) and `RequiredValue` is the single required-heap-value reader. A misread word has one place to fix.
- The comments name the ruling behind each rule (heap sentinel, C# nullable lift, C# promotion, source order, enum word) and cite the oracle; no comment describes a path the code does not take.
- No reflection at emit beyond the existing `Ref` lookups, no new `#region`, no agent paperwork in product paths.

## Follow-ups

Named here, not in this slice (Scot's ruling; do not expand #330):

| follow-up | why deferred |
|-----------|--------------|
| Constant fold for mixed unsigned/small-int vs IEEE | **Closed.** The fold uses the comparison promotion: `float` when either side is `float`, `double` when either side is `double` or a `decimal` meets a real, `decimal` for an integral pair. `16777217 == 16777216f` folds true because the integral converts to `float`, not because the double set grew. |
| Enum slot uniformity | **Closed.** `ToRing`, `TryInlineConstant`, and `FromClr` store `RingCodec.EnumWord`. Comparison converts that word back to the enum. A `ulong` underlying uses the unchecked bitcast. |
| Exact `decimal` ↔ `float`/`double` | Today the table compares in `double` (total, non-throwing, fold-consistent) and loses decimal precision above 2^53. A future exact rule can replace the one row. |
| Optional narrow-integral arithmetic | **Closed at `55da516d`**: `PromoteNumeric` widens `sbyte`/`byte`/`short`/`ushort` (and `char`) to `int`, so `byte? + byte?` is `int?` and boxes 400. |
| Optional `ulong` arithmetic | **Closed.** `ulong` division and the right shift run on `ulong`, including the lifted form. Addition already matched at the bit level. The ring still stores the bits in a `long`. |

## Needs Scot

none. The fold and the enum word are closed (see Follow-ups). Unseeded required `Guid` equality throws, pinned by `RequiredGuid_Unseeded_Equality_Throws`.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-26 | planner | opencode/opencode-go/deepseek-v4.1-flash | 14c5e8fa | planned | R6 R7 open; R1–R5 lessons |
| 2026-10-26 | planner | opencode/opencode-go/deepseek-v4.1-flash | 417b174e | revised after PLAN-REVIEW REVISE | P1: C# promotion replaces the fold-mirroring table, no `long` domain. P2: enums compare as boxes. P3: `Null` matched before the null-CLR failure. Scot ruling: C# nullable semantics; fold divergences parked. |
| 2026-10-26 | reviewer | grok review | 27e45cda | R6 fixed; 3 regressions closed | `6061cadf`: operands read once, left then right; nullable return boxes its scalar value; R6 source order. `55da516d`: narrow integrals promote to `int`. Full suite 4644/4644. R7 still open. |
| 2026-10-26 | implementer | grok | bc7656cc | R7 closed by the comparison protocol | **RED** (`dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter '/*/*/AbiEmitterCompareDomainTests/*'` from the worktree, against the old ladder): total 40, failed 22, succeeded 18. The 22 were the matrix befores: optional heap ordering threw, decimal vs ulong/float/double compared as words, unseeded decimal and Guid equality did not throw, required decimal `== null` dereferenced the heap. **GREEN** same filter 40/40. `decimal? + 1m` stayed an `Add` until analysis stopped rewriting a nullable operand to `Decimal.Add`; `AbiEmitterOptionalArithmeticTests` 10/10 after that guard. A resolved void call is inline `long` (`EmitResult` writes `0L`); `DirectVmAbiEmitterTests` 36/36. Optional `ulong` arithmetic stays out. Benchmarks build 0 warnings / 0 errors. Full suite 4685/4685 (baseline 4644; +40 compare-domain, +1 optional-decimal arithmetic). |
| 2026-10-26 | implementer | grok | 0441ffbd | fold, enum word, and ulong arithmetic closed | **RED** (worktree, new pins on the protocol tip, before the writers changed): `RingCodecTests` 3/12 failed (Saturday was a heap handle, not word 6); `ConstantFoldingTests` 6/34 failed (unsigned-vs-real and `16777217 == 16777216f` folded false; `ulong.MaxValue / 2` did not fold); `AbiEmitterCompareDomainTests` 3/44 failed (constant `true > false` did not throw; `DayOfWeek` member equality threw `InvalidCastException` in `RequiredValue`); `AbiEmitterOptionalArithmeticTests` 2/12 failed (`ulong?.MaxValue / 2` was 0, `>> 1` stayed all-bits); `NumericTypePromotionTests` 2/12 failed the same way. **GREEN** those filters 12/12, 34/34, 44/44, 12/12, 12/12. Neighbor classes stayed green (`ValueRepresentationEmitTests` 16, `DirectVmAbiEmitterTests` 36, `Int32WidthTests` 6, optional heap/null/scalar/bool and lifted-order). Benchmarks build 0 warnings / 0 errors. Full suite 4701/4701 (baseline 4685; +16). |
| 2026-10-26 | implementer | grok | 55d01f70 | nullable body is a `Nullable<T>` local | **RED** `OptionalDecimal_Arithmetic_UsesTheValue` against the old emitter: `arithmetic on an optional Decimal is not supported`. After the lift, `AbiEmitterLiftedOperandOrderTests` 3/5 failed (a nullable call's frame handle was added as a long: one call answered 3, two calls answered 5, equality was false). **GREEN** after the caller unboxes that handle once: arithmetic 12/12, compare-domain 44/44, lifted-order 5/5, coalesce 7/7, `LanguageVmTests` 115/115. Benchmarks build 0 warnings / 0 errors. Full suite 4701/4701 (the decimal pin was rewritten in place). |
