# TASK ABI7 - One kind of local, copies only at observation points

Status: row written 2026-10-27 on the user's instruction ("write the board row and full send"); Scot to confirm the admission. Implementing on this worktree, uncommitted. Stages S0-S5 in section 5 are the done-when list. Design decisions D1-D4 and the renames are recorded in section 8.
Plan: this file. Verification: per-stage verifier runs, then the parent full suite.

Repo: /Users/scoizzle/Projects/Poly/Poly.worktrees/nullable-t-locals
Scope: Poly/Interpretation/Vm/Abi/ (Context, SlotLocation, FrameRepresentation,
ValueRepresentation, RingCodec, CompiledNode, and the emitters that use them), plus the
suspend path (EmitSuspendNode, resume dispatch), VmDebugInfo, and VmDebugger.
Tier: T2 (a seam used by every emitter, and by the debugger and the future coroutine model).
Admission: NOT admitted. Needs a board row from Scot.

## The model in four sentences

1. Every local the VM declares, whether a temp, a variable, or a helper, is a CLR local of a
   **`LocalType`**. The `LocalType` gives its CLR type, its name prefix, its zero value, and its
   frame-word encoding. Adding a typed local is one subclass and one registration.
2. Every local is **leased from one pool** and returned when its scope ends. There are no
   registers and no second kind of variable.
3. A value is copied to the **frame arena only at an observation point**: a block exit (for
   post-execution `GetLocals`), a debug hook (for the hook's span), and a suspend (for the
   debugger and resume). No copy happens on a write.
4. A **suspend is a spill barrier**: the emitter stores each live local to its frame word, and
   the resume label reloads it, because resume re-enters the method with CLR locals reset.

## 1. What is wrong today

| Concept | Where (verified) |
|---|---|
| CLR type to representation | `FrameRepresentation` (6 record subclasses), `ValueRepresentationClassifier` (4 methods, `ValueRepresentation.cs:36-49`), `RingCodec` (12 forwards) |
| Typed nullable local | `FrameRepresentation.SlotType` (`:119`), `Context.SlotTypeOf` (`AbiEmitter.Context.cs:284-285`), `_n{index}_{underlying}` names (`:268-269`) |
| Storage location | `SlotLocation`: Ring, Frame, Local, Cell, View |
| Temp | `AllocSlot` (22 call locations, Roslynk), `RingDepth`, `RingRegion`, `RingScope`, `RingVar(index, type)` |
| Variable | a register (`_regN`, `_regUsed`, `_variableRegisters`, `RegisterVar`) **and** a frame slot (`DeclareVariable`, `_nextFrameSlot`), with `EmitScopeStores` copying the register into the slot on every statement under the hook |
| Hoisted CLR locals | `_typeHandles`, `_bridgeLocalCounter`/`DeclareBridgeLocal`, `_arrayLocals`/`_promotedArrays`, ad-hoc `Variable(...)` (`AbiEmitter.cs:534,736,877,880`) |
| Naming check | `VmTree.cs:20` (`^_[rdfi](\d+)$`) and `:21` (`_reg`) are hand-written, and they miss `_n` locals |
| Suspend | `EmitSuspendNode` (`AbiEmitter.Statements.cs:600-632`) saves the state fields, and no value. Resume re-enters from the top, so CLR locals start at zero |
| Name lookup | `TryGetVariable`, `TryGetRegister`, `TryGetInlineParameter(string)`, `TryGetFrameParameter(string)` (`AbiEmitter.Context.cs:715`), `LambdaBridge` `ParametersByName` / `CapturesByName` (`AbiEmitter.LambdaBridge.cs:521-538`) |

The copies that exist today: the per-statement register-to-frame copy (`EmitScopeStores`,
`AbiEmitter.cs:269-286`) sits inside the debug-hook guard and runs only when a hook is attached. Two
copies run unconditionally: the block-exit snapshot (`AbiEmitter.Statements.cs:202-211`) and the
catch-exit snapshot (`:585-587`). The catch-exit snapshot is the only record of the synthetic catch
variable. None of these copies is needed by execution. The hook and `GetLocals` read them.

## 2. The model

