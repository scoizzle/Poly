# Stack Depth & Definite Assignment Analyzers

## 1. Stack Depth Analyzer

**Goal:** Pre-compute net stack effect of every expression. Assert during lowering that the emitted bytecode is balanced.

### Design

Each expression node gets `StackDepthMetadata(int Push, int Pop)` — the number of values pushed and popped by the node alone (children are counted separately, then aggregated).

| Node | Push | Pop | Net |
|------|------|-----|-----|
| `Constant(42)` | 1 | 0 | +1 |
| `Binary(left, right, add)` | 0 (children) | 2 (op pops) | -2 + children |
| `Block(nodes)` | sum of children | pop count | balanced |
| `IfStatement(cond, then, else)` | max(then, else) | cond | max path |
| `WhileLoop(cond, body)` | 0 | 0 | balanced |

### Lowering integration

At the end of `Lower()`, walk the emitted bytecode and track expected stack depth. Assert:
- Depth never goes negative (no underflow)
- Depth returns to base at each function's `Return`
- Each function's entry/exit depth matches `LocalCount + RetBytes`

## 2. Definite Assignment Analyzer

**Goal:** For each variable read, determine if it's definitely assigned (written on all preceding control-flow paths). The lowering can skip zero-initialization for definitely-assigned locals.

### Design

A pass that walks control flow and tracks variable write sets:

- `Assignment(x, v)` → x is assigned
- `Block(nodes)` → x is assigned after Block iff assigned in all non-exceptional paths
- `IfStatement(cond, then, else?)` → x is assigned after If iff assigned in both then and else
- `WhileLoop(cond, body)` → x is assigned after loop iff assigned in body (assumes body runs at least once) OR was assigned before loop
- `TryCatchFinally(try, catch, finally)` → x is assigned after Try iff assigned in try AND catch

### Lowering integration

- Skip `PushInt 0` (zero-init) for locals that are definitely assigned before any read
- The VM's current zero-init is a safety net; this analyzer makes it unnecessary for correct programs
