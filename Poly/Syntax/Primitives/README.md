# Primitive Nodes (Syntax/Primitives/)

Primitives are the irreducibile instruction set that structured AST nodes decompose to
via `Node.ToPrimitives()`. Each primitive declares its `StackEffect` — how many values
it pops and pushes on the eval stack — which drives the ring allocator in `ProgramCompiler`.

## Primitive Taxonomy

| Primitive | StackEffect | Purpose |
|-----------|-------------|---------|
| `PushConstant` | (0,1) | Push a literal value |
| `LoadLocal` | (0,1) | Read a local variable by slot index |
| `StoreLocal` | (1,0) | Write a local variable by slot index |
| `Parameter` | (0,1) | Push a function argument by slot index |
| `BinaryOp` | (2,1) | Binary operation (add, sub, and, or, etc.) |
| `UnaryOp` | (1,1) | Unary operation (negate, not, bitwise not) |
| `Goto` → `ResolvedGoto` | (0,0) | Unconditional branch (resolved to PC by `PrimitiveLinker`) |
| `CondGoto` → `ResolvedCondGoto` | (1,0) | Conditional branch (pops condition, branches if false) |
| `Label` | (0,0) | Branch target marker (no-op, kept in sequence for forward refs) |
| `Return` | (1,0) | Return from function |
| `Call` | (N+1,1) | Call a function (N args + 1 target) |
| `CallExternal` | (N,1) | Call a CLR method directly |
| `Throw` | (1,0) | Throw an exception |
| `Dup` | (1,2) | Duplicate top of stack |
| `Discard` | (1,0) | Pop and discard a value |
| `CountBits` | (1,1) | Population count (popcount) |
| `ArrayLoad` | (2,1) | Load from heap-allocated array (handle, index) |
| `ArrayStore` | (3,0) | Store to heap-allocated array (value, handle, index) |
| `NewArray` | (1,1) | Allocate a new `long[]` on the heap |
| `StridedSet` | (4,0) | Bit-set operation across strided indices |
| `LoadHeapConstant` | (0,1) | Load a heap-allocated constant by handle |
| `AllocClosure` | (N,1) | Allocate a closure with N captured upvalues |
| `LoadUpvalue` | (0,1) | Load a captured upvalue from current closure |
| `StoreUpvalue` | (1,1) | Store a captured upvalue (pushes value back) |

## Design

- `PrimitiveNode` extends `Node` and seals `ToPrimitives()` — primitives are terminal
  and don't expand further.
- `StackEffect` is an abstract property — each primitive self-describes its dataflow.
- `PrimitiveLinker` resolves `Label` references to absolute PC offsets, producing
  `ResolvedGoto`/`ResolvedCondGoto`.
- Labels are kept as no-op markers in the final array so forward branch references
  resolve correctly.
