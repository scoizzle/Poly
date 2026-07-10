# Statement / Expression Type Hierarchy

> **Naming note:** `Expression` as the AST type name does not conflict with
> `System.Linq.Expressions.Expression` in practice. No file currently imports
> both namespaces. The instruction files and `ProgramCompiler.cs` use
> `System.Linq.Expressions` (via `using static`). The lowering/analysis files
> use `Poly.Syntax.Nodes`. If a future file needs both, one side qualifies
> with the full namespace — one line of ceremony.

> **Status:** Design  
> **Prerequisite:** Lowering prep passes (StackDepthAnalysis, LabelAssignment,
> UopGeneration). Desired before assembly step to simplify Block PopOp logic.

---

## New abstract types

```csharp
namespace Poly.Syntax.Nodes;

public abstract record Statement : Node;    // net 0 push
public abstract record Expression : Node;   // net 1 push
```

Delete `Operator.cs`. Replace with these two.

---

## Expression (net +1 push, 34 nodes)

Arithmetic, comparison, logical, bitwise, shift:
- `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`
- `Equal`, `NotEqual`, `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual`
- `And`, `Or`
- `BitwiseAnd`, `BitwiseOr`, `BitwiseXor`
- `ShiftLeft`, `ShiftRight`

Unary:
- `UnaryMinus`, `Not`, `BitwiseNot`
- `Coalesce`, `NullForgiving`
- `TypeAs`, `TypeIs`, `TypeCast`

Value producers:
- `Conditional` — always push result of chosen branch
- `Assignment` — push assigned value
- `Invoke` — push return value (void calls push 0)
- `Member`, `IndexAccess`
- `New`, `NewArray`
- `Lambda` — push closure handle

Simple values:
- `Constant`, `Variable`, `Parameter`, `ThisReference`
- `Default`, `Await`, `SuspendNode`

---

## Statement (net 0 push, 12 nodes)

Control flow:
- `WhileLoop`, `DoWhileLoop`, `ForLoop`, `ForEachLoop`
- `IfStatement` (with or without else — always statement)
- `Return` — pop return value, push nothing
- `BreakStatement`, `ContinueStatement`, `GotoStatement`
- `ThrowStatement`
- `UsingStatement`
- `LabelDeclaration`

---

## Neither (keep as `Node`, 7 types)

- `Block` — contains mix of statements/expressions, push depends on children
- `SwitchStatement` — similar to Block, branch-dependent push
- `TryCatchFinally` — depends on body and catches
- `BooleanOperator` — abstract, currently unused. Delete.
- `TypeCast` — currently `Operator` but no override. Keep as `Expression`.
- `TypeReference`, `ClrTypeReference`, `TypeDefinitionReference`, `ResolvedTypeReference` — metadata, not executable
- `ParameterReference` — same
- `PrimitiveTypeReference` — same

These stay `Node` base directly.

---

## What this unblocks in the assembly step

**Before** (current code, line 252 of Lowering.cs):
```csharp
if (i < block.Nodes.Count - 1 && block.Nodes[i] is not WhileLoop)
    ctx.Instructions.Add(new PopOp { SourceNodeId = block.Id });
```

**After:**
```csharp
if (i < block.Nodes.Count - 1 && block.Nodes[i] is Expression)
    ctx.Instructions.Add(new PopOp { SourceNodeId = block.Id });
```

No more list of exceptions. Any future statement node automatically correct.

**IfStatement discards own result** (UopGenerationPass):
```csharp
case IfStatement iff:
    ...
    // Instead of:
    uops.AddRange(GetChildUops(context, iff.ThenBranch));
    // Now add PopOp to discard then-branch result:
    uops.AddRange(GetChildUops(context, iff.ThenBranch));
    uops.Add(new PopOp { SourceNodeId = iff.Id });
    // Same for else branch
```

---

## Full checklist of files that change

### New files (2):
| File | Content |
|------|---------|
| `Poly/Syntax/Nodes/Statement.cs` | `public abstract record Statement : Node;` |
| `Poly/Syntax/Nodes/Expression.cs` | `public abstract record Expression : Node;` |

### Delete (1):
| File | Reason |
|------|--------|
| `Poly/Syntax/Nodes/Operator.cs` | Replaced by Statement/Expression |

### Change base type from `Operator` to `Expression` (28 files):

