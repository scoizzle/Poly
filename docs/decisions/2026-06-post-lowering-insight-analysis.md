# Post-Lowering Insight Analysis as a Core Neurosymbolic Capability

**Status**: Proposed  
**Date**: 2026-06-01  
**Author**: opencode  
**Deciders**: (to be confirmed)

## Context

The neurosymbolic platform vision states that models codify discovered algorithms and heuristics as composable macros in a symbolic IR, validated by a tree-walker interpreter, and compiled to native backends.

A critical insight emerged during the design of the tree-walking interpreter and lowering system:

> One beautiful part of this lowering system is that **additional analysis can be performed on the generated code** and additional hints/info/warning information can be provided back to the model/user doing the authoring.

This suggests that lowering should not be a one-way transformation (Domain → Syntax.Nodes → execution). Instead, the lowered symbolic representation should become a **rich medium for iterative analysis, introspection, and refinement**.

This decision record proposes making **Post-Lowering Insight Analysis** a first-class, core capability of the neurosymbolic platform.

## Decision

We will treat the lowered `Poly.Syntax.Node` trees (and future bytecode) as **first-class artifacts** that support:

1. **Multiple layered analysis passes** after lowering
2. **Suspension and introspection** of execution state at any point
3. **Generation of actionable insights** (hints, suggestions, warnings, explanations) that flow back to the authoring model or user
4. **Iterative refinement loops** where insights can trigger additional lowering, analysis, or authoring assistance

This capability is not an optimization. It is a foundational neurosymbolic primitive.

## Considered Options

### Option 1: Lowering as one-way compilation (Rejected)
- Lower Domain → Syntax → C#/Linq/Bytecode
- Analysis only happens before lowering
- No rich feedback from the generated representation
- **Rejected**: Violates the neurosymbolic vision of iterative symbolic reasoning

### Option 2: Tree-walker with ad-hoc debugging (Partially Accepted)
- Current tree-walking design with suspendability
- Basic introspection of call stack and evaluation stack
- **Accepted as baseline**, but insufficient alone

### Option 3: Post-Lowering Insight Analysis Layer (Chosen)
- Lowered code is systematically re-analyzed by a suite of insight analyzers
- Insights are first-class results alongside diagnostics
- Execution can be suspended at natural points (statement boundaries, function entry/exit, stage transitions, etc.)
- Authoring tools or models can consume insights to suggest improvements
- The system becomes a **conversational symbolic reasoning engine**

## Detailed Design

We will **not** introduce a separate `Insight` type or `ISyntaxInsightAnalyzer`. Instead, we extend the existing diagnostic system with richer severity levels.

### 1. Rich Diagnostic Severities

Extend `DiagnosticSeverity`:

```csharp
public enum DiagnosticSeverity {
    Error,           // Blocking issue that must be fixed
    Warning,         // Should be addressed
    Hint,            // Helpful observation
    Info,            // Purely informational
    Suggestion,      // Actionable authoring guidance (NEW)
    Explanation      // Deep reasoning about the lowered code (NEW)
}
```

`Suggestion` and `Explanation` severities are specifically intended for post-lowering insight analysis.

### 2. Post-Lowering Insight Analyzers

These analyzers run on the lowered `Syntax.Node` tree (or bytecode) and emit diagnostics with the new severities:

- `SemanticCoherenceAnalyzer` — checks fidelity between original domain intent and lowered code
- `IdempotencySafetyAnalyzer` — detects replay/retry hazards in the generated code
- `CausalityOrderingAnalyzer` — finds problematic causal chains and feedback loops
- `ResourceLifecycleAnalyzer` — validates ownership and lifecycle rules in the lowered representation
- `ContractComplianceAnalyzer` — verifies that contract bindings are complete and correct
- `PerformanceHotPathAnalyzer` — identifies expensive patterns in hot execution paths
- `AuthoringSuggestionGenerator` — synthesizes concrete, actionable suggestions from other analyses

These can run iteratively. New insights can trigger additional lowering or analysis passes.

### 2. Core Insight Analyzers (Proposed)

