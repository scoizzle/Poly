# IR Lowering Redesign: InstructionSequence + Metadata

## Problem

The µop layer (now "Instruction" layer) has a structural problem: the instruction array carries only raw data, with no data-flow information. Every downstream consumer re-derives what lowering already knew:

- `ProgramCompiler.Compile` re-computes stack depth at every PC via a `StackEffect()` switch
- `InstructionHeuristicPass` duplicates the same `StackEffect()` switch for stack simulation
- `InstructionCsePass` re-implements pattern matching (`OpsEqual`, `IsPure`, `IsSafeToHoist`) to find def-use relationships

Three independent re-derivations of the same information. Each consumer reverse-engineers what the producer already knew.

The root cause: the instruction array is a passive sequence with no metadata layer. The AST side solved this problem (analysis passes → `NodeMetadataStore`), but the instruction side never got the same treatment.

## Solution

Two structural changes:

1. **Intrinsic ABI on the type level** — every `Instruction` subclass reports `PopCount` and `PushCount` as abstract properties. These are the ABI-level stack effects, fixed per instruction type. Eliminates the duplicated `StackEffect` switches.

2. **Extrinsic data flow in a metadata store** — `InstructionMetadataStore`, a per-PC store (same pattern as `NodeMetadataStore`, but keyed by int). Populated during lowering via a shadow stack. Producers, depth, block boundaries live here. Passes query the store instead of re-deriving.

The two travel together in `InstructionSequence` — wraps `Instruction[]` + `InstructionMetadataStore`.

## Pipeline

```
Lowering (EmitNode + shadow stack)
    │
    ▼
InstructionSequence {
    Instruction[]              ← PopCount/PushCount intrinsic
    InstructionMetadataStore   ← producers, depth, block boundaries (extrinsic)
}
    │
    ▼
Instruction Passes (Heuristic, CSE, SSA)  ← read/write metadata, no re-derivation
    │
    ▼
Compilation (reads depth, blocks, producers from metadata)  ← no StackEffect switch
    │
    ▼
Action<VmState>
```

## Phases

### Phase 1 — Rename + Intrinsic ABI