| File | Current | New |
|------|---------|-----|
| `Add.cs` | `Operator` | `Expression` |
| `Subtract.cs` | `Operator` | `Expression` |
| `Multiply.cs` | `Operator` | `Expression` |
| `Divide.cs` | `Operator` | `Expression` |
| `Modulo.cs` | `Operator` | `Expression` |
| `Equal.cs` | `Operator` | `Expression` |
| `NotEqual.cs` | `Operator` | `Expression` |
| `LessThan.cs` | `Operator` | `Expression` |
| `LessThanOrEqual.cs` | `Operator` | `Expression` |
| `GreaterThan.cs` | `Operator` | `Expression` |
| `GreaterThanOrEqual.cs` | `Operator` | `Expression` |
| `And.cs` | `Operator` | `Expression` |
| `Or.cs` | `Operator` | `Expression` |
| `BitwiseAnd.cs` | `Operator` | `Expression` |
| `BitwiseOr.cs` | `Operator` | `Expression` |
| `BitwiseXor.cs` | `Operator` | `Expression` |
| `ShiftLeft.cs` | `Operator` | `Expression` |
| `ShiftRight.cs` | `Operator` | `Expression` |
| `UnaryMinus.cs` | `Operator` | `Expression` |
| `Not.cs` | `Operator` | `Expression` |
| `BitwiseNot.cs` | `Operator` | `Expression` |
| `Coalesce.cs` | `Operator` | `Expression` |
| `Conditional.cs` | `Operator` | `Expression` |
| `Assignment.cs` | `Operator` | `Expression` |
| `Invoke.cs` | `Operator` | `Expression` |
| `IndexAccess.cs` | `Operator` | `Expression` |
| `New.cs` | `Operator` | `Expression` |
| `Lambda.cs` | `Operator` | `Expression` |
| `Await.cs` | `Operator` | `Expression` |
| `SuspendNode.cs` | `Operator` | `Expression` |

### Change base type from `Node` to `Expression` (7 files):

| File | Current | New |
|------|---------|-----|
| `Constant.cs` | `Node` | `Expression` |
| `Variable.cs` | `Node` | `Expression` |
| `Parameter.cs` | `Node` | `Expression` |
| `ThisReference.cs` | `Node` | `Expression` |
| `Default.cs` | `Node` | `Expression` |
| `Member.cs` | `Node` | `Expression` |
| `NewArray.cs` | `Node` | `Expression` |
| `NullForgiving.cs` | `Operator` | `Expression` |
| `TypeAs.cs` | `Operator` | `Expression` |
| `TypeIs.cs` | `Operator` | `Expression` |
| `TypeCast.cs` | `Operator` | `Expression` |

### Change base type from `Operator` to `Statement` (10 files):

| File | Current | New |
|------|---------|-----|
| `WhileLoop.cs` | `Operator` | `Statement` |
| `DoWhileLoop.cs` | `Operator` | `Statement` |
| `ForLoop.cs` | `Operator` | `Statement` |
| `ForEachLoop.cs` | `Operator` | `Statement` |
| `IfStatement.cs` | `Operator` | `Statement` |
| `Return.cs` | `Operator` | `Statement` |
| `GotoStatement.cs` | `Operator` | `Statement` |
| `ThrowStatement.cs` | `Operator` | `Statement` |
| `UsingStatement.cs` | `Operator` | `Statement` |
| `LabelDeclaration.cs` | `Operator` | `Statement` |

`SwitchStatement` and `TryCatchFinally` stay as `Node` — they're not used in the lowering pipeline yet and their push/pop depends on content.

### Change base type from `Node` to `Statement` (3 files):

| File | Current | New |
|------|---------|-----|
| `BreakStatement.cs` | `Node` | `Statement` |
| `ContinueStatement.cs` | `Node` | `Statement` |
| `NullForgiving.cs` | already counted above | — |

### Change base from `Operator` to `Node` (3 files):

| File | Current | New |
|------|---------|-----|
| `Block.cs` | `Operator` | `Node` |
| `SwitchStatement.cs` | `Operator` | `Node` |
| `TryCatchFinally.cs` | `Operator` | `Node` |

### Keep as `Node` (no change, 5 files):
- `TypeReference.cs`, `ClrTypeReference.cs`, `TypeDefinitionReference.cs`, `ResolvedTypeReference.cs`
- `ParameterReference.cs`

### Delete (2 files):
- `Operator.cs` — replaced by Statement/Expression
- `BooleanOperator.cs` — unused abstract class

---

## Lowering logic changes (3 files)

