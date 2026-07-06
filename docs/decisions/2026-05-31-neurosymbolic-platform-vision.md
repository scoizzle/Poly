# Neurosymbolic Platform Vision — Codify, Execute, Evolve

**Date:** 2026-05-31
**Amended:** 2026-06-25 (Expression Levels); 2026-07-06 (direction clarification)
**Status:** Vision / Partially Superseded

> **Current direction (2026-07-06):** 
> - The **AST (`Poly.Syntax` nodes)** is the primary serializable, model-facing, symbolic IR. Models and synthesis primarily work with Domain expressions and the AST.
> - **PrimitiveNode** (under Syntax/Primitives) is the canonical IR for the *VM execution engine*. Lowering (`ToPrimitives`) is an opportunity to *expand* known analysis metadata rather than discard structure.
> - No separate `Poly/Ir/` module will be created (see 2026-07-04-primitives-as-canonical-ir.md).
> - The VM provides canonical *execution semantics*. The AST provides the canonical *symbolic representation*.
> - Earlier three-tier model (TreeWalker as canonical) and "Expression Levels" table below are historical; tree-walker was removed (2026-06-08-vm-as-canonical-semantics.md).

This document retains value for the high-level neurosymbolic intent but should be read with the ADRs and AGENTS.md for the actual layering.

## Context

Poly was originally scoped as a shared abstraction layer into varying type systems for dynamic code generation and execution — a fluent, strongly-typed DSL for domain modeling, validation, serialization, and codegen.

The platform's ultimate output was always executable software, though this was not previously disclosed. The domain model, analysis passes, validation rules, and evolution layer together form the **compiler frontend** for a program synthesis engine. The model *is* the program; evolution *is* iterative development.

The broader goal is a **neurosymbolic platform** that lets models generate, execute, and continuously evolve explicit, interpretable programs. It combines neural perception modules with reusable symbolic heuristics (macros) in a modular IR, enabling efficient, verifiable, and adaptive intelligence.

The core insight: **LLM inference is expensive; a codified algorithm runs at native speed with zero inference cost.** The platform's value is amortizing discovery cost across reuse.

## The Knowledge Pipeline

```
LLM inference (expensive)
    ↓ discovers pattern
Macro/heuristic (symbolic, interpreted)
    ↓ validates via tree walker
Tested heuristic (verified behavior)
    ↓ codifies via code generator
Native assembly (zero inference cost)
    ↓
Macro library (compoundable knowledge base)
```

