# TASK ABI6 - One storage-location model (`SlotLocation`)

Status: implemented; branch `refactor/abi6-slot-locations` (from `origin/master`
`74afd364`). Review 1.
Plan: this file.

## Why

The emitter had **no noun for "where a value lives"**. A value has one
representation — a `long` slot, inline word or heap handle — but several places to
sit, and every site re-derived the location, the shape, and the coercion by hand:

- 12 `_slots[_fp + …]` addressing idioms in three offset forms, plus a debug-span
  slice that computes the same base without `ArrayAccess`.
- 5 binding-storage ladders (capture → bound parameter → inline parameter → frame
  parameter → register → slot), one of them keyed by name in `LambdaBridge`.
- 9 hand-rolled IEEE bitcasts, and the `BitConverter` reflection fields declared
  twice.
- capture-cell read/write rebuilt by hand at four sites.
- value-form / ring-form twin methods.

That is a correctness surface, not a tidiness one: with N storage kinds and two
shapes, every new site can pick the wrong pair, and only the suite notices.

## Scope

### ABI6a - one IEEE codec, one member table
- `RingCodec.ToIeee`/`FromIeee` are the only writer/reader of the
  `double`↔`long` bitcast; every hand-rolled site calls them (`RingCodec` 4,
  `AbiEmitter` 3, `AbiEmitter.Expressions` 2, `AbiEmitter.Statements` 2,
  `AbiEmitter.LambdaBridge` 1).
- Deleted the duplicate `BitConverterInt64BitsToDouble`/
  `BitConverterDoubleToInt64Bits` fields from `AbiEmitter.Invoke.cs`.

### ABI6b - `SlotLocation` for every binding
- New `Poly/Interpretation/Vm/Abi/SlotLocation.cs`: an abstract record with
  `Ring(slot)`, `Frame(base)`, `Local(clrLocal)`, `Cell(array)`; `Read`/`Write`
  are one call per location.
- `Context.Resolve(Variable)` is the one resolver: **capture first**, then the
  bound-parameter / register / frame-slot ladder, wrapping a cell-backed local in
  its cell. `ResolveStorage` keeps `VariableRead`/`VariableWrite`'s rule (own
  storage plus cell, no capture); `ResolveRaw` keeps `VariableReadRaw`'s.
  `Resolve(Parameter)` keeps `ParameterValue`'s declare-on-first-use and the
  pre-C1a `_this` alias.
- `VariableRead`/`VariableWrite`/`VariableReadRaw`/`VariableWriteRaw`/
  `ParameterRead` are now one-line delegations. `EmitVariable`, `EmitAssignment`'s
  variable arm, `ParameterValue`, and the capture-cell helpers use `Resolve`.
- Capture precedence is load-bearing: a captured parameter is read from the
  closure, not from a parameter slot the closure body never declared. The first
  cut checked the bound parameter first and broke
  `LambdaBridgeVmTests.StoredLambda_CapturingParameter_QuotesFromTheParameter`;
  the suite caught it.

### ABI6d - collapse the exact twin
- `EmitBinaryArithmetic` = `SpillToRing(EmitBinaryArithmeticValue(…))`. The IEEE
  and shift rules had two copies.
- The other twins are **not** exact: `EmitUnaryMinus`, `EmitBitwiseNot`, `EmitNot`
  and the short-circuit forms reuse the operand's ring slot and assign in place,
  which is a different emitted shape from `SpillToRing(value)`. Left alone.

### ABI6e - the two contradictions (probe first)
Two red tests were written before any of the above. Outcome:

1. **Nullable inline-vs-heap: not a bug — a naming collision.**
   `RingCodec.KindFor` peels `Nullable` (`int?` → inline) and
   `ValueRepresentationClassifier.KindFor` calls it a heap handle. Both are
   correct answers to *different questions*, and `RingCodecTests` already pins the
   former. Renamed to `RingCodec.UnderlyingKindFor` and documented: it asks
   whether the **underlying** value is long-representable; the classifier asks
   what a **node** of that type puts in its slot. `SlotInvariantsTests` locks the
   split. No runtime change.
2. **Handle-0 deref: benign.** `VmManagedObjectHeap` starts at 1, so handle 0 is
   never live and both the guarded (`HeapValueToObject`) and unguarded
   (`ToClr`) idioms yield null. Locked by `SlotInvariantsTests`.

### Deferred - ABI6c, the frame window
The frame-window location (`SlotLocation.Window`) belongs to the small-array
promotion, which ships in its own PR. That PR adds the `Window` case on top of
this model: a promoted buffer resolves to a window and is read/written by
element, with the same bounds failure as a heap array. Keeping it out of this PR
is deliberate — this one is behaviour- and perf-neutral, and a reviewer should be
able to check that without judging an optimization.

## Shape contract

No emitted shape changes. `VmExpressionShapeTests` is unchanged and green.

## Files

- New: `Poly/Interpretation/Vm/Abi/SlotLocation.cs`
- New: `Poly.Tests/Interpretation/SlotInvariantsTests.cs`
- `Poly/Interpretation/Vm/Abi/RingCodec.cs` — `ToIeee`/`FromIeee`, `UnderlyingKindFor`
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` — `FrameSlotRead`, `Resolve`, `ResolveStorage`, `ResolveRaw`, `CaptureCellArray`
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` — assignment, parameter, capture cell
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Expressions.cs` — `EmitVariable`, `EmitBinaryArithmetic`
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` — IEEE sites
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs` — dropped the duplicate bitcast fields
- `Poly/Interpretation/Vm/Abi/AbiEmitter.LambdaBridge.cs` — `FromIeee`
- `Poly/Interpretation/Vm/README.md`, `docs/interpretation/vm-abi-reference.md`
- `Poly.Tests/Interpretation/RingCodecTests.cs` — renamed to `UnderlyingKindFor`

## Done when

- Full suite green: **4272 passed, 0 failed**.
- `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`: 0 warnings.
- nqueens PolyVM benchmark **unchanged from master** (min 58 µs / med 60 on this
  branch, which is the `74afd364` baseline). This PR is perf-neutral by design.
- `RingCodec.KindFor`, the duplicate `BitConverter` fields, `CellRead`/`CellWrite`,
  and the five binding ladders are extinct.

## Residuals (evidence, not scheduled)

1. `RingCodec.IsInline` is `UnderlyingKindFor`-based, so `IsInline(int?)` is true.
   Four sites use it as "the slot holds the word", which is wrong for a nullable:
   `AbiEmitter.Invoke.cs:716` (`ReadsRawSlot`), `AbiEmitter.Expressions.cs:479`
   (`EmitDefault`), `AbiEmitter.cs:336-345` (`EmitNew` ctor args), and
   `RingCodec.ToClr`'s inline arm. Each needs a red test before a fix; nullable
   slots are half-implemented end to end.
2. `AbiEmitter.LambdaBridge.cs` resolves bindings by **name** rather than
   identity. That is the one ladder that can legitimately disagree with `Resolve`
   under shadowing, so it was left alone. Fold only after a shadowing
   characterization test.
3. A resolved array element type of `int?` (from analysis, not from an
   `OptionalTypeReference` node) still takes the `long[]` shape while the
   classifier calls a nullable a handle. The AST-declared case is rejected.
4. Remaining twin pairs (`EmitNot`, `EmitBitwiseNot`, `EmitUnaryMinus`,
   `EmitPopCount`, `EmitConditional`, `EmitCoalesce`) are shape-distinct;
   collapsing them changes register pressure and needs a shape decision.
