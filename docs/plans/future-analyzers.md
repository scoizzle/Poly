# Future Analyzer Types

---

## 1. Stack Depth Analyzer

**Problem:** The lowering emits bytecode that can underflow or leak values. These bugs surface at runtime as "Stack underflow" or silently incorrect results.

**Goal:** Pre-compute the net stack effect of every expression and assert the emitted bytecode stays balanced.

**How it would work:**

A pass that walks the AST and annotates each node with `(push: int, pop: int)` — the number of values it pushes and pops:

| Node | Push | Pop | Net |
|------|------|-----|-----|
| `Constant(42)` | 1 | 0 | +1 |
| `Binary(left, right, add)` | 0 (children push) | 2 (op pops) | -2 + children |
| `Block(nodes)` | net of each child summed | 0 | varies |
| `IfStatement(cond, then, else?)` | net of `then` or `else` | net of `cond` | max path |
| `WhileLoop(cond, body)` | 0 | 0 (balanced loop) | 0 |

**Lowering integration:**
- During lowering, track the expected stack depth at each instruction
- After lowering, verify the bytecode's per-instruction stack depth matches
- Catch underflows (depth goes negative) and leaks (depth doesn't return to base at function return)

**Implementation:**
- New file: `Poly/Interpretation/Analysis/StackDepthAnalyzer.cs`
- `INodeAnalyzer` pass that computes `(Push, Pop)` for each node
- Validation check at the end of `Lower()` that walks the bytecode and verifies depth

---

## 2. Lambda Return Type Resolver (Dedicated Pass)

**Problem:** The current lambda return type resolution is a single `case Lambda` in `ResolveMethodInvocationType`. Recursive lambdas, generic lambdas, and lambdas whose body type depends on invocation context don't resolve correctly, causing `typeof(object)` fallback.

**Goal:** A dedicated pass that fully resolves lambda return types, supporting recursive lambdas and contextual resolution.

**How it would work:**

- Walk all `Lambda` nodes in a first pass, assigning a placeholder type to each
- In a second pass, resolve each lambda's body type, using placeholders for recursive references
- For `Invoke(lambda, args)`, unify the lambda's parameter types with the argument types to get the return type
- Store the resolved type as `LambdaReturnTypeMetadata`

**Edge cases:**
- Recursive lambdas: use fixed-point iteration (resolve until stable)
- Generic lambdas: unify parameter types with argument types
- Lambdas returning other lambdas: resolve to the inner lambda's type

---

## 3. Side-Effect Classification

**Problem:** The lowering treats every expression uniformly — there's no information about whether an expression is pure or impure. An optimizer could reorder or deduplicate pure expressions.

**Goal:** Tag each expression node with a purity classification.

**How it would work:**

| Classification | Meaning | Examples |
|---------------|---------|----------|
| `Pure` | No side effects, idempotent | Arithmetic, comparisons, variable reads |
| `Read` | Reads state but doesn't modify | Member access, index access |
| `Write` | Modifies state | Assignment, store |
| `External` | Calls CLR code with unknown effects | `CallExternal`, `Invoke` of CLR method |
| `Allocate` | Allocates heap memory | `New`, `Lambda`, `Await` |

**Lowering integration:**
- The peephole optimizer could skip folding of `External` expressions
- The scheduler (future) could reorder `Pure` expressions for better locality
- Debugging: mark `External` call sites for special trace behavior

**Status:** `SideEffectAnalysisPass` already exists — see below.

---

## 4. Definite Assignment Analyzer

**Problem:** All locals are zero-initialized, so reading an uninitialized variable silently produces 0. This masks bugs where a variable is used before being assigned.

**Goal:** For each variable read, determine if it's definitely assigned (written on all preceding control-flow paths). Report diagnostics for possibly-uninitialized reads.

**How it would work:**

- Track assignments to each variable across all control flow paths
- For `IfStatement`: variable is definitely assigned after the `if` only if assigned in both branches
- For `WhileLoop`: variable assigned in the body is definitely assigned after the loop (the body ran at least once) or not (the body might not have run)
- For `TryCatchFinally`: variable assigned in try and catch is definitely assigned after

**Lowering integration:**
- Skip zero-initialization of locals that are definitely assigned before any read
- Report warnings for possibly-uninitialized reads
- The VM's current zero-initialization is a safety net; this analyzer would make it unnecessary for correct programs
