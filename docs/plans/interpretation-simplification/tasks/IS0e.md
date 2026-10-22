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
| 2026-10-22 | agent | Copilot/VS Code | `71132746` | SHIP: the void CLR arm finishes its reserved slot with `0L`, as the AST and name-dispatch arms do | **RED** (`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DirectVmAbiEmitterTests/VoidClrInvoke*'`): `total: 2 failed: 2 succeeded: 0` — `VoidClrInvoke_AssignsItsReservedSlot`: `AssertionException: Expected to contain 1 but the item was not found in the collection` at `Assert.That(scan.AssignedRingIndexes).Contains(1)` (the body declares ring locals `[0, 1]` and assigns only `0`: `_r1`, the slot the void call reserved, is read by the root write-out and never written); `VoidClrInvoke_LeavesZero_ForTheNextStatement`: `AssertionException: Expected to be 1 but found 0` — the comparison read the previous statement's string handle out of the slot the void call reserved. **GREEN** same filter `2/2`; `(DirectVmAbiEmitterTests|SlotInvariantsTests|VmTreeInvariantTests)` `48/48`; lane C `(*Invoke*|*Lambda*|*Slot*|*Loop*|*Vm*)` `629/629`; full suite `4297/4297` in three consecutive runs on the IS0d base. After rebasing onto the plan tip (`0e3d1649`, IS0a) the same filters read `48/48` and `630/630`, and the full suite `4303/4303`; `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings. **Fix:** one statement, `fullBody.Add(Assign(ctx.RingVar(slot), Constant(0L)))` — the arms now read `:121`, `:205`, `:759`, one invariant, one answer. **Test helper:** `VmTree.Scan.AssignedRingIndexes`. `RingIndexes` alone cannot answer the question: with the bug the reserved slot is still *declared* (the root write-out asks for `RingVar(RingDepth - 1)`) and still *read*, so it is not a `DeadLocal`; only an assignment list separates "reserved" from "written". **Second test was constructible:** a block whose first statement leaves a handle in the slot the void call reserves, then `Equal(voidCall, 0)`; before the fix it compares the handle and yields `0`, after it yields `1`. `SlotInvariantsTests` was checked and is not its home — that file pins the two representation-kind predicates and holds no emitter oracle — so both tests live in `DirectVmAbiEmitterTests`, the file the card names for the structural assertion, without duplicating one fact in two homes. **Untouched by design:** the three scalar predicates (IS5) and `CompileValue` (IS3). **Flake, not this slice:** `LambdaBridgeVmTests.DelegateInvokedAfterExecution_StateAlive_SeesLiveCapture` failed once in the first full run, then passed in isolation and in three later full runs; `LambdaBridgeProbe._captured` is process-wide static state that two tests both `Capture` into and `Fire`, so a parallel interleaving can swap the delegate. **Benchmarks:** flat by construction (one assignment, no new slot); no numbers recorded because ISB has not established the baseline yet, and IS-README §Suite gate item 3 collects them on IS3/IS5. |
