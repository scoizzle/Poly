> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Plan: Iterative Worklist Lowering

**Date:** 2026-06-08  
**Status:** Draft  
**Goal:** Replace recursive `Emit` with a `Stack<EmitWork>`. No C# stack growth from AST depth. Enables pause/resume for incremental lowering.

## Data structures

```csharp
private readonly record struct EmitWork(
    Node? Node,
    EmitPhase Phase,
    int Data = 0,          // opcode, store index, func index, tryStart, EmitsValue flag
    string? Label = null,  // primary label for Mark/Jump/JumpIfFalse, catchStart label
    string? Label2 = null  // secondary label (finallyEntry/end for TryCatchFinally)
);

private enum EmitPhase : byte {
    Enter,            // first visit: source map, push children + structural work
    AfterChildren,    // all children done: emit opcode(s), pop loop labels, record exception region
    MarkLabel,        // ctx.Labels.Mark
    Jump,             // ctx.Labels.JumpTo
    JumpIfFalse,      // ctx.Labels.JumpIfFalseTo
    Pop,              // ctx.Code.Add(Pop)
    Dup,              // ctx.Code.Add(Dup)
}
```

## Top-level loop (replaces `Emit(root, ref ctx, lambdaState)`)

```csharp
var worklist = new Stack<EmitWork>();
worklist.Push(new EmitWork(root, EmitPhase.Enter));

while (worklist.TryPop(out var work)) {
    switch (work.Phase) {
        case EmitPhase.Enter:
            EnterNode(work.Node!, worklist, ref ctx, lambdaState);
            break;
        case EmitPhase.AfterChildren:
            AfterChildren(work.Node!, work, worklist, ref ctx, lambdaState);
            break;
        case EmitPhase.MarkLabel:
            ctx.Labels!.Mark(work.Label!, ctx.Code);
            break;
        case EmitPhase.Jump:
            ctx.Labels!.JumpTo(work.Label!, ctx.Code, ctx.Relocations);
            break;
        case EmitPhase.JumpIfFalse:
            ctx.Labels!.JumpIfFalseTo(work.Label!, ctx.Code, ctx.Relocations);
            break;
        case EmitPhase.Pop:
            ctx.Code.Add((byte)OpCode.Pop);
            break;
        case EmitPhase.Dup:
            ctx.Code.Add((byte)OpCode.Dup);
            break;
    }
}
```

## EnterNode — pushes work items, returns immediately

1. Handle replacement (recurse into replacement via worklist).
2. Leaf `Constant` — emit directly, return (no work items).
3. Source map entry.
4. Switch on node type to push work items.

### Leaf nodes (emit directly, no children pushed)

| Node | Action |
|------|--------|
| `Default` | `PushInt 0` |
| `Variable` | `LoadArg`/`LoadLocal`/`LoadUpvalue` by name |
| `Parameter` | `LoadArg`/`LoadLocal` or recurse into DefaultValue |
| `BreakStatement` | `Jump` to enclosing loop break label |
| `ContinueStatement` | `Jump` to enclosing loop continue label |
| `GotoStatement` | `Jump` to named label |
| `TypeAs`, `ParameterReference`, `ThisReference`, `TypeDefinitionNode` | no-op |

### Unary (push AfterChildren, push operand)

| Node | AfterChildren action |
|------|---------------------|
| `Not` | `ctx.Code.Add(Not)` |
| `Neg`/`UnaryMinus` | `DNeg` if double, else `Neg` |
| `TypeCast` | nothing |
| `NullForgiving` | nothing |
| `ThrowStatement` | `ctx.Code.Add(Throw)` |
| `Return` | nothing |
| `SuspendNode` | `Pop; Int 0` |
| `Await` | `PushInt 1; PushInt 1; CallExternal(idx)` |

### Binary (push AfterChildren, push right, push left)

`Data = (int)resolvedOp`. AfterChildren: `ctx.Code.Add((OpCode)Data)`.

String variant (`Data = -1`): AfterChildren emits `PushInt 2; StrConcat`.

### Short-circuit

`And`:
```
ctx.Labels ??= new LabelContext()
end = labels.Next()
push Mark(end)
push right
push Pop
push JumpIfFalse(end)
push Dup
push left
```

`Or`:
```
ctx.Labels ??= new LabelContext()
eval, after = labels.Next(), labels.Next()
push Mark(after)
push right
push Pop
push Mark(eval)
push Jump(after)
push JumpIfFalse(eval)
push Dup
push left
```

### Ternary

`Conditional`:
```
elseL, endL = labels.Next(), labels.Next()
push Mark(endL)
push false
push Mark(elseL)
push Jump(endL)
push true
push JumpIfFalse(elseL)
push condition
```

