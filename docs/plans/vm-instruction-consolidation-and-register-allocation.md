# VM Instruction Consolidation & Register Allocation

## Status: Proposed

## 1. Goals

- Reduce instruction types from ~56 to ~20 by merging redundant variants
- Eliminate the runtime stack model within basic blocks — replace with linear-scan register allocation
- Switch ABI from stack-passed arguments to register-passed arguments
- Keep calling convention boundary (caller-save spill to value stack on nested calls)
- Keep custom array ops (BatchReduce, CountBits, etc.) intact — they're macro-level and orthogonal
- Delete: DupOp/PopOp as regular instructions, 9 separate ImmOp types, 6 split var-access types, DLE pass, Heuristic pass
- PopCount/PushCount stay — allocator and metadata need them

## 2. VmState Changes

Add a register file for suspend/resume:

```csharp
public sealed class VmState : IDisposable {
    // existing fields...
    
    /// <summary>Register file used by the compiled delegate as the
    /// suspend/resume bridge.  Normal execution uses CLR locals;
    /// this array is only written on suspend and read on resume.
    /// Sized at MAX_REGISTERS (32).</summary>
    public object?[]? Registers { get; set; }
}
```

Allocation: `state.Registers = new object?[maxPoolSize]` during bytecode init (or lazily on first suspend).

`VmState.Reset()` sets `Registers = null` for fresh start.

### 2.1 Register file sizing

`state.Registers` is always max size (32). The CLR locals used during normal execution are `ParameterExpression[]` sized to `clamp(maxLiveIntervals, 4, 32)` for the current program.

### 2.2 Register conventions

| Register | Convention |
|---|---|
| 0 | Return value / first argument |
| 1..N-1 | Arguments (in order) |
| N..31 | General purpose |

## 2. Consolidated Instruction Set

### 2.1 Value access (was 8 types → 3)

Unify LoadLocalOp, LoadArgOp (slot reads) and their store counterparts:

```
LoadSlot(int Offset, NodeId? AstSource = null)
StoreSlot(int Offset, NodeId? AstSource = null)
```

Offset is frame-relative, computed during lowering (fb + cas + 1 + local_index, fb + arg_index, etc.). The instruction doesn't know what "kind" of slot it is — just a flat index into the frame.

`IncLocalOp` stays as a fused read-modify-write:
```
IncSlot(int Offset, long Increment, NodeId? AstSource = null)
```

Upvalues (closure captures) are NOT slot-based — they're heap-allocated in the closure object. Use `LoadCapture(int CaptureIndex)` / `StoreCapture(int CaptureIndex)` — they read/write the closure's capture array at a fixed index. The closure reference itself comes from a register (the result of `AllocClosure`).

### 2.2 Stack manip (was 3 → 0 as instructions)

`DupOp` and `PopOp` are no longer instructions. The register allocator handles any required value duplication or discard as part of liveness (dup → two consumers of the same producer; pop → unused producer = no interval needed).

`PushOp(Value)` stays for constants: `LoadConst(long Value)`.

### 2.3 Binary/unary ops (was 33 → 4)

**Current:** 15 binary types with optional Immediate, 9 separate ImmOp types, 3 unary types, 6 comparison types.
**Consolidated:**

```
BinOp(BinOpKind Kind, long? Immediate = null, NodeId? AstSource = null)
```

`BinOpKind` enum:
```csharp
enum BinOpKind { Add, Sub, Mul, Div, Mod, And, Or, Xor, Shl, Shr,
                 Eq, Ne, Lt, Le, Gt, Ge }
```

When `Immediate` is non-null, the RHS operand is the immediate value (popCount=1). When null, both operands come from the register inputs (popCount=2). The instruction body is a single switch on `Kind`.

```
UnaryOp(UnaryOpKind Kind, NodeId? AstSource = null)
```

`UnaryOpKind` enum: `Neg, Not, BitNot`.

### 2.4 Control flow (stays 4 types)

```
Jump(int Target)
BranchIfFalse(int Target)
Return
ReturnFromCall(int ArgSlots)
```

### 2.5 Calls (stays 3 types)

