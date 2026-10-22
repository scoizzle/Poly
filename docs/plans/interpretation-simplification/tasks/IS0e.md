# TASK IS0e - a void CLR invoke writes the slot it reserved

Status: planned. Branch `slice/IS0e`. Lane C (`vm`). Issue #260.
Plan: [IS-README](../IS-README.md).

## Why

`AbiEmitter.Invoke.cs:119-123` counts a result slot for a CLR call and, when the target returns `void`, never writes it:

```csharp
if (resultType == typeof(void)) { fullBody.Add(callExpr); return Block(fullBody); }
```

The AST invoke path writes `0L` to the same slot (`:204`, `:765`). Ring locals persist across statements — `EmitBlock` rewinds `RingDepth` without clearing — so a slot that is counted but unwritten carries whatever a previous statement left there. Three paths, one invariant, two answers.

The **durable** fix is IS3 (make `CompileNode` total so a void node leaves `0L` in the ring by construction, and `CompileValue` disappears). This card is the small, in-place restore that stops the base from being knowingly wrong for the whole of lane A's work.

## Scope

- Assign `0L` to the reserved slot in the void arm, the same statement the AST path uses (`:204`).
- Do not touch the three scalar predicates (`RingCodec.IsStackScalar` `:120`, `RingCodec.IsInvokeArgumentScalar` `:137`, `ReadsRawSlot` `Invoke.cs:714`) — that is IS5, and mixing it here makes this PR unverifiable.
- Do not delete `CompileValue` — that is IS3.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs` — the void arm.
- `Poly.Tests/Interpretation/DirectVmAbiEmitterTests.cs` — the structural assertion.
- `Poly.Tests/Interpretation/SlotInvariantsTests.cs` — if the slot-written fact belongs in the invariant suite (check first; do not duplicate the assertion in two homes).

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| a statement-position CLR call to a `void` method | slot counted, never assigned | slot assigned `0L` | `VoidClrInvoke_AssignsItsReservedSlot` (uses `VmTree.Scan.Read(...).RingIndexes` / `AbsoluteSlotAccesses`) |
| the same call followed by a statement that reuses the slot | may read the previous statement's value | reads `0L` | `VoidClrInvoke_LeavesZero_ForTheNextStatement` |
| a CLR call returning a value | unchanged | unchanged | existing invoke tests |

## Done when

- Both tests fail before the change (the first does; if the second cannot be constructed from the public AST surface, say so in the Log and keep the first).
- The void arm and the AST arm are textually identical in how they finish the slot.
- `VmTree.AssertInvariants` still passes for the invoke corpus.
- Full suite green; benchmarks flat (this adds one assignment, not a slot).

## SHIP if / NOT SHIP if

SHIP: the reserved slot is written on the void path and the structural test proves it from the emitted tree, not from source inspection.
NOT SHIP: the fix is a comment; `CompileValue` was touched; the scalar predicates were unified here.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DirectVmAbiEmitterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SlotInvariantsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmTreeInvariantTests/*'
```
Then the full suite.

## Hand-edit

The void arm ends with the same one assignment as the two sibling paths, three lines apart in the same file.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
