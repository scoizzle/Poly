# VM Gap Analysis

**Date:** 2026-06-08  
**Context:** The VM replaced the tree-walker as the canonical semantics backend for the lowered IR. This document identifies gaps between what the VM currently implements and what the platform conceptually requires.

---

## Summary

The VM has 38 opcodes and handles most basic language constructs (arithmetic, control flow, closures, exceptions, CLR interop). However, it has several conceptual gaps — some architectural (no GC, no array ops), some domain-level (no policy/event/entity opcodes), and some fidelity gaps (TypeIs doesn't check types). The most critical gaps block either production viability or the neurosymbolic platform vision.

---

## 1. Memory Management (Architectural)

### No GC
The `Heap` is an append-only `List<object?>`. Handles are indices that never shrink. Long-running synthesis loops will OOM.

**Impact:** Any iterative macro expansion or evolution feedback cycle will leak memory.

*Possible fix:* Add a `Heap.Sweep(IEnumerable<int> roots)` pass (compact + reindex) or plug into .NET `GC` via weak references.

### No finalization
There is no general-purpose finalization mechanism. `UsingStatement` lowering manually emits dispose calls via `CallExternal`. This pattern doesn't compose (nested disposables, partially-constructed objects).

---

## 2. Missing Opcode Families

### No array/collection operations
There is no `NewArr`, `LoadElem`, `StoreElem`, `ArrLen`. Array access, iteration, and construction all go through `CallExternal`, which bakes in CLR dependency at the opcode level.

**Impact:** Every array or list operation becomes a CLR interop call — slower than native opcodes and impossible to serialize.

### No string operations
String concatenation goes through `CallExternal` (`StringConcatDelegate`). No `StrEq`, `StrLen`, `StrConcat`, `StrInterp`.

**Impact:** String-heavy code (logging, serialization, code generation) is slow and CLR-bound.

### No value-type boxing/unboxing
Primitive types (`int`, `long`, `double`) are handled directly on the stack. But there is no opcode to box a stack value to a heap object or unbox a heap handle back to a stack primitive. Boxing currently happens implicitly when code calls `Heap.Allocate` (e.g. in `CallExternal`).

### No tail call
`Call` always pushes a new frame. Recursive macro expansion will stack overflow.

### No dynamic dispatch
All method calls are resolved at lowering time. The VM has no virtual method table, no `callvirt`, no interface dispatch. CLR virtual calls go through `CallExternal` — losing the benefit of static dispatch.

---

## 3. Domain-Model Integration (Neurosymbolic Platform)

The vision requires the IR to express domain concepts (actors, entities, stages, policies, events). The VM must execute them. Currently these concepts are absent:

### No entity/relationship traversal
Domain relationships (`OneToMany`, `ManyToMany`, `sourceOwnsTarget`) have no VM representation. Navigating `order.Customer.Address` requires CLR interop.

### No policy enforcement opcode
Policies (rules with All/Any aggregation, actor property constraints, role checks) have no VM-level primitive. Policy decisions must be implemented by the domain modeler via `CallExternal`, or worse — not implemented at all until codegen.

### No event dispatch
Domain events with subscription/correlation routing have no VM-level dispatch mechanism. The `event subscription` data in the domain model is irreducible by the VM today.

### No actor identity / lifecycle
Actor types with claim mapping, `sub` property, and `roleClaimType` have no VM-level construct. There is no `LoadIdentity`, `CheckRole`, or `ResolveSubject` opcode.

---

## 4. Execution Fidelity Gaps

### `TypeIs` doesn't test types
`TypeIs` is lowered to `IsNotNullDelegate`, which checks whether a heap value is non-null. `TypeIs(new Constant("hello"), TypeReference.To<int>())` returns `1` because `"hello"` is non-null. It should return `0` because `"hello"` is not an `int`.

**Impact:** Any IR that uses `TypeIs` for dispatch (pattern matching, runtime type branching) produces wrong results.

### Divide-by-zero exceptions are unhandled by exception regions
If the `Div` or `Mod` opcode divides by zero, it throws a CLR `DivideByZeroException`. This is caught by the top-level try-catch in `Execute()` and converted to `InterpreterResult.Throw(exception)`. However, **the VM `Throw` opcode and exception regions are a separate mechanism**. A CLR exception thrown inside an opcode handler does NOT trigger `FindRegion` — it's caught by the outer catch-all, which means **try/catch blocks in IR programs cannot catch divide-by-zero**.

### `Suspend` result extraction uses different paths
`Int` (vector=0) sets `state.Status = InterpreterStatus.Suspended`. The loop exits, and `ExtractResult` is NOT called. The returned result is `InterpreterResult.Suspend()`. However, if the loop exits for another reason (normal completion) simultaneously, `ExtractResult` processes the stack. There's a potential race or logic gap in the priority of status checks.

### `Await` blocks synchronously
The `Await` node calls `GetAwaiter().GetResult()` via `CallExternal`. This blocks the entire VM thread. No state machine transformation, no continuation.

---

## 5. Debugging & Observability

### No breakpoints
`VmState.BreakpointSkipNodeId` is declared but never read by the execute loop. There is no `Breakpoint` opcode.

### No step execution
`Int`/`Iret` provide suspend/resume at the opcode level, but there is no step-over, step-into, or step-out support.

### No state serialization
`VmState` contains `Program`, `PC`, `FrameBase`, `Stack`, `Heap`, `PendingExceptionValue`. None of this is serializable. You cannot persist a suspended VM for later resumption.

### No profiling hooks
No instruction counter, no allocation tracker, no per-function execution time. The 100,000 step limit is the only instrumentation.

### No SOS/palth
The `ToTraceString()` virtual method on `Node` exists for human-readable descriptions but is only used in debug trace.

---

## 6. Lowering / Code Quality

### No peephole optimization
Every AST node maps directly to an opcode sequence. There is no optimization pass: constant folding happens during analysis (AST level), not at the bytecode level. Patterns like `PushInt 0; Add` (identity) remain as-is.

### No jump threading
`Jump` -> `Jump` chains are not collapsed. `JumpIfFalse` over `JumpIfFalse` is not simplified.

### No dead code elimination
Unreachable code after unconditional `Jump`, `Return`, or `Throw` is not removed.

### Variable-length encoding not used
All immediate values are fixed 4 or 8 bytes. Small constants (0, 1, true, false) consume 5 bytes each (`PushInt` + 4 bytes). A `PushByte` or short-form encoding would halve bytecode size for common patterns.

---

## 7. Serialization & Portability

### No bytecode format
`Bytecode` is an in-memory CLR type with CLR object references (delegates, `Type`, `NodeId`). It cannot be serialized to disk, sent over a wire, or cached between process invocations.

**Impact:** The entire lowering must be repeated every time a program runs. No caching of compiled IR.

---

## 8. Safety & Security

### No sandboxing
The VM can call any CLR method via `CallExternal`. There is no capability check, no whitelist, no permission system. A lowered program can call `File.Delete`, `Process.Start`, or `Environment.Exit`.

### No step limit per call
The 100,000 step limit is global. A single macro expansion may consume all steps, starving other macros.

### No cycle detection
Recursive macro expansion without base case will hit the step limit or stack overflow, but the error message is generic ("Max instruction steps exceeded"). No cycle detection at the call graph level.

---

## 9. Feature Maturity Matrix

| Feature | Status | Notes |
|---------|--------|-------|
| Integer arithmetic | ✓ | All signed/unsigned ops |
| Double arithmetic | ✓ | All ops |
| Control flow (if/while/for/switch) | ✓ | |  
| Exceptions (try/catch/finally) | 🟡 | **PrimThrow/catch MVPs** — unprotected throw wired (P1A); try-catch dispatch with side table (Strategy B, P1C). **Still missing:** try-finally, UsingStatement dispose, nested EH. See INT-018 tracking. |
| Closures with upvalues | ✓ | |
| CLR interop | ✓ | Methods, constructors, fields |
| Member/index access | ✓ | Through CLR |
| Coalesce / null-forgiving | ✓ | |
| Suspend/resume | ✓ | Basic |
| **GC** | ✗ | Append-only heap |
| **Array operations** | ✗ | CLR-only |
| **String operations** | ✗ | CLR-only |
| **TypeIs correctness** | ✓ | Three-way lowering (TypeCheck for heap-ref, StaticTypeIsMatch for scalar, 0L fallback); see K-015 for remaining VM-path test gap |
| **Tail calls** | ✗ | |
| **Dynamic dispatch** | ✗ | |
| **Policy enforcement** | ✗ | |
| **Event dispatch** | ✗ | |
| **Actor identity** | ✗ | |
| **Breakpoints / stepping** | ✗ | |
| **State serialization** | ✗ | |
| **Bytecode format** | ✗ | In-memory only |
| **Profiling** | ✗ | |
| **Sandboxing** | ✗ | |
| **Optimization passes** | ✗ | |
| **Async state machine** | ✗ | Await blocks |

---

## Recommended Priority Order

> **Note (2026-07-10):** This gap list and priority ordering are **historical** (bytecode/µop era).
> Do not use as an execution backlog. Current pipeline: AST → `DirectVmAbiEmitter` → VM ABI.
> Product planning: `docs/plans/v2-to-v3/master-roadmap.md`. Archived trackers:
> `docs/plans/archive/interpretation/`.

Original list (kept for historical reference — see resolution plan for current priorities):

1. ~~Fix `TypeIs`~~ — **resolved** (three-way lowering: TypeCheck + StaticTypeIsMatch)
2. ~~GC~~ — **resolved** (free-list reclamation; Heap.Sweep deferred)
3. **Tail calls** — recursive macro expansion is core to the platform (high impact, medium effort)
4. **Breakpoints** — **partial** (DebugInterrupt callback exists; BreakpointPCs not implemented)
5. **Bytecode serialization** — cache lowered IR, enable VM state persistence (medium impact, medium effort)
6. **Array/string opcodes** — remove CLR dependency for common operations (medium impact, high effort)
7. ~~Policy/event opcodes~~ — **resolved per domain-lowering ADR:** domain concepts lower to generic ops; no domain-specific opcodes (see `docs/decisions/2026-06-08-domain-lowering-boundary.md`)
8. **Peephole optimizer** — reduce bytecode size, improve performance (low impact, medium effort)
9. **Sandboxing** — security for untrusted macro execution (low impact, high effort)
10. **Dynamic dispatch** — virtual methods for polymorphic domain entities (low impact, high effort)