```
Call(int FuncIndex, int ArgSlots)
CallClosure
CallExternal(int SiteIndex)
```

### 2.6 Added: BreakpointCheck

```
BreakpointCheck(int NodeId, NodeId? AstSource = null)
```

Emitted by lowering at AST node boundaries (one per AST node, not one per µop). Has PopCount=0, PushCount=0 — it's a pure gate instruction. Its `ToExpression` generates the suspend sequence:

```csharp
if (state.DebugMode && state.BreakpointPCs.Contains(NodeId)) {
    // Spill live CLR locals to state.Registers[]
    state.Registers[0] = reg0;
    state.Registers[1] = reg1;
    // ...
    state.SavedPC = this_pc;   // µop PC of this instruction
    state.Status = Suspended;
    goto exit;                 // exit delegate
}
```

When `DebugMode` is false (production), the JIT sees a single false boolean check — branch predicts "not taken," the entire body is a cold path that never executes.

Lowering emits it before the first µop of each AST node. This replaces the runtime `DebugMode && BreakpointPCs.Contains(pc)` at every instruction label.

### 2.7 Array / heap (stays 6 types — domain macros)

```
NewArray(int? Size, string? Alias)
ArrayLoad(string? Alias)
ArrayStore(string? Alias)
BatchReduce(...)
CountBits(string? Alias)
StridedSet(string? Alias)
```

### 2.7 Other

```
Nop — placeholder (breakpoint marker, deleted CSE remnants, etc.)
Throw
EndFinally
AllocClosure(int FuncIndex, int CaptureCount)
LoadCapture(int CaptureIndex)
StoreCapture(int CaptureIndex)
```

### 2.8 Removed entirely

| Type | Fate |
|---|---|
| AddImmOp, SubImmOp, MulImmOp, EqImmOp, LtImmOp, LeImmOp | Subsumed by `BinOp(Kind, Immediate)` |
| NegImmOp, NotImmOp, BitNotImmOp | Subsumed by `UnaryOp(Kind, Immediate)` |
| PopOp, DupOp | Handled by allocator |
| LoadLocalOp, LoadArgOp | LoadSlot |
| StoreLocalOp, StoreArgOp | StoreSlot |
| LoadUpvalueOp, StoreUpvalueOp | LoadCapture/StoreCapture |
| LoadValueOp, StoreValueOp | LoadSlot/StoreSlot (value is offset 0) |
| CmpLocalLeOp | Subsumed by BinOp + dead code |
| CommentOp | Already dead |
| DivRemOp | Subsumed by BinOp(Div) + BinOp(Mod) |
| IncLocalOp | IncSlot |
| All separate ImmOp types | (covered above) |

**Total: ~56 → ~22**

## 3. Calling Convention

### 3.1 Current (stack-based ABI)

```
// Caller pushes args to value stack
slots[sp++] = arg0;
slots[sp++] = arg1;
state.PC = callee_entry;
break;  // exit to resume loop

// Callee reads from frame
reg0 = slots[fb + 0];
reg1 = slots[fb + 1];
```

### 3.2 New (register-based ABI)

```
// Caller places args in registers
state.Registers[0] = arg0;
state.Registers[1] = arg1;
state.PC = callee_entry;
break;

// Callee entry — args are already in state.Registers[0..N-1]
// The entry switch reloads them to CLR locals:
reg0 = state.Registers[0];
reg1 = state.Registers[1];
// ...dispatch to instruction label...
```

The value stack frame shrinks to a spill area. It's only touched for caller-save spills at nested call sites and during breakpoint suspend/resume.

### 3.3 Caller-save at nested calls

When Poly calls Poly, the callee's entry reloads from `state.Registers[]`, overwriting whatever the caller had there. The caller must save its live registers to the value stack before each call:

```
// BEFORE call — spill caller's live registers to value stack
// (identified by liveness pass: intervals live across this call)
slots[fb + spill_offset + 0] = reg2;
slots[fb + spill_offset + 1] = reg4;

// Place args in registers
state.Registers[0] = reg1;
state.Registers[1] = reg3;
state.PC = callee_entry;
break;

// AFTER call return — entry switch dispatches to resume label
// Reload caller's live registers from value stack
reg2 = slots[fb + spill_offset + 0];
reg4 = slots[fb + spill_offset + 1];
```