- `MicroOp` → `Instruction` (abstract record)
- `PopCount { get; }` and `PushCount { get; }` on `Instruction`, implemented by each concrete type
- `Source` → `AstSource`
- `MicroOperations.cs` → `Instructions.cs`
- `OpCode.cs` → delete (it's empty)
- `IUopPass` → `IInstructionPass`, `UopOptimizer` → `InstructionOptimizer`, etc.
- All local variable names: `uop`/`uops` → `instruction`/`instructions`

Zero behavioral change. The duplicated `StackEffect` switches remain until phases 4-6.

**Invariants:**
- Reflection test: iterates every concrete `Instruction` type and asserts `PopCount`/`PushCount` match the values `ProgramCompiler.StackEffect()` used to return
- Every `Instruction` subclass's `PopCount`/`PushCount` is correct by construction (test)

### Phase 2 — New Infrastructure

Three new files, zero existing files touched.

- `InstructionSequence` — wraps `Instruction[]` + `InstructionMetadataStore`
- `InstructionMetadataStore` — `List<Dictionary<Type, object>?>` indexed by PC. `Set<T>(int pc, T data)`, `Get<T>(int pc)`, `Invalidate(int pc)`, `Reset()`
- Metadata records: `ProducersMetadata(int[] ProducerPcs)`, `StackDepthMetadata(int Depth)`, `BlockBoundaryMetadata(bool IsBlockStart, bool IsBlockEnd)`

**Invariants:**
- `Debug.Assert` in `InstructionSequence` constructor: `instructions.Length == metadata.Length`
- `Debug.Assert` in `InstructionMetadataStore.Set<T>`/`Get<T>`: `pc >= 0 && pc < _store.Count`
- Unit tests: Set/Get roundtrip, Get unset returns null, Invalidate clears, Set overwrites, out-of-range throws

### Phase 3 — Shadow Stack in Lowering

- `EmitContext` adds `Stack<int> _valueStack` tracking producer instruction indices
- Each `EmitOp` call pops N producers from shadow stack, sets `ProducersMetadata` on store, pushes the new producer index
- Records `StackDepthMetadata` and `BlockBoundaryMetadata` per PC
- `Lowering.Lower()` returns `InstructionSequence` instead of raw `List<MicroOp>`
- `Bytecode` wraps `InstructionSequence` internally instead of `IReadOnlyList<MicroOp>`

**Invariants (Debug.Asserts after every EmitOp):**
- `_valueStack.Count >= popCount` (stack underflow guard)
- `store.Get<ProducersMetadata>(pc).ProducerPcs.Length == popCount`
- After push > 0: `_valueStack.Peek() == pc`
- After `Lower()` returns: `_valueStack.Count == 0`
- All producer PCs in `ProducersMetadata` are < current PC

**Tests:**
| Test | What it verifies |
|------|------------------|
| `Binary_Expression_Producers` | `a + b` → add.ConsumedFrom == [load_a_pc, load_b_pc] |
| `If_Statement_BlockBoundaries` | if(c) { x } → block bounded by JumpIfFalse/Jump |
| `While_Loop_Producers` | Loop carry dependencies appear in metadata |
| `ShortCircuit_Producers` | `a && b` → correct producer chains across dup/jump |
| `IncLocal_Fusion_Producers` | `i = i + 1` → IncLocalOp consumes and produces correctly |
| `Return_Value_Producer` | `return x` → ReturnFromCallOp.ConsumedFrom[0] is the load of x |
| `Sequence_Ends_With_Empty_Stack` | Every valid lowering yields empty shadow stack |

### Phase 4 — Compilation Consumes Metadata

- `ProgramCompiler.Compile(InstructionSequence)` reads `StackDepthMetadata` from store at each PC instead of re-computing via `StackEffect()` switch
- Reads `BlockBoundaryMetadata` instead of `IsBlockEnd()` type checks
- **Removes:** `ProgramCompiler.StackEffect()` — the single canonical source is now `Instruction.PopCount`/`PushCount`

**Transition (Debug.Assert):** compute depth both ways, assert they match at every PC before removing the old switch.

**Tests:**
- `All_Instruction_Types_Compile_Correctly` — every concrete `Instruction` type compiles to a delegate that executes without error
- End-to-end integration tests pass identically before and after

### Phase 5 — Heuristic Pass Consumes Metadata

- `DataFlowSameLocalBinary` reads `ProducersMetadata` from store instead of re-simulating stack depth and scanning forward
- `LoadLoadSameCommutativeBinary` and `UnaryThenCommutativeBinary` read adjacency from the instruction array (unchanged)
- **Removes:** `InstructionHeuristicPass.StackEffect()` — the second duplicate

**Transition (Debug.Assert):** run both old and new detection, assert `DataFlowSameLocalBinary` fires on the same sequences.

**Tests:**
- `HeuristicPass_DataFlow_Detects_SameLocal` — specific instruction sequence, asserts metadata-based detection matches old behavior

### Phase 6 — CSE Pass Uses Inline IsPure

- Replaced `PureTypes` whitelist with an inline switch in `IsPure` that enumerates pure instruction types
- Kept `OpsEqual`, `IsSafeToHoist`, `CollectReadLocals` — these are not `StackEffect` duplicates and serve distinct purposes (structural equality vs stack effect)
- Full def-use chain rewrite of the CSE pass deferred until SSA construction is available (producer indices are absolute, so `ProducersMetadata` can't directly replace structural equality between condition and body sequences)

**Removes:** `PureTypes` (the whitelist set)

**Tests:**
- `CsePass_Hoists_LoopInvariant` — known loop pattern, asserts hoisted instructions match old behavior

## Dependency Order

```
Phase 1 ──> Phase 2 ──> Phase 3 ──> Phase 4
                              │
                              ├──> Phase 5
                              └──> Phase 6
```

Phases 4-6 are independent after Phase 3.

## Assertions vs Tests

| Location | Assert or Test |
|----------|---------------|
| `Instruction.PopCount`/`PushCount` per type | Reflection test |
| `InstructionSequence` constructor bounds | `Debug.Assert` |
| `InstructionMetadataStore` all public methods | `Debug.Assert` |
| `EmitOp` after every emission | `Debug.Assert` |
| `Lower()` return | `Debug.Assert` |
| `ProgramCompiler.Compile` transition | `Debug.Assert` |
| `DataFlowSameLocalBinary` transition | `Debug.Assert` |
| `InstructionCsePass` transition | `Debug.Assert` |
| Structural lowering patterns (binary, if, while) | Unit tests |
| Every concrete instruction type | Reflection test |
| Compilation end-to-end | Integration test |