| File | Change |
|------|--------|
| `Poly/Interpretation/Vm/Lowering.cs` | Block PopOp check: `block.Nodes[i] is not WhileLoop` → `block.Nodes[i] is Expression` |
| `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` | IfStatement: add PopOp after each branch to discard result |
| `Poly/Interpretation/Analysis/LoweringPrep/LoweringPrepPass.cs` | IfStatement depth: exit = 0 instead of 1 |

### Test file updates

Any test that does `is Operator` needs change. Grug check:

```bash
grep -r "is Operator\|as Operator\|: Operator" Poly.Tests/
```

Likely few. Most tests check `is Constant`, `is Add`, etc., not the base type.

### Plan documents to update (2 files):

| File | Change |
|------|--------|
| `docs/plans/archive/interpretation/v2-to-v3/spikes/lowering-as-analysis-passes.md` | Add Statement/Expression to architecture section |
| `docs/plans/archive/interpretation/v2-to-v3/spikes/lowering-analysis-passes-phase2.md` | Mention that Block PopOp logic simplifies |
| lowering-assembly-step | Never created; obsolete under direct AST→ABI |

---

## Per-file change map

### New files (2)

| File | Content |
|------|---------|
| `Poly/Syntax/Nodes/Statement.cs` | `public abstract record Statement : Node;` |
| `Poly/Syntax/Nodes/Expression.cs` | `public abstract record Expression : Node;` |

### Delete (2)

| File | Reason |
|------|--------|
| `Poly/Syntax/Nodes/Operator.cs` | Replaced by Statement/Expression |
| `Poly/Syntax/Nodes/BooleanOperator.cs` | Unused abstract class |

### Change base from `Operator` to `Expression` (30 files)

Change `: Operator` → `: Expression` in the class declaration line.

| # | File | Current declaration |
|---|------|-------------------|
| 1 | `Add.cs` | `: Operator` |
| 2 | `Subtract.cs` | `: Operator` |
| 3 | `Multiply.cs` | `: Operator` |
| 4 | `Divide.cs` | `: Operator` |
| 5 | `Modulo.cs` | `: Operator` |
| 6 | `BitwiseAnd.cs` | `: Operator` |
| 7 | `BitwiseOr.cs` | `: Operator` |
| 8 | `BitwiseXor.cs` | `: Operator` |
| 9 | `ShiftLeft.cs` | `: Operator` |
| 10 | `ShiftRight.cs` | `: Operator` |
| 11 | `UnaryMinus.cs` | `: Operator` |
| 12 | `BitwiseNot.cs` | `: Operator` |
| 13 | `Coalesce.cs` | `: Operator` |
| 14 | `TypeAs.cs` | `: Operator` |
| 15 | `TypeCast.cs` | `: Operator` |
| 16 | `Conditional.cs` | `: Operator` |
| 17 | `Assignment.cs` | `: Operator` |
| 18 | `Invoke.cs` | `: Operator` |
| 19 | `IndexAccess.cs` | `: Operator` |
| 20 | `New.cs` | `: Operator` |
| 21 | `Lambda.cs` | `: Operator` |
| 22 | `Await.cs` | `: Operator` |
| 23 | `SuspendNode.cs` | `: Node` |
| 24 | `Member.cs` | `: Operator` |
| 25 | `NullForgiving.cs` | `: Node` |
| 26 | `TypeIs.cs` | `: BooleanOperator` |
| 27 | `Equal.cs` | `: BooleanOperator` |
| 28 | `NotEqual.cs` | `: BooleanOperator` |
| 29 | `LessThan.cs` | `: BooleanOperator` |
| 30 | `LessThanOrEqual.cs` | `: BooleanOperator` |
| 31 | `GreaterThan.cs` | `: BooleanOperator` |
| 32 | `GreaterThanOrEqual.cs` | `: BooleanOperator` |
| 33 | `And.cs` | `: BooleanOperator` |
| 34 | `Or.cs` | `: BooleanOperator` |
| 35 | `Not.cs` | `: BooleanOperator` |

Note: items 26-35 currently extend `BooleanOperator`. Since `BooleanOperator` is being deleted, change directly to `: Expression`.

### Change base from `Node` to `Expression` (7 files)

| # | File | Current declaration |
|---|------|-------------------|
| 1 | `Constant.cs` | `: Node` |
| 2 | `Variable.cs` | `: Node` |
| 3 | `Parameter.cs` | `: Node` |
| 4 | `ThisReference.cs` | `: Node` |
| 5 | `Default.cs` | `: Node` |
| 6 | `Member.cs` | `: Node` — wait, this was already counted in Operator→Expression. Let me check. |