With depth ≤ 4, the spill set is at most 3-4 registers. The value stack frame provides the storage slots at deterministic offsets computed by the allocator.

### 3.4 CLR calls (CallExternalOp)

CallExternalOp bridges to CLR methods. The register ABI doesn't apply — CLR methods expect arguments on the managed stack. The compiler generates a standard `Expression.Call(...)` with the args sourced from registers:

```csharp
// Load args from their assigned registers
var clrArgs = new[] { reg1, reg2, reg3 };
// Emit CLR call
Expression.Call(targetMethod, clrArgs);
```

Return value goes into the result register. No value stack involvement.

## 4. Liveness Analysis Pass

### 4.1 Purpose

Compute the live interval for every value-producing instruction, then assign each interval to a virtual register via linear scan.

### 4.2 Inputs

- `IReadOnlyList<Instruction>` — the optimized instruction sequence
- `InstructionMetadataStore` with `ProducersMetadata` already populated (already exists)

`ProducersMetadata[pc]` is an `int[]` of length `PopCount`, each entry the producer PC for that operand. This is the def-use chain we need.

### 4.3 Algorithm

```csharp
// Phase 1: Build intervals
struct LiveInterval {
    int StartPC;        // where value is produced
    int EndPC;          // where value is last consumed (exclusive)
    int ProducerPC;     // the instruction that defines it
}

// Walk forward. For each instruction at PC:
//   If PushCount > 0, create interval starting at PC, ending at 0 (unknown yet)
//   For each producer in ProducersMetadata[pc]:
//     Extend that producer's interval to end at pc+1
//   (Pseudo-interval for phi: extended across all predecessor blocks)

// Phase 2: Linear scan
// Sort intervals by StartPC
// For each interval in order:
//   Free all registers whose intervals ended ≤ current StartPC
//   If a free register exists, assign it
//   If not, spill (should never happen: max live ≤ reserved register count)
```

### 4.4 Block boundaries and phi resolution

At jump targets, intervals may join from multiple predecessors. Each predecessor may have the same value in a different register.

**Approach: per-PC live-set reload.**

The liveness pass identifies, for each PC, exactly which registers are live-in (consumed by the current instruction or later). At each jump target:

1. No spill before the jump — the predecessor's registers are valid
2. After the jump target label, reload ONLY the registers that differ between predecessors

Determining "differing registers" requires comparing live sets across all predecessors of a block. For depth ≤ 4 this is cheap — each predecessor's live set is a small bitmask.

**Simplification for first implementation:**

Since the entry switch already dispatches per-PC, each case reloads the live-in registers for that specific PC from `state.Registers[]`. This is correct even if some registers haven't changed — reloading an unchanged register is a single array read (cold path at entry, warm path on subsequent loop iterations).

For backward jumps (loop back-edges), a phi-resync block is emitted at the loop header label, loading any register that differs between the loop entry and the back-edge. With depth ≤ 4, this is at most 4 load instructions.

**Cost:** Zero spill traffic within basic blocks. O(live_set) reloads at block entry points (≤ 4 per entry in practice).

### 4.5 Output: RegisterAssignmentMetadata

```csharp
// Per-instruction: which register holds each operand, and which register holds the result
record RegisterAssignmentMetadata(int[] OperandRegisters, int ResultRegister);
// OperandRegisters[i] = register for the i-th operand (by consumer order in ProducersMetadata)
// ResultRegister = register for the produced value (or -1 if PushCount == 0)
```

Stored in `InstructionMetadataStore` keyed by `typeof(RegisterAssignmentMetadata)`.

## 5. Compilation Context Rewrite

### 5.1 Before (stack model)

```csharp
_ss[_depth++] = value;   // Push — assign to CLR local at current depth
_ss[--_depth];           // Pop — read CLR local, decrement depth
_ss[_depth - 1];         // Top — read without popping
SyncToSlots/SyncFromSlots at block boundaries
```

### 5.2 After (register model)