### Control flow

`IfStatement`:
```
elseL, endL = labels.Next(), labels.Next()
push Mark(endL)
if else: push else, push Mark(elseL)
push Jump(endL)
push then
push JumpIfFalse(elseL)
push condition
```

`WhileLoop`:
```
breakL, contL = labels.Next(), labels.Next()
loopLabel = ctx.Labels.PendingLoopLabel ?? ""; ctx.Labels.PendingLoopLabel = null
ctx.Labels.LoopLabels.Push((loopLabel, breakL, contL))
push AfterChildren  // LoopLabels.Pop()
push Mark(breakL)
push Jump(contL)
push body
push JumpIfFalse(breakL)
push Mark(contL)
push condition
```

`DoWhileLoop`: `Mark(contL), body, condition, JumpIfFalse(breakL), Jump(contL), Mark(breakL), AfterChildren(PopLoopLabels)`

`ForLoop`: init, then same as WhileLoop condition/body/increment pattern.

### Block

```
for i = nodes.Count - 1 down to 0:
    if i < nodes.Count - 1 && EmitsValue(nodes[i]): push Pop
    push Enter(nodes[i])
```

### Assignment

Simple variable dest: push AfterChildren (emits `Dup; StoreLocal/Arg/Upvalue`), push value.

Member/IndexAccess dest: delegate to helpers (see below).

### Lambda

```
if lambdaState?.FuncMap contains lambda:
    push AfterChildren  // AllocateClosure funcIdx captureCount
    for each capture in reverse:
        push LoadCapture  // LoadArg/LoadLocal/PushInt 0
```

### TryCatchFinally

```
ctx.Labels ??= new LabelContext()
int tryStart = ctx.Code.Count
string catchStartL = labels.Next()
string finallyEntryL = finally exists ? labels.Next() : null
string endL = labels.Next()
push AfterChildren(Data: tryStart, Label: catchStartL, Label2: finallyEntryL ?? endL)
push Mark(endL)
if finally:
    if EmitsValue(finally): push Pop
    push EndFinally(Data: 1)  // special: emit EndFinally + optional Pop
    push finally body
    push Mark(finallyEntryL)
for each catch in reverse:
    if finally is null: push Jump(endL)
    push catch body
    push StoreArg/Pop for catch variable
push Mark(catchStartL)
push Jump(finallyEntryL ?? endL)
push try body
```

AfterChildren reads `Labels.Targets[catchStartL]` and `Labels.Targets[finallyEntryL]` for region boundaries.

### LabelDeclaration

```
labels.Mark(lbl.Name, ctx.Code)
ctx.Labels.PendingLoopLabel = lbl.Name
push AfterChildren  // clear PendingLoopLabel
push statement
```

### ForEachLoop, UsingStatement, SwitchStatement, Coalesce, Invoke, Member, IndexAccess, New, TypeIs

Each pushes appropriate children + structural work items. Same logic as current helpers — just pushes worklist items instead of calling `Emit` recursively.

## AfterChildren — emits opcodes after children done

Dispatches on `work.Node` type. Reads `work.Data` for opcode/storeIndex/captureCount/etc. For TryCatchFinally, reads `work.Label`/`work.Label2` from Labels.Targets.

## Helper methods

`EmitBinary`, `EmitShortCircuitAnd`, `EmitShortCircuitOr`, `EmitConditional`, `EmitIf`, `EmitWhileLoop`, `EmitDoWhileLoop`, `EmitForLoop`, `EmitForEachLoop`, `EmitUsingStatement`, `EmitSwitch`, `EmitCoalesce`, `EmitInvoke`, `EmitMember`, `EmitIndexAccess`, `EmitNew`, `EmitAssignmentMember`, `EmitAssignmentIndexAccess`

Each receives `Stack<EmitWork>` and pushes work items instead of calling `Emit` recursively. The helpers become pure push-methods — they push the appropriate `EnterNode` work items for children and structural items (Mark, Jump, etc.) in reverse order, then return.

---

## Transform Walkthrough (sanity check)

### 1. Simple binary: `Add`

**Current recursive code:**

```csharp
case Add add:
    if (isString) {
        Emit(add.LeftHandValue, ref ctx, lambdaState);
        Emit(add.RightHandValue, ref ctx, lambdaState);
        ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 2);
        ctx.Code.Add((byte)OpCode.StrConcat);
    } else {
        EmitBinary(add.LeftHandValue, add.RightHandValue, resolvedOp, ref ctx, lambdaState);
    }
    return;

// EmitBinary:
void EmitBinary(Node l, Node r, OpCode op, ...) {
    Emit(l, ...); Emit(r, ...); ctx.Code.Add((byte)op);
}
```