Actually, `Member.cs` is `: Operator` per the earlier analysis. Let me re-verify:

```bash
grep "Member :" Poly/Syntax/Nodes/Member.cs
```

Expected: `public sealed record Member(Node Value, string MemberName) : Operator {`

So `Member` is in the Operator→Expression list. The correct Node→Expression list is:

| # | File | Current declaration |
|---|------|-------------------|
| 1 | `Constant.cs` | `: Node` |
| 2 | `Variable.cs` | `: Node` |
| 3 | `Parameter.cs` | `: Node` |
| 4 | `ThisReference.cs` | `: Node` |
| 5 | `Default.cs` | `: Node` |
| 6 | `NewArray.cs` | `: Node` |

### Change base from `Operator` to `Statement` (10 files)

Change `: Operator` → `: Statement`.

| # | File | Current declaration |
|---|------|-------------------|
| 1 | `WhileLoop.cs` | `: Operator` |
| 2 | `DoWhileLoop.cs` | `: Operator` |
| 3 | `ForLoop.cs` | `: Operator` |
| 4 | `ForEachLoop.cs` | `: Operator` |
| 5 | `IfStatement.cs` | `: Operator` |
| 6 | `Return.cs` | `: Operator` |
| 7 | `GotoStatement.cs` | `: Operator` |
| 8 | `ThrowStatement.cs` | `: Operator` |
| 9 | `UsingStatement.cs` | `: Operator` |
| 10 | `LabelDeclaration.cs` | `: Operator` |

### Change base from `Node` to `Statement` (2 files)

| # | File | Current declaration |
|---|------|-------------------|
| 1 | `BreakStatement.cs` | `: Operator` |
| 2 | `ContinueStatement.cs` | `: Operator` |

Wait — earlier analysis showed BreakStatement and ContinueStatement as `: Operator`, not `: Node`. Let me re-check:

```bash
grep "BreakStatement :\|ContinueStatement :" Poly/Syntax/Nodes/*.cs
```

These were shown as `: Operator` in the exploration task. So they change from Operator to Statement, not from Node. The Node→Statement list might be empty.

### Change base from `Operator` to `Node` (3 files)

| # | File | Current declaration |
|---|------|-------------------|
| 1 | `Block.cs` | `: Operator` |
| 2 | `SwitchStatement.cs` | `: Operator` |
| 3 | `TryCatchFinally.cs` | `: Operator` |

### Keep as `Node` (no change, 5 files)

- `TypeReference.cs`
- `ClrTypeReference.cs`
- `TypeDefinitionReference.cs`
- `ResolvedTypeReference.cs`
- `ParameterReference.cs`

### Lowering logic changes (5 files)

| # | File | Change |
|---|------|--------|
| 1 | `Poly/Interpretation/Vm/Lowering.cs` | Line 252: `is not WhileLoop` → `is Expression` |
| 2 | Same file, `EmitIfStatement` | Add `PopOp` after each branch to discard result |
| 3 | `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` | Line 293: `is not WhileLoop` → `is Expression` |
| 4 | Same file, `EmitIfStatement` | Add `PopOp` after each branch to discard result |
| 5 | `Poly/Interpretation/Analysis/LoweringPrep/LoweringPrepPass.cs` | `ComputeIfStatement`: return `(0, 0)` always |

## Summary

| Category | Count | Files |
|----------|-------|-------|
| New files | 2 | `Statement.cs`, `Expression.cs` |
| Delete | 2 | `Operator.cs`, `BooleanOperator.cs` |
| Change base (Operator/BooleanOperator→Expression) | 35 | All arithmetic, bitwise, shift, unary, comparison, logical, type-ops, and compound expressions |
| Change base (Node→Expression) | 6 | Constant, Variable, Parameter, ThisReference, Default, NewArray |
| Change base (Operator→Statement) | 12 | All loops, IfStatement, Return, Goto, Throw, Using, LabelDeclaration, Break, Continue |
| Change base (Operator→Node) | 3 | Block, SwitchStatement, TryCatchFinally |
| Keep as Node (no change) | 5 | TypeReference variants |
| Lowering logic changes | 5 | 2 files×2 changes each + 1 depth fix |
| **Total files touched** | **~70** | |

~70 files sounds intimidating. But 57 of them are one-line base class swaps. 5 files have real logic changes. The rest are deletes.