- **SemanticCoherenceAnalyzer** — checks fidelity between domain intent and lowered code
- **IdempotencySafetyAnalyzer** — detects replay/retry hazards
- **CausalityOrderingAnalyzer** — finds problematic causal chains and feedback loops
- **ResourceLifecycleAnalyzer** — validates ownership, cleanup, and lifecycle hooks
- **ContractComplianceAnalyzer** — verifies contract implementation completeness
- **PerformanceHotPathAnalyzer** — identifies expensive operations in hot paths
- **AuthoringSuggestionGenerator** — synthesizes concrete authoring suggestions from other insights

### 3. Suspension Model

The interpreter (stack-based tree-walking VM) must support clean suspension at semantically meaningful points:
- Statement boundaries
- Function entry/exit
- Stage transitions
- Event publication/consumption
- Contract boundary crossings

When suspended, the full `InterpreterState` (call stack, evaluation stack, heap, current node, metadata, suspension reason) must be first-class, serializable, and queryable. This enables rich debugging, step-through execution, and "pause-and-analyze" workflows.

### 4. Feedback Loop

```mermaid
graph TD
    A[Authoring Model/User] --> B[Domain Model]
    B --> C[Lowering Pass]
    C --> D[Syntax.Node Tree]
    D --> E[Post-Lowering Analysis Passes]
    E --> F[Rich Diagnostics<br/>(Error, Warning, Hint, Suggestion, Explanation)]
    F --> A
    D --> G[Tree-Walking VM or Bytecode VM]
    G --> H[Execution with Suspension Points]
    H --> E
```

---

## Rationale

### Why This Is Foundational

1. **Closes the Neurosymbolic Loop**: The system doesn't just execute — it reasons about its own execution and provides actionable feedback.

2. **Enables Iterative Refinement**: The authoring model (human or AI) can continuously improve the domain model based on concrete insights from the lowered code.

3. **Separates Concerns Beautifully**:
   - Lowering = symbolic transformation
   - Analysis = symbolic reasoning about the transformed code
   - Execution = interpretation or compilation of the symbolic code
   - Insight Generation = communication back to the author

4. **Future-Proof**: This model naturally extends to bytecode, JIT, AOT, and multi-stage compilation pipelines.

### Alignment with Core Principles

- **"The domain model is the key artifact"**: Insights flow back to improve the domain model, not just the generated code.
- **"Build working code before extracting abstractions"**: We are doing this *after* having a working lowering system and tree-walker design.
- **"Optimize for shipped capability"**: This directly improves the authoring experience, which is a primary user-facing capability.

---

## Consequences

**Positive:**
- Rich, actionable feedback flows back to the authoring model/user from the lowered code
- The system becomes a genuine **conversational symbolic reasoning engine**
- Natural evolution path toward self-improving, self-analyzing models
- Strong debugging, observability, and iterative refinement capabilities
- Leverages existing `Diagnostic` infrastructure — no new parallel type system

**Negative:**
- Increases scope of the analysis layer (more analyzers to maintain)
- Risk of generating noisy or low-value suggestions initially
- Requires careful tuning to avoid "analysis paralysis"
- Adds complexity to the suspension and state inspection model

**Mitigations:**
- Start with a small, high-signal set of post-lowering analyzers (`SemanticCoherenceAnalyzer`, `AuthoringSuggestionGenerator`, `IdempotencySafetyAnalyzer`)
- Make diagnostic generation configurable (severity thresholds, categories, focus areas)
- Use the existing `AnalysisResult.Diagnostics` collection and tooling
- Prioritize diagnostics that map directly to concrete authoring actions (`Suggestion` severity)
- Design suspension points to be semantically meaningful (not every single node)

---

## Implementation Plan

1. **Decision Record** (this document) — accepted
2. **Core Types**: `Insight`, `InsightSeverity`, `ISyntaxInsightAnalyzer`
3. **Insight Analysis Pipeline** — integrate into `AnalyzerBuilder` or as post-processing step
4. **Suspension Model** — update tree-walking VM design to support clean interruption points
5. **First Insight Analyzers** — SemanticCoherenceAnalyzer and AuthoringSuggestionGenerator
6. **Integration with Authoring Surface** — how insights are presented back to user/model

---

## Status

**Proposed** — awaiting confirmation that this direction aligns with the broader neurosymbolic vision and core engineering principles.

If accepted, this becomes a foundational architectural decision for the platform.