**Recursive C# stack for `Add(Constant(3), Constant(4))`:**
```
Emit(Add)                 ← frame 1
  → Emit(Constant(3))     ← frame 2
  → returns
  → Emit(Constant(4))     ← frame 2
  → returns
  → ctx.Code.Add(Add)      ← frame 1
Depth: 2 frames.
```

**Worklist form:**

```csharp
// EnterNode:
case Add add:
    ctx.SourceMap[ctx.Code.Count] = node.Id;
    if (isString) {
        worklist.Push(new EmitWork(add, EmitPhase.AfterChildren, Data: -1)); // -1 = string
        worklist.Push(new EmitWork(add.RightHandValue, EmitPhase.Enter));
        worklist.Push(new EmitWork(add.LeftHandValue, EmitPhase.Enter));
    } else {
        worklist.Push(new EmitWork(add, EmitPhase.AfterChildren, Data: (int)resolvedOp));
        worklist.Push(new EmitWork(add.RightHandValue, EmitPhase.Enter));
        worklist.Push(new EmitWork(add.LeftHandValue, EmitPhase.Enter));
    }
    return; // ← returns immediately

// AfterChildren:
case Add add:
    if (work.Data == -1) { ctx.Code.Add((byte)OpCode.PushInt); EmitInt32(ctx.Code, 2); }
    ctx.Code.Add((byte)(OpCode)work.Data);
    return;
```

**Worklist execution:**
```
1: pop Enter(Add) → push AfterChildren, Enter(4), Enter(3)
   stack: [Enter(3), Enter(4), AfterChildren(Add)]
2: pop Enter(3)  → emit PushInt 3 directly
   stack: [Enter(4), AfterChildren(Add)]
3: pop Enter(4)  → emit PushInt 4 directly
   stack: [AfterChildren(Add)]
4: pop AfterChildren(Add) → emit Add opcode
   stack: empty
```

**Bytecode:** `PushInt 3, PushInt 4, Add` — identical.  
**C# stack depth:** always 1 (`DispatchWork`).

### 2. Short-circuit: `And`

**Current recursive:**

```csharp
void EmitShortCircuitAnd(And n, ...) {
    ctx.Labels ??= new LabelContext(); var labels = ctx.Labels;
    string end = labels.Next();
    Emit(n.LeftHandValue, ...);
    ctx.Code.Add((byte)OpCode.Dup);
    labels.JumpIfFalseTo(end, ...);
    ctx.Code.Add((byte)OpCode.Pop);
    Emit(n.RightHandValue, ...);
    labels.Mark(end, ...);
}
```

**Worklist form:**

```csharp
case And n:
    ctx.Labels ??= new LabelContext(); string end = ctx.Labels.Next();
    ctx.SourceMap[ctx.Code.Count] = node.Id;
    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: end));
    worklist.Push(new EmitWork(n.RightHandValue, EmitPhase.Enter));
    worklist.Push(new EmitWork(null, EmitPhase.Pop));
    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: end));
    worklist.Push(new EmitWork(null, EmitPhase.Dup));
    worklist.Push(new EmitWork(n.LeftHandValue, EmitPhase.Enter));
    return;
```

**Execution for `And(Constant(0), Constant(42))`:**
```
push order (bottom→top): Mark(end), Enter(42), Pop, JumpIfFalse, Dup, Enter(0)

pop order:
1. Enter(0)       → emit PushInt 0    → stack: [Dup, JumpIfFalse, Pop, Enter(42), Mark(end)]
2. Dup            → emit Dup          → stack: [JumpIfFalse, Pop, Enter(42), Mark(end)]
3. JumpIfFalse    → emit JumpIfFalse end → stack: [Pop, Enter(42), Mark(end)]
4. Pop            → emit Pop          → stack: [Enter(42), Mark(end)]
5. Enter(42)      → emit PushInt 42   → stack: [Mark(end)]
6. Mark(end)      → record code.Count → stack: empty
```

**Bytecode:** `PushInt 0, Dup, JumpIfFalse → L0, Pop, PushInt 42, L0:`. The VM determines at runtime whether PushInt 42 or just L0 executes. The lowering emits the same sequence regardless of values — identical to the recursive version.

### 3. Control flow: `WhileLoop`

**Current recursive:**

```csharp
void EmitWhileLoop(WhileLoop wl, ...) {
    ctx.Labels ??= new LabelContext(); var labels = ctx.Labels;
    string breakL = labels.Next(), contL = labels.Next();
    string loopLabel = labels.PendingLoopLabel ?? ""; labels.PendingLoopLabel = null;
    labels.LoopLabels.Push((loopLabel, breakL, contL));
    labels.Mark(contL, ctx.Code);
    Emit(wl.Condition, ...);
    labels.JumpIfFalseTo(breakL, ...);
    Emit(wl.Body, ...);
    labels.JumpTo(contL, ...);
    labels.Mark(breakL, ...);
    labels.LoopLabels.Pop();
}
```