```csharp
ParameterExpression[] _regs;   // CLR locals: reg0, reg1, ..., regN
RegisterAssignmentMetadata _assignment;  // per-PC

// Compile instruction at PC:
void CompileAt(int pc) {
    var meta = _assignment[pc];
    var op = instructions[pc];
    
    // Read operands from assigned registers
    var inputs = meta.OperandRegisters.Select(r => _regs[r]);
    var expr = op.ToExpression(inputs);  // new signature: takes operands, not Push/Pop
    
    // Write result to assigned register
    if (meta.ResultRegister >= 0)
        Append(Expression.Assign(_regs[meta.ResultRegister], expr));
}
```

### 5.3 What changes in each instruction

Each `ToExpression` becomes a pure value transformer — no control flow, no stack access:

```csharp
// NEW: returns the computation. No Push/Pop, no SP manipulation, no PC modification.
// Control flow (gotos, branches, calls) is handled by the compiler, not the instruction.
public override Expression ToExpression(IReadOnlyList<Expression> inputs) =>
    inputs.Count == 0 ? Expression.Constant(Value) : Expression.Add(inputs[0], inputs[1]);
```

### 5.4 What compiles per instruction

| Instruction | Inputs | Output | Control flow by compiler |
|---|---|---|---|
| LoadConst(5) | — | `const 5` | `goto next` |
| LoadSlot(off) | — | `slots[off]` | `goto next` |
| StoreSlot(off) | val | — | `goto next` |
| BinOp(Add) | a, b | `a + b` | `goto next` |
| BinOp(Add, 5) | a | `a + 5` | `goto next` |
| BranchIfFalse(t) | cond | — | `if (cond == 0) goto t; else goto next` |
| Jump(t) | — | — | `goto t` |
| Call(idx, n) | args[0..n-1] | return value | set state.PC, `break` |
| Return | — | — | `break` (exit delegate) |
| ReturnFromCall(n) | retval | — | set state.PC from call stack, `break` |

### 5.5 Goto-based dispatch (replaces loop-switch)

Instead of a loop that re-dispatches PC through a switch on every iteration:

```csharp
// BEFORE: loop-switch model
while (pc < count) {
    switch (pc) {
        case 0: /* body */ pc = 1; break;
        case 1: /* body */ pc = 2; break;
        ...
    }
}

// AFTER: goto-chaining with entry resumption switch
// Entry: dispatch incoming PC to the right label
switch (pc) {
    case 0: goto pc_0;
    case 1: goto pc_1;
    ...
}

// Each label is a basic block — falls through or branches directly
pc_0:
    reg0 = loadconst 5;
    goto pc_1;

pc_1:
    reg1 = loadlocal 0;
    reg2 = binop(add, reg0, reg1);
    if (reg2 == 0) goto pc_exit;
    goto pc_2;

pc_exit:
    return;
```

#### Implementation in expression trees

```csharp
// Per-instruction label targets
var labels = new LabelTarget[instructionCount];
for (int i = 0; i < instructionCount; i++)
    labels[i] = Expression.Label($"pc_{i}");

// Entry resumption switch — maps incoming PC to a jump
var resumptionSwitch = Expression.Switch(
    pcVar,
    Expression.Break(exitTarget),  // default (pc >= count): exit
    Enumerable.Range(0, instructionCount)
        .Select(i => Expression.SwitchCase(
            Expression.Goto(labels[i]), Expression.Constant(i))));

// Build body: resumption switch + all instruction labels
var body = new List<Expression> { resumptionSwitch };

for (int i = 0; i < instructionCount; i++) {
    body.Add(Expression.Label(labels[i]));

    var meta = assignment[i];
    var op = instructions[i];
    var operands = meta.OperandRegisters.Select(r => _regs[r]).ToArray();
    var resultExpr = op.ToExpression(operands);

    // Write result register
    if (meta.ResultRegister >= 0)
        body.Add(Expression.Assign(_regs[meta.ResultRegister], resultExpr));

    // Control flow — compiler handles it, not the instruction
    switch (op) {
        case JumpOp jmp:
            body.Add(Expression.Goto(labels[jmp.Target]));
            break;
        case BranchIfFalseOp bif:
            body.Add(Expression.IfThenElse(
                Expression.Equal(operands[0], Expression.Constant(0L)),
                Expression.Goto(labels[bif.Target]),
                Expression.Goto(labels[i + 1])));
            break;
        case ReturnOp or ReturnFromCallOp or CallOp or CallClosureOp:
            body.Add(Expression.Break(exitTarget));
            break;
        default:
            body.Add(Expression.Goto(labels[i + 1]));
            break;
    }
}

var compiled = Expression.Lambda<Action<VmState>>(
    Expression.Block(variables, body), stateParam).Compile();
```