1. A model pays inference cost **once** to discover or identify an algorithm.
2. The algorithm is codified as a macro in the IR — a tiny, composable program.
3. The tree-walker interpreter validates it against conformance tests.
4. A backend (currently C# codegen) compiles it to native speed.
5. The macro enters a library with signature, expanded AST, provenance, and usage frequency.
6. Every subsequent use of that macro runs at native speed — no LLM call, no tokens, no context consumed.

The platform turns proprietary model expertise into a durable, portable asset that survives model churn. If the inference backend is replaced (e.g., Llama 6 → Claude 9), the accumulated macro library is unaffected. Weights are ephemeral; the symbolic archive is permanent.

## Architecture

### Layer 0 — IR (exists, extend)

```
Poly.Syntax
```

The AST is the currency of the entire system. Every other module reads or writes it. Extend with nodes for macro references, perception module calls, and evolution metadata (provenance tags). No new dependencies; everything already depends on this.

### Layer 1 — Execution (exists, needs tree walker)

```
Poly.Interpretation
  ├── TreeWalker        ← NEW: fast feedback, macro expansion, reference semantics
  ├── LinqExpressions   (testing, reference implementation)
  └── CSharp            (current production codegen)
```

Three-tier eval with the tree walker as the **canonical semantics** of the IR. Every downstream backend must produce identical results for every input. When the production backend changes (WASM, native AOT, GPU kernels), the IR, analysis passes, and macro system remain stable — only a new backend is written, validated against the tree walker's conformance suite.

### Layer 2 — Synthesis (new)

```
Poly.Synthesis
  ├── MacroExpansion    expand heuristics inline before evaluation
  ├── Sketching         NL → AST adapter (pluggable perception interface)
  └── Refinement        iterative improvement from test feedback
```

The only hard dependency is `Poly.Syntax` and the tree walker for feedback. The perception interface (`IPerceptionModule`) keeps the neural backend swappable.

### Layer 3 — Domain Modeling / Evolution (exists, reframe)

```
Poly.DomainModeling
  ├── Builders           (existing)
  ├── Evolution          (existing — add execution-result feedback)
  └── CodeGeneration     (existing DomainLoweringGenerator)
```

Evolution is the outer agent loop that uses synthesis + execution + analysis. `EvolutionResult` gains a slot for test-run outcomes alongside static analysis diagnostics.

### Layer Boundaries (enforced, one-way)

```
Syntax ← Synthesis ← Evolution
Syntax ← Interpretation ← Evolution
Syntax ← DomainModeling ← Evolution
```

No module below Evolution may depend on Synthesis or Perception.

## Design Principles

### The Unix Philosophy Applies to Model Cognition

Each macro is a tiny program. The IR is the universal interface (text streams). The tree walker is the shell (pipes). Macros are composable filters. Small, verifiable, composable units beat monolithic black boxes at every axis except raw throughput — and the C#/native backends close that gap for production.

Unix gave developers pipes to compose programs written by humans. This platform gives models a pipe-equivalent for composing their own discoveries. Same shape, new source of programs.

### Two-Tier Evaluation (Historical)

> **Note (2026-07-06):** This section reflects the pre-2026-06-08 state. The tree-walker was removed; the VM is now the canonical execution semantics. Evaluation is effectively AST-level (symbolic) → primitives + expanded metadata (VM execution) → backends. See the header note and 2026-06-08-vm-as-canonical-semantics.md + 2026-07-04-primitives-as-canonical-ir.md.

| Path | Speed | Fidelity | Use Case |
|------|-------|----------|----------|
| Tree-walker | Instant | Approximate | Evolution feedback, macro expansion, early validation |
| LINQ pipeline | Fast | Exact | Unit tests, reference semantics |
| Backend codegen | Slow | Production | Final assemblies, deployment |

Each tier catches failures before you pay the next tier's cost. The tree walker is the highest-leverage: cheapest to build, saves the most expensive operation (backend compilation) most often.

### Expression Levels & the Canonical IR (Historical)

> **Note (2026-07-06):** The planned separate `Poly/Ir/` was never created. Primitives (enhanced during lowering) serve as the canonical IR for execution. The AST remains the primary symbolic/serializable form. Models should not be exposed to execution-model details (ring allocation, etc.). Lowering must preserve and expand metadata.

The evaluation tiers are projection targets, not authoring surfaces. Models express intent at one of three levels, with the Canonical IR (`Poly/Ir/`) as the pivot:

| Level | Module | Who authors it |
|-------|--------|---------------|
| **Domain** | `DomainModeling` | Model (default), User |
| **IR** | `Ir` | Compiler (lowering), Model (escape hatch) |
| **µops** | `Interpretation/Vm` | Compiler only |

The model defaults to the domain level because domain errors are meaningful ("`Order.Confirm` requires `Order` to be in `Pending` stage"), the lowering is deterministic, and the model can inspect the lowered IR to understand *why* its domain description behaves a certain way — without ever touching µops.

The IR is the lowest level that is semantically complete: every IR `Module` has a deterministic execution result, but the model is not burdened with execution-model concerns (ring allocation, PC offsets, `ConsumedFromPcs` arrays). When the model needs finer control — performance-critical paths, custom algorithms — it drops to IR as an opt-in escape hatch.

Once IR is verified by the VM, every backend is a **deterministic projection** of the same canonical representation (C# source, µop listing, CFG visualization, domain-level trace). The model never generates per-language code.

For the full three-level architecture, the IR design, and the lowering pipeline, see `docs/ARCHITECTURE.md` §3 and `docs/experiments/interpretation-compiler-framework-plan.md` (both also require updates for current direction).

### Analysis ↔ Interpreter Boundary

Analysis passes remain the right tool for:
- Type resolution / member resolution (structural contracts)
- Variable scope / lifetime validation (cheap, catches real bugs)
- Constant folding (free optimization)
- Control flow graphs (needed by lowering passes)

The tree walker becomes the right tool for:
- Macro expansion correctness (did the expanded form produce the right values?)
- Side-effect ordering validation (did the program do what we expected?)
- Boundary conditions (what happens at iteration N with input X?)
- Any behavioral check that would require simulating full evaluation in an analysis pass

This keeps analysis from over-investing in areas where running the program is cheaper and more correct than modeling it.

## Impact on Current Codebase

### What Already Fits

- **Modular IR** — `Poly.Syntax` AST nodes are the tree IR
- **Execution** — `Poly.Interpretation.LinqExpressions` compiles and runs trees
- **Verification** — Analysis passes (type resolution, member resolution, control flow, constant folding) + `Poly.Validation` rules
- **Evolution** — `DomainEvolution`, `EvolutionResult`, analysis-gated change application designed for LLM-driven cycles

### What Is New

- **Canonical IR** under `Poly/Ir/` — block-structured CFG with SSA values, the pivot between domain expression and all backends (see `docs/ARCHITECTURE.md` §3)
- **Tree-walker interpreter** under `Poly.Interpretation` — canonical semantics, fast feedback, macro expansion
- **Macro system** under `Poly.Synthesis` — macro library, expansion, storage, provenance
- **Perception interface** (`IPerceptionModule`) — pluggable neural backends
- **Execution-aware evolution** — `EvolutionResult` with test-run outcomes

### What Changes Priority

- The LINQ Expressions pipeline is a development scaffold and test reference, not the production codegen path
- `CSharpGenerator` becomes the primary production backend (replaceable)
- The tree walker is the highest-leverage new component — it unblocks the iteration loop everything else depends on

## Consequences

- The macro library's provenance (which model discovered it) matters less than its conformance suite (does it pass the tree walker?). A macro from a weaker model that passes is worth more than an unvalidated macro from a frontier model.
- When inference backends are replaced, no accumulated macros are lost. The platform's knowledge base survives model churn.
- The tree walker must be built with sufficient fidelity to serve as the canonical semantics reference — not just a sketch. It needs a conformance test suite that every backend (LINQ, C#, future targets) must pass.
- `EvolutionResult` gains execution feedback alongside static analysis.
- `AGENTS.md` should be updated to reflect the neurosymbolic platform framing once the first tree-walker and macro expansion exist.