### 2a. `LocalType`: the one base for every typed local
An abstract base class. Each CLR type the VM stores in a local is one registered `LocalType`. It
answers five questions and nothing else:
- `Clr`: the CLR type of the local.
- `Prefix`: its name family. Locals are named `_r`, `_d`, `_f`, `_i`, `_n{i}_{T}` (the same names
  today's ring uses, `AbiEmitter.Context.cs:264-270`). Variables use the same family, so there is no
  `_reg` family.
- `Zero`: the initial value.
- `ToWord` / `FromWord`: the frame-word encoding, used only at observation points and suspend.
- `IsHandle`: whether the word is a heap handle.

| Subclass | CLR types | Word encoding |
|---|---|---|
| `Int64Local` | `long`, `uint`, `ulong` (bitcast), `nint`, `nuint` (bitcast), and an enum whose underlying type is 64-bit | the integer (`FrameRepresentation.cs:202-258`) |
| `Int32Local` | `int`, `short`, `ushort`, `sbyte`, `byte`, `char`, and an enum whose underlying type is 32-bit or smaller | a typed 32-bit local; a word on the frame |
| `IEEELocal` | `double`, `float`: IEEE bits (`RingCodec.ToIeee`/`FromIeee`, the one codec) | IEEE bits |
| `NullableLocal<T>` | `Nullable<T>` | a handle; the typed local keeps the nullable type (D1) |
| `TemporalLocal` | `DateTime`, `DateOnly`, `TimeOnly`: one word each, with a per-type encoder. `DateTime` uses `ToBinary`/`FromBinary`, which keeps `DateTimeKind`. `DateOnly` uses `DayNumber`. `TimeOnly` uses `Ticks`. `DateTimeOffset` is a special case: 16 bytes, so a handle, with its own encoder | per type (word, or handle for `DateTimeOffset`) |
| `StructLocal` (value type) | `decimal`, `Guid`, `Int128`, `UInt128`, and any other value type that does not fit one word and is not temporal. Explicit, so a new struct is a conscious choice | a heap handle (box into the heap; unbox on read) |
| `HandleLocal` | reference types | a heap handle |
| `FlagLocal` | `bool` | 0 or 1 (`FrameRepresentation.cs:286-299`) |
| `NoneLocal` | `void` | no word. A value-less node leaves `0L`, so it has no local |

Registration is the only extension point. `VmTree`'s patterns, the pool's keys, the zeroing, and
the frame encoding are read from the registry.

`FrameRepresentation`, the classifier methods, the `RingCodec` forwards, `SlotLocation.View`, and
the register machinery are replaced by `LocalType` (S1 and S3).

### 2b. One pool
A pool with a free list per `LocalType`. `pool.Lease(localType)` returns a `ParameterExpression`
and an `IDisposable` that returns it when the scope ends. Temps, variables, and helper locals all
lease from it. `MaxRingDepth` becomes the high-water of simultaneous leases in the shared index
space (`VmProgram.cs:11-13`).

Helper locals (`_coal`, `_shift`, `_callSp`, `_callerFp`, the hoisted type handles, bridge locals,
and promoted arrays) become leases too. The suspend spill set is read from the same pool once
they are.

### 2c. Values instead of indices, with explicit exceptions
`Value` = `(Expression Read, Place Where, LocalType Type)`, replacing `CompiledNode.Result` and
`int` slot operands. The default shape:
```
using var scope = ctx.Scope();
var left  = Compile(l);     // Value, leased in scope
var right = Compile(r);     // Value
return scope.Yield(Value(Add(left.Read, right.Read), ...));
```
Five groups of sites do NOT fit the default. Each keeps an explicit form, reviewed in S2:
1. `EmitSwitch` (`AbiEmitter.Statements.cs:365-413`): the discriminant stays live across every arm.
2. SetArgs inline lambda (`AbiEmitter.Invoke.cs:262-298`): the result is copied to `saveDepth`
   when it did not land there, or when it is a typed slot (`:293-294`). `EmitInlineInvoke`
   (`Invoke.cs:453-466`) does fit `Yield`.
3. `EmitAstArguments` / `EmitAstInvoke` (`Invoke.cs:803-826`, `874-880`): N argument values and
   the instance receiver (`:856-863`) stay live across the call.
4. Short-circuit `And`/`Or` (`AbiEmitter.Expressions.cs:196-235`): the result is `ring.Base`, and
   the right operand's lease is live until the else branch reads it.
5. In-place unaries and `PopCount` (`Expressions.cs:240-293`), with nullable spills kept
   (`:254-255`, `:274-275`), and index-assign overwrite (`Statements.cs:40-42`). These stay. A spill
   is a shape change, and ABI6 residual 4 left them alone.

### 2d. Two places, no view
- `Local(ParameterExpression)`: every temp, variable, and helper.
- `FrameWord(offset)`: `_slots[_fp + offset]`. Parameters, the result slot, and the observation
  copies.
- `Cell(array expr)`: a capture cell's element 0 (a captured variable only).
`View` is removed. A typed read is `Value.As(localType)`, a conversion on a value.

### 2e. Variables, observation points, and the suspend barrier
A variable is a `Local` leased from the pool, like a temp. A write is a plain assignment. No
copy is made on a write.

Frame copies happen only at these observation points:
- **Block exit** (unconditional, one store per register-backed variable of the block, frame-only
  variables skipped). It keeps post-execution `VmDebugger.GetLocals(VmState)` working. Three tests
  read it, through their `GetLocals(exec.State)` calls: `VmDebuggerTests.cs:337`
  (`FromState_AfterExecute`, method at `:330`), `:361` (`NestedBlocks_FrameOffsetsAreDistinct`,
  method at `:344`), and `:407` (`CapturedVariable_IsCellValueNotHandle`, method at `:398`). Those
  tests fix the requirement, so the copy stays as long as the requirement does (D2).
- **Catch exit** (unconditional, after a catch body, `Statements.cs:585-587`). The only snapshot of
  the synthetic catch variable.
- **Debug hook** (only when a hook is attached, `Normal` mode). The hook receives a span of the
  frame, so the copies for that span are emitted inside the hook guard, once per statement.
- **Suspend** (once per suspend point). See below.

**A suspend is a spill barrier.** The spill set at a `SuspendNode` is the set of CLR locals that die
on resume and are read after it: variable leases, enclosing temps still needed, and helper locals
assigned before the suspend and read after it (`_coal` at `AbiEmitter.cs:877-878` and `:880-888`,
`_shift` at `:733-745`, `_callSp` and `_callerFp` at `Invoke.cs:268-299`). Making the set exact is
part of S2. Three exclusions are required:
- A binding whose only home is already a frame word (a variable with no free lease, or the 33rd
  variable in `ThirtyThreeSimultaneousVariables_FallBackToFrameSlots`) is not spilled. Its word
  survives resume, because `_slots` is rebound from `VmState` before the dispatch
  (`AbiEmitter.cs:160`). Spilling a zeroed lease over it would clobber the value.
- A cell-backed variable spills its handle, never `cell[0]`. Its value lives in the heap cell,
  which is rebound before dispatch (`AbiEmitter.cs:161`). A frame word holding the handle is what
  `DecodeLocal` (`VmDebugger.cs:283-286`) expects.
- A binding is never spilled twice.
For each member of the spill set:
- before the exit: `frameWord = local` (the `LocalType`'s `ToWord`; for a cell-backed lease, the handle)
- at the resume label: `local = frameWord` (the `LocalType`'s `FromWord`). The reload is the first
  expression after `Label(resumeLabel)` in `EmitSuspendNode` (`AbiEmitter.Statements.cs:630`),
  before `clearSuspended`. It must not be placed after `EmitPcDispatch` (`AbiEmitter.cs:187`),
  whose `Goto` skips everything between the dispatch and the label.

The spill is required, not a redundant copy: resume re-enters the method, and CLR locals start at
zero. The reload is the only way a value crosses that boundary.

**The debugger instance reads the suspended frame.** `VmDebugger.Continue()` returns
`CaptureCompleted()` (`VmDebugger.cs:164-167`, `:223-228`) before it checks `Suspended`. A suspend
ends the delegate with `Goto(ExitLabel)` (`Statements.cs:629`), so `Continue` returns a stale
`CurrentLocals` snapshot from the last stepped statement (filled only by the hook, `:195`, and only
while stepping). It is not empty and it is not the suspend-point frame. `Start` steps once, so that
snapshot exists. A hook-less run never calls `Continue`, and that path is `GetLocals(state)`, which
hardcodes `fp = 0` (`:272-277`). S0's red test asserts the stale snapshot. S3 refreshes
`CurrentLocals` from the spill on this path, or checks `Suspended` before `_completed`, and reads
`FramePos` from the state.

Name lookup: `TryGetVariable`, `TryGetRegister` (removed with registers), `TryGetInlineParameter(string)`,
`TryGetFrameParameter(string)`, and the `LambdaBridge` name maps are removed in S4, after a shadowing
characterization test.

### 2f. The frame arena stays monotonic
The arena holds the observation copies, so `FrameOffset` values must not move while a body is
emitted. Reclaiming frame slots per scope is a later improvement.

### 2g. Typed nullable (D1)
Nullable is a handle in the classifier (`FrameRepresentation.Optional.Kind`, `:324`), and the typed
nullable local is `SlotType` (`:119`). With `LocalType` this is one registration, either typed (no
shape change) or a handle. A single decision.

## 3. Suspension cases, each with a test

Suspension is the debugger model and the future coroutine model, so each case has a test in S0.
None is assumed.

| Case | Required behaviour | Status |
|---|---|---|
| Variable set before suspend, read after resume | the value is preserved | must hold (red test first) |
| Debugger `Continue()` to a suspend point | the suspended frame's values, not the last stepped snapshot (`CaptureCompleted`) | must hold (red test first) |
| `GetLocals(state)` with a parked SetArgs frame | the suspended frame's values | must hold (red test first) |
| `double` / `float` variable across suspend | the IEEE value | must hold |
| Array or object handle across suspend | the handle survives and is not recycled early | must hold |
| Loop-carried variable across suspend | preserved at each iteration | must hold |
| Suspend in the right arm of `??` (`_coal`), and in an operand of `<<` / `>>` (`_shift`) | the helper keeps its value | must hold (red test first) |
| SetArgs-body suspend, then return, from a caller frame that is neither 0 nor `callSp` | after `Invoke.cs:299`, `FramePos` equals the caller frame saved at `:274` | must hold (red test first) |
| Post-execution `GetLocals` (three existing tests) | unchanged | must keep passing |
| Suspend in value position with a live temp | the temp is spilled and reloaded | D3 |
| Suspend inside `try`, `catch`, `finally`, `using`, or `foreach` | rejected with a positioned error. CLR cannot jump into a `try`, the probe already threw `Control cannot enter a try block` (`docs/decisions/2026-10-15-async-await-in-the-interpreter.md:113`, `:253`), and `Goto(ExitLabel)` runs `finally` | D4 |

Existing suspend tests (`SuspendResumeVmTests.cs`) cover none of the "must hold" rows.

## 4. What this does NOT change
- Emitted semantics. Names are kept for temps. Register names go away, which is a shape change,
  reviewed in S3.
- Typed IEEE locals (`IEEELocal`) and the perf-critical paths. A variable that was a register is now
  a ring-style local of the same type, so its performance is the same CLR local kind.
- The heap contract: promoted-array clear and `heap.Recycle` (`HeapLifetimeTests`,
  `ArrayLocalCacheTests`, `HeapLifetimeTests.PromotedArray_ScopeExit_RecyclesTheHandle`), with the
  clear moved into the array lease's dispose.
- The debugger contract: `VariableLayout.FrameOffset` and the span, now fed by observation copies.

## 5. Staging (each stage green before the next)

S0 - Characterize and write the red tests. (a) The snapshot corpus: `ProgramBenchmarks` programs
(NQueens, Mandelbrot, Sieve, Collatz), the README example, `SuspendResumeVmTests`,
`ShadowedVariableVmTests`. (b) One red test per "must hold" row of section 3, each run and recorded
with its command. (c) The performance baseline (section 6).
S1 - `LocalType` (2a). Replace `FrameRepresentation`, the classifier, the `RingCodec` forwards, and
`SlotLocation.View`. Temp names are kept. `VmTree`'s patterns come from the registry. Oracle: suite
green and an empty snapshot diff.
S1b - Integer family (see the rule under section 2a). Each small type gets its narrowing test red
first, then the family change. Oracle: suite green, a reviewed snapshot diff listing every local whose
CLR type changed, and the performance gate.
S2 - Pool, leases, `Value` (2b, 2c, 2d). Replace `AllocSlot`, `RingDepth`, `RingRegion`,
`RingScope`, `RingVar`, `_typeHandles`, `_arrayLocals`, and the bridge locals. Make the helper
locals leases. Oracle: suite green, a reviewed snapshot diff, and the performance gate.
S3 - Variables and observation (2e). Variables become leases (registers removed). The block-exit
snapshot, the catch-exit snapshot, the hook copies, and the suspend spill are the only copies.
`CurrentLocals` on suspend reads the spill. Shape and name tests that change, each explained:
- `VmExpressionShapeTests`: `NoVariables_EmitsNoRegisterLocals`, `OneVariable_EmitsOnlyItsRegister`
  (asserts `_reg0`), `SiblingScopes_ReuseOneRegisterLocal`, `NineSimultaneousVariables_GrowByOne`,
  `ThirtyThreeSimultaneousVariables_FallBackToFrameSlots`. `RegisterLocalCount` (`:31`) counts only
  `_reg`.
- `TypedVariableStorageTests`: `DoubleVariableArithmetic_HasNoBitcast` (`_dreg0`),
  `DoubleVariable_AcrossStatements_KeepsItsRegister`, `DoubleVariable_ToClrCall_ConvertsOnce`,
  `DoubleVariable_Captured_ConvertsAtTheCell` (`_reg0`), `RegisterIndex_ReusedAcrossRepresentations_DoesNotAlias`,
  `IntegerVariable_StaysAWord` (`_reg0`).
- `IeeeRingSlotTests.IeeeAcrossStatements_CrossesTheBoundaryOnce`: `RegisterBitcasts` and the
  bitcast count of 5 (which assumes two block-exit snapshots).
- `ArrayLocalCacheTests.SiblingBlock_SlotReuse_ReadsNullNotStaleArray` (asserts `_reg0`).
- `VmTree`: `VariableRegister` (`VmTree.cs:21`, `^_(?:d|f|i)?reg(\d+)$`) and `RegisterBitcasts`.
  Once variables use ring names, `RegisterBitcasts` must still detect bitcasts on variable locals.
  Its emptiness today is true for the wrong reason, so the scan is rewritten to the `LocalType`
  registry (S1), not to the new names.
The names removed are `_reg`, `_dreg`, `_freg`, `_ireg`, and `_nreg`, not only `_reg`.
Oracle: every "must hold" red test from S0 turns green, the three post-execution tests stay green,
the catch-exit and block-exit snapshots are still present, and the suspend shape is reviewed.
S4 - Name lookup (2e). Characterize with a shadowing test, then remove the `Context` fallbacks and the
`LambdaBridge` name maps. Oracle: suite green plus the shadowing characterization.
S5 - Docs. `Poly/Interpretation/Vm/README.md` (its register and `_regN` text changes) and
`docs/interpretation/vm-abi-reference.md`, in the stage that changes each part.

## 6. Gates
- Full suite green at the parent gate, with the pass count recorded.
- `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`: 0 warnings.
- Performance: `dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium`
  (the command IS3, IS5, and ISB used). NQueens, Mandelbrot, Sieve, and Collatz gate
  (`ProgramBenchmarks.cs:132-142`). The regression threshold comes from the S0 spread, not a
  proposal. `--job short` is not used (IS-README: up to 2.3x spread).
- `scripts/restart-poly-mcp.sh` after S1 and S2.

## 7. Lanes and admission
- IS3, IS5, IS7a, IS7b are landed (`IS-README.md`) and form the baseline.
- The open work is the ABI lane. ABI1-ABI6 are admitted (`board.md:79-84`), and this design needs
  its own row. ABI6 residual 1 is D1. Residual 2 is S4. Residual 3 is left as it is. Residual 4 is
  a 2c exception group, kept in place.

## 8. Decisions (recorded)
D1. Nullable locals stay typed: `NullableLocal<T>` keeps the nullable CLR type in its local, as
    `SlotType` does today. No shape change. (Read from the reply "Nullable" as the typed option.)
D2. Block-exit snapshot is kept, since the three post-execution `GetLocals` tests require it.
D3. A suspend with a live temp spills the temp and reloads it at the resume label.
D4. Suspend inside `try`, `catch`, `finally`, `using`, or `foreach` is rejected with a positioned
    error for now. Support is future work and needs its own design.
Agreed: the frame arena stays monotonic; name lookup fallbacks are removed in S4 after a
characterization test.

Renames (decided): `WordLocal` is `Int64Local`; `SmallWordLocal` is `Int32Local`; `BitsLocal` is
`IEEELocal`.

Integer family rule (decided): a local's family follows C# promotion. Every integral type that
promotes to `int` in an operation (`sbyte`, `byte`, `short`, `ushort`, `char`) and every `int` is an
`Int32Local`. `uint` is an `Int64Local`, because mixing it with `int` promotes to `long`. An enum
follows its underlying type. Today only `int` is typed (`FrameRepresentation.cs:118`). The others are
`long` locals, so this rule is a shape change, not a rename. It is its own stage (S1b), not part of
S1. Two requirements for it:
- Narrowing on store must match C#: a `short` assignment wraps the way `(short)` does. Each small type
  gets a red characterization test before the change, such as `(short)(s + 1)` wrapping at 32767.
- The change is reviewed as a snapshot diff, because each affected local's CLR type changes.

Decided (D5): `TemporalLocal` holds `DateTime`, `DateOnly`, and `TimeOnly`, with a single-word
encoding for each. `DateTimeOffset` is a special case of `TemporalLocal`, because it is 16 bytes and uses
a handle. `StructLocal` is the class for `decimal`, `Guid`, `Int128`, and `UInt128`, all with the handle
encoding. Today `DateTime` is a heap handle, and `RingCodecTests.cs:56` and `SlotInvariantsTests.cs:33`
pin that. The single-word encodings change behavior: `DateTime` is a `TemporalLocal` (a word) rather
than a handle, so those tests and the classifier answer change. That is a shape change in S1b, with
a reviewed snapshot and a benchmark gate, and the pinned tests are updated with that reasoning.