#### Benefits

- Straight-line sequences: `goto pc_N` instead of `pc++ → switch(pc)`
- Branches: conditional goto directly — no re-dispatch
- Calls: set state.PC, `break` to exit (resumption switch handles the re-entry on return)
- No bounds check per instruction (only the entry switch hits one)
- The JIT sees direct branches — better branch prediction

#### Resumption entry

The entry switch handles initial execution AND any re-entry after a call return or breakpoint resume. The `state.PC` value determines the label.

```
state.PC is set externally → entry switch → goto right label → execute
→ on call: set state.PC, break out → wait for return → state.PC updated → entry switch → goto right label
```

### 5.6 Suspend / Resume (breakpoints)

When a breakpoint is hit, the delegate must preserve its state for later resumption.

**Suspend sequence (at the breakpoint label):**

```csharp
// Spill CLR locals to state.Registers[]
state.Registers[0] = reg0;
state.Registers[1] = reg1;
// ... (only live registers at this PC — identified by liveness pass)
state.PC = current_pc;     // save resume point
state.Status = Suspended;
break;                     // exit delegate
```

**Resume sequence (on next `Vm.Execute` call):**

```csharp
// The compiled delegate's entry switch reads state.PC to find the right label.
// Each entry switch case starts by reloading the live registers for that PC:
// case 42:
//   reg0 = state.Registers[0];
//   reg1 = state.Registers[1];
//   goto pc_42;
```

The liveness pass identifies which registers need spilling at each potential breakpoint. Since breakpoints are cold paths, spilling all 32 registers (worst case) is acceptable — it's O(32) and happens at most a few times per execution.

#### Step-through support

Stepping is implemented by the debugger setting a breakpoint at the next PC and resuming. The goto model naturally supports this:

1. User sets a breakpoint at PC 30
2. Execution hits label `pc_30`, the breakpoint check fires
3. Suspend: spill registers, save `state.PC = 30`, set `Status = Suspended`, exit
4. User inspects state
5. User presses "step" → debugger sets a temporary breakpoint at PC 31, calls `Vm.Execute()`
6. Entry switch dispatches to `pc_30`, reloads registers, executes the instruction
7. `goto pc_31` — execution continues to the next label
8. At `pc_31`, the breakpoint check fires again
9. Suspend again with `state.PC = 31`

The "over" (step over call) and "out" (step out of call) variants use the same mechanism — set breakpoints at return addresses and resume.

### 5.7 Profiling builds (opt-in)

Profiling increments are ONLY emitted when the caller requests them (e.g., `ProgramCompiler.Compile(instructions, profiling: true)`). When false (default), the compiled delegate has zero profiling code — no counters, no branches, no conditionals.

When enabled, each instruction label starts with a counter increment:

```csharp
pc_42:
    state.InstructionCounters[42]++;   // unconditional, compile-time constant index
    reg2 = reg0 + reg1;
    goto pc_43;
```

The counter index is the PC — known at compile time, so this is an unconditional array store with no bounds check (the array is sized to instruction count at allocation time).

Since profiling is a developer tool, the code size increase (one array store per instruction) is acceptable. In production builds, the delegate has exactly zero profiling overhead.

### 5.8 Breakpoint check placement

`BreakpointCheck` is a regular instruction emitted by lowering at AST node boundaries. The compiler treats it like any other instruction — it has its own label, the compiler handles its control flow (goto next), and its `ToExpression` generates the suspend check.