**Worklist form:**

```csharp
case WhileLoop wl:
    ctx.Labels ??= new LabelContext();
    string breakL = ctx.Labels.Next(), contL = ctx.Labels.Next();
    string loopLabel = ctx.Labels.PendingLoopLabel ?? ""; ctx.Labels.PendingLoopLabel = null;
    ctx.Labels.LoopLabels.Push((loopLabel, breakL, contL));
    ctx.SourceMap[ctx.Code.Count] = node.Id;
    worklist.Push(new EmitWork(wl, EmitPhase.AfterChildren));  // LoopLabels.Pop()
    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: breakL));
    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: contL));
    worklist.Push(new EmitWork(wl.Body, EmitPhase.Enter));
    worklist.Push(new EmitWork(null, EmitPhase.JumpIfFalse, Label: breakL));
    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: contL));
    worklist.Push(new EmitWork(wl.Condition, EmitPhase.Enter));
    return;

// AfterChildren:
case WhileLoop: ctx.Labels!.LoopLabels.Pop(); return;
```

**Execution:**
```
pop order:
1. Enter(condition) → emit condition
2. Mark(contL)      → record code.Count
3. JumpIfFalse(breakL) → emit JumpIfFalse → breakL
4. Enter(body)      → emit body
5. Jump(contL)      → emit Jump → contL
6. Mark(breakL)     → record code.Count
7. AfterChildren    → LoopLabels.Pop()
```

**Bytecode:** `[condition, JumpIfFalse → L1, body, Jump → L0, L1:]` where L0=contL, L1=breakL. Identical.

### 4. TryCatchFinally — boundary capture

**Current recursive** captures boundaries by reading `ctx.Code.Count` at inline positions:

```csharp
int tryStart = ctx.Code.Count;       // before try
Emit(tcf.TryBlock, ...);
int tryEnd = ctx.Code.Count;         // after try
// ... emit jumps, catch clauses ...
int catchStart = ctx.Code.Count;     // after jumps, before first catch
// ... emit catches ...
// ... emit finally ...
ctx.Labels.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart, finallyStart));
```

**Worklist form** captures boundaries via labels marked at those same positions:

```csharp
case TryCatchFinally tcf:
    ctx.Labels ??= new LabelContext();
    int tryStart = ctx.Code.Count;
    string catchStartL = ctx.Labels.Next();
    string finallyEntryL = tcf.FinallyBlock is not null ? ctx.Labels.Next() : null;
    string endL = ctx.Labels.Next();
    ctx.SourceMap[ctx.Code.Count] = node.Id;
    worklist.Push(new EmitWork(tcf, EmitPhase.AfterChildren,
        Data: tryStart, Label: catchStartL, Label2: finallyEntryL ?? endL));
    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: endL));
    // ... finally, catches, jump, try body all pushed as work items ...
    worklist.Push(new EmitWork(null, EmitPhase.MarkLabel, Label: catchStartL));
    worklist.Push(new EmitWork(null, EmitPhase.Jump, Label: finallyEntryL ?? endL));
    worklist.Push(new EmitWork(tcf.TryBlock, EmitPhase.Enter));
    return;
```

AfterChildren reads:
```csharp
case TryCatchFinally tcf:
    int tryStart = work.Data;
    int tryEnd = ctx.Labels!.Targets[work.Label!];    // = catchStartL position
    int catchStart = ctx.Labels!.Targets[work.Label!]; // = position after try body + jump
    int? finallyStart = work.Label2 is not null ? ctx.Labels!.Targets[work.Label2] : null;
    ctx.Labels.ExceptionRegions.Add(new ExceptionRegion(tryStart, tryEnd, catchStart, finallyStart));
    return;
```

The labels resolve to the same PCs as the inline `ctx.Code.Count` reads — because `Mark(x)` is processed at the same code position where the recursive code would have read `ctx.Code.Count`.

---

## Migration steps

| Step | What |
|------|------|
| 1 | Add `Dup` phase to enum |
| 2 | Write `EnterNode` — all node-type cases |
| 3 | Write `AfterChildren` dispatcher |
| 4 | Update `Lower()` to use worklist loop |
| 5 | Convert all helper methods to push work items |
| 6 | Delete old recursive `Emit` |
| 7 | Build + run conformance suite |

## Verification

- All 1200 tests pass with identical bytecode output per input AST
- Deep chain (200+ levels) needs no explicit limit — worklist doesn't grow C# stack
- No behavior change — the worklist produces the same opcode sequences in the same order
