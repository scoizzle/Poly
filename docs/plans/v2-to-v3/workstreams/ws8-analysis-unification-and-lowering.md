# Workstream WS8: Phase 2 — Analysis Unification & Lowering Parity

**Phase**: 2
**Priority**: High (blocks consumer migration)
**Owner**: TBD
**Status**: In Progress (core shared analysis surface + member mutability modeling significantly advanced via orchestrator session; DomainExpression lowering + full V3 parity still primary deliverables)
**Last Updated**: 2026-06 (post-orchestrator analysis unification + CompileTimeConst-implies-ReadOnlyAfterInit session)

## Goal

Unify the V2 and V3 analysis surfaces on the shared `Syntax/Analysis` infrastructure and achieve lowering parity for core domain concepts (DomainExpression, policies, effects, constraints) through the V3 compilation pipeline (LinqExpressionGenerator + CSharpGenerator).

**Note on recent progress (orchestrator session)**: While the primary deliverables (DomainExpression lowering etc.) remain, the shared foundation has been materially strengthened. See the "Progress Achieved" section below for details on SideEffect DCE refactoring, ControlFlow ownership of reachability/mutation/termination, and the promotion of member mutability semantics to a first-class `Mutability` [Flags] enum on `ITypeMember`. This work directly supports the neurosymbolic "re-analyzable + analysis-driven execution" requirements.

## Current State

### Baseline at Planning (Pre-Phase 2)
- `Syntax/Analysis` infrastructure is complete (AnalysisContext, AnalyzerBuilder, 6 passes, metadata store, incremental support, diagnostics)
- `LinqExpressionGenerator` compiles 40+ AST node types to `System.Linq.Expressions` trees
- `CSharpGenerator` (996 lines) emits full C# text from AST nodes
- `Introspection/` type resolution system is complete (ITypeDefinition, ClrTypeDefinitionRegistry)
- V2 `DomainLoweringGenerator` (1529 lines) lowers V2 domain model to AST — but is coupled to V2 Data/Modeling types

**What's still missing (core deliverables unchanged):**
- **DomainExpression has no lowering path** — V3 `DomainExpression` (PropertyAccess, ParameterAccess, Literal, OwnedAccess, Exists, NotExists, And/Or/Not, Subtract) cannot be lowered to Syntax/Nodes, compiled via LinqExpressionGenerator, or emitted via CSharpGenerator
- **No INodeCompiler registration** — `_customCompilers` extensibility in LinqExpressionGenerator is never populated; domain-specific compilation is unimplemented
- **No unified pipeline** from `Domain` → `DomainExpression` → Syntax/Nodes → compiled code
- **V3 analysis is thin** compared to V2's ~19 analyzers — V3 has only StructuralDomainAnalyzer, SemanticDomainAnalyzer, PolicyConstraintAnalyzer
- **No V3 contract interface generation** — V2 `LowerToContractInterfaces` has no V3 equivalent
- **No V3 test/program generation** — V2 `GenerateTestStatements` has no V3 equivalent

### Progress Achieved (Core Analysis Unification + Introspection — Orchestrator Session)
Significant strengthening of the *shared* `Syntax/Analysis` + introspection foundation that WS8 will rely on (even while DomainExpression lowering remains the main open deliverable):

- **SideEffectAnalysisPass** evolved into a proper, efficient DCE precursor:
  - `AggregateChildren<T>` + direct indexed `Block` handling for true one-pass fused visitation + aggregation (no AnalyzeChildren + separate Compute* walk).
  - Flyweight singletons + sparse metadata (only emit `SideEffectMetadata(false)` for pures; default = "has side effects").
  - Elision of unused pure non-last expressions in blocks + pure initializer/increment inside ForLoops (even when loop is kept).
  - `CanElide` / `HasSideEffects` extensions; three consumers updated (TreeWalker `EvaluateBlock`, LinqExpressionGenerator, CSharpGenerator).
  - Directly enables skipping pure work in interpretation and lowering.