The generated code for a `BreakpointCheck` at label pc_N:

```csharp
pc_N:
    // Profiling (optional):
    //   state.InstructionCounters[N]++;

    // Breakpoint gate:
    // (generated by BreakpointCheck.ToExpression)
    if (state.DebugMode && state.BreakpointPCs.Contains(nodeId)) {
        state.Registers[0] = reg0;  // spill live registers
        state.Registers[1] = reg1;
        // ... (only registers live at this PC)
        state.SavedPC = N;          // save resume PC
        state.Status = Suspended;
        goto exit;                  // exit delegate
    }

    // Fall through to next instruction (BreakpointCheck produces no value)
    goto pc_N_plus_1;
```

When `DebugMode` is false (production), the JIT's branch predictor predicts "not taken" — the entire spill block is never reached. The profiler increments are also absent in non-profiling builds. The only cost is the unconditional `goto` to the next label, which is the same cost as any straight-line instruction.

### 5.9 Block boundary spill

Before any label that's a jump target (including the entry switch cases), the compiler emits spill/reload code for registers that cross the boundary. This is identified during the liveness pass — any interval whose start or end crosses a block boundary needs spilling.

Spill happens BEFORE the jump (in the predecessor block). Reload happens AFTER the jump target label.

### 5.10 Frame layout

The value stack frame is sized to max depth (from ProducersMetadata), used as a spill area for:
- Caller-save at nested call sites
- Block boundary spilling (phi resolution)

The register-based ABI moves arguments through `state.Registers[]`, not the value stack. The value stack is purely a spill/resume mechanism.

## 6. CSE Interaction

The CSE pass runs before the allocator (existing pipeline order). It may:
- Introduce new producer instructions (common subexpression results)
- Remove redundant instructions (fewer consumers)

The allocator sees the post-CSE instruction sequence and produces correct intervals regardless. No special handling needed.

## 7. Phases & Migration

### Phase 0 — VmState (start of session)
1. Add `Registers` field to `VmState`
2. Set up allocation on bytecode load

### Phase A — Instruction consolidation (1 session)
1. Define the new instruction types in `Instructions2.cs`
2. Define `BinOpKind` and `UnaryOpKind` enums
3. Add `ToExpression(IReadOnlyList<Expression>)` to base `Instruction` class
4. Write new `ToExpression` bodies for each new type (pure value transformers)

### Phase B — Register allocator (1 session)
1. Build liveness pass — compute live intervals from ProducersMetadata
2. Build linear scan — assign registers
3. Store `RegisterAssignmentMetadata` per PC

### Phase C — Compilation context rewrite (1 session)
1. Replace `CompilationContext` with register-based model
2. Add goto-based dispatch (label targets, resumption switch)
3. Add spill/reload at block boundaries
4. Add suspend/resume bridge (state.Registers[])

### Phase D — Lowering adaptation (1 session)
1. Update `Lowering.cs` to emit new instruction types
2. Remove old instruction types from `Instructions.cs`

### Phase E — Deletions (0.5 session)
1. Delete old `ToExpression(CompilationContext)` signatures
2. Delete DLE pass, heuristic pass
3. Delete removed instruction records
4. Update tests

## 8. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Phi resolution via spill-all at block boundaries | Simple, correct, depth ≤ 4 keeps cost negligible |
| 2 | Keep IncLocalOp fused as IncSlot | Common pattern |
| 3 | Register-based ABI for arguments | Eliminates value stack traffic on calls |
| 4 | Caller-save spill at nested calls | Depth ≤ 4 → ≤ 4 registers to save |
| 5 | state.Registers[] as suspend/resume bridge | CLR locals for hot path, array for cold |
| 6 | Pool size = clamp(maxLiveIntervals, 4, 32) | Safety floor, reasonable ceiling, adaptive |
| 7 | Incremental adoption, break early | Minimizes coordination cost |
| 8 | PopCount/PushCount stay on Instruction | Needed by allocator and metadata |
| 9 | Separate LoadCapture/StoreCapture for upvalues | Not frame-relative (heap-allocated) |
| 10 | External calls use Expression.Call directly | Args come from registers, no value stack |