- **ControlFlowAnalysisPass** now owns advanced control/reachability (moved from SideEffect where it didn't belong):
  - Infinite loop detection fully migrated (pure const-true condition + no mutation to cond vars in body/increment → prune exit edge, `InfiniteLoopMetadata(true, hasEffects)`, `CF0003` diag, post-loop code marked `Elidable` + dead).
  - Constant/pure condition pruning for IfStatement, loops, SwitchStatement (impossible branches pruned in CFG, dead alternatives marked `Elidable`, specific diags like "condition constantly false").
  - Switch exhaustiveness + dead case analysis when value is pure/constant.
  - Exception/throw reachability in TryCatchFinally (dead catches pruned when no throw in try subtree).
  - Must-execute / post-dominance facts.
  - Labeled gotos + dead label detection.
  - Pure-vs-effectful infinite distinction.
  - Deeper integration with elision metadata.
  - Lightweight helpers (`CollectVariables`, `HasMutationToVars`) now account for external state (parameters, non-pure `Invoke`s, `SuspendNode`, member/index assigns) for soundness.

- **First-class `Mutability` enum on `ITypeMember`** (replacing three separate booleans on ITypeField/ITypeProperty):
  - `[Flags]` `Mutability { Mutable, ReadOnlyAfterInit, CompileTimeConst (automatically implies ReadOnlyAfterInit), VolatileAccess }`.
  - Promoted to base interface as the canonical answer to "Is this thing mutable, or does accessing it cause mutations?"
  - Clr impls: reflection assumptions (`IsLiteral` → CompileTimeConst, `IsInitOnly` → ReadOnlyAfterInit) + safe `Mutable` fallbacks for unknowable cases (properties, external assemblies).
  - AST nodes (`FieldDefinitionNode`, `PropertyDefinitionNode`) carry the flags; `Ast*` wrappers surface the enum.
  - Consumed in SideEffect (VolatileAccess forces side effects → no elision), CF mutation detection (CompileTimeConst assignments don't count as runtime mutations; VolatileAccess counts as impact), C# emission (correct `const`/`readonly`/`volatile` prefixes, with const taking precedence).
  - Directly supports elision safety, CF termination analysis, and the "un-knowable impact" requirement from volatile discussions.

- **Analysis-before-interpret discipline**:
  - `TreeWalkingInterpreter` (when no pre-`AnalysisResult` passed) now consistently runs the full `AnalyzeForEvaluation` pipeline internally (`ConstantFolding` → `SideEffectAnalysis` → `ControlFlowAnalysis` + basics).
  - Integration test observations (e.g. `ArithmeticParserEvaluatorTests`) highlighted that some paths used lighter analysis (`BuildExpression` only does Type+Member+Scope) vs full pipeline. This surfaced the need for explicit policy in tests vs real usage.
  - `EnsureAnalysisCanDriveExecution` + rich metadata now more consistently available for elision, breakpoints, etc.

These changes make the shared `Syntax.Node` IR + `AnalysisResult` a much stronger "executable symbolic medium" for the neurosymbolic platform (aligns with `tree-walking-interpreter-design.md` and `2026-06-post-lowering-insight-analysis.md`). WS8 can now assume better elision, reachability, and mutation awareness when lowering DomainExpression and V3 concepts.

DomainExpression lowering (A–C), V3 analyzer porting, policy/effect lowering, contract generation, and integration tests remain the primary open deliverables for this workstream. The introspection + core IR analysis work above is enabling infrastructure that reduces risk for those deliverables.

## Entry Criteria

- Phase 1 workstreams complete or stable enough to hand off (WS1 foundation, WS5 proofs, incremental analysis)
- DomainChange expanded coverage sufficient to build candidate V3 domains
- `DomainExpression` model is stable (no breaking changes expected)

## Deliverables

### A. DomainExpression Lowering Pass (`DomainExpression -> Syntax/Node`)
- Convert `DomainExpression.PropertyAccess` → AST member access
- Convert `DomainExpression.ParameterAccess` → AST parameter reference
- Convert `DomainExpression.Literal` → AST literal
- Convert `DomainExpression.OwnedAccess` → AST chained member access
- Convert `DomainExpression.Exists`/`NotExists` → AST null checks
- Convert `DomainExpression.And`/`Or`/`Not` → AST boolean operators
- Convert `DomainExpression.Subtract` → AST arithmetic
- File: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`

### B. DomainExpression INodeCompiler Registration
- Implement `INodeCompiler` that handles translated DomainExpression nodes in `LinqExpressionGenerator`
- File: `Poly/Interpretation/LinqExpressions/DomainExpressionCompiler.cs`

### C. CSharpGenerator DomainExpression Support
- Add dispatch in `CSharpGenerator.WriteExpression` for DomainExpression-derived nodes
- Minimal change — leverages the lowering pass output so DomainExpression nodes become standard Syntax/Node trees

### D. V3 Domain Analyzer Passes
- Port V3-specific analyzers to the `INodeAnalyzer` pattern if not already there
- Add type resolution support for DomainExpression trees in the analysis pipeline

### E. V3 Policy/Effect/Constraint Lowering
- Port V2 `LowerPolicy`, `LowerRule`, `LowerConstraint`, `LowerEffect` logic to operate on V3 types
- V3 Policy already uses `DomainExpression` — this is primarily about adapting the lowering orchestration
- File: `Poly/DomainModeling/Lowering/PolicyLoweringPass.cs`

### F. V3 Contract Interface Generation
- Port `LowerToContractInterfaces` logic from V2 to operate on V3 Entity/Stage/Action types
- Contract interface naming rules from AGENTS.md: `I{StageName}{EntityName}`, inheritance chain, action placement

### G. Integration Tests
- One test per lowerable DomainExpression node type
- Test lowering → LinqExpressionGenerator compiles and evaluates correctly
- Test full pipeline: V3 Domain → evolution changes → DomainExpression lowering → C# emission

## Non-Goals (Explicitly Out of Scope for WS8)
- Full 1:1 parity with V2's ~19 analyzers (deferred to later refinement)
- Actor/claims-aware lowering (requires V3 Actor model — Phase 4)
- Event subscription + correlation lowering (requires V3 EventSubscription model — Phase 4)
- Visual metadata/projection support (Phase 4)
- Imported contracts/recipes (Phase 4)

## Exit Criteria

- `DomainExpression` trees can be lowered to Syntax/Nodes and compiled through LinqExpressionGenerator
- `CSharpGenerator` can emit correct C# for DomainExpression-derived expressions
- At least one end-to-end test: V3 Domain with policy/effect → lower → compile → execute
- All Phase 1 tests continue to pass
- Lowering parity for the core concepts listed in deliverables A–F

## Dependencies
- Phase 1 stable (✅)
- DomainChange coverage sufficient for V3 domain construction (expanded in June 2026)

## Parallelism Notes
- Deliverables A–C are sequential (lowering → compiler → emitter)
- Deliverables D–G can proceed in parallel once A is stable
- Suitable for 2–3 agents working concurrently after A and the pipeline interface are established

## Related Documents
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`
- `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`
- `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md` (new DomainExpression subtypes that will need lowering in WS8)
- V2 `DomainLoweringGenerator.cs` at `Poly/Data/Modeling/CodeGeneration/`
- V2 `DomainLoweringToCSharpIntegrationTests.cs`
