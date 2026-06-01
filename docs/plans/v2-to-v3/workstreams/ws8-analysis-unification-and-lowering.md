# Workstream WS8: Phase 2 — Analysis Unification & Lowering Parity

**Phase**: 2
**Priority**: High (blocks consumer migration)
**Owner**: TBD
**Status**: Planning Complete — Ready for Execution
**Last Updated**: 2026-06

## Goal

Unify the V2 and V3 analysis surfaces on the shared `Syntax/Analysis` infrastructure and achieve lowering parity for core domain concepts (DomainExpression, policies, effects, constraints) through the V3 compilation pipeline (LinqExpressionGenerator + CSharpGenerator).

## Current State (Pre-Phase 2)

### What works well:
- `Syntax/Analysis` infrastructure is complete (AnalysisContext, AnalyzerBuilder, 6 passes, metadata store, incremental support, diagnostics)
- `LinqExpressionGenerator` compiles 40+ AST node types to `System.Linq.Expressions` trees
- `CSharpGenerator` (996 lines) emits full C# text from AST nodes
- `Introspection/` type resolution system is complete (ITypeDefinition, ClrTypeDefinitionRegistry)
- V2 `DomainLoweringGenerator` (1529 lines) lowers V2 domain model to AST — but is coupled to V2 Data/Modeling types

### What's missing:
- **DomainExpression has no lowering path** — V3 `DomainExpression` (PropertyAccess, ParameterAccess, Literal, OwnedAccess, Exists, NotExists, And/Or/Not, Subtract) cannot be lowered to Syntax/Nodes, compiled via LinqExpressionGenerator, or emitted via CSharpGenerator
- **No INodeCompiler registration** — `_customCompilers` extensibility in LinqExpressionGenerator is never populated; domain-specific compilation is unimplemented
- **No unified pipeline** from `Domain` → `DomainExpression` → Syntax/Nodes → compiled code
- **V3 analysis is thin** compared to V2's ~19 analyzers — V3 has only StructuralDomainAnalyzer, SemanticDomainAnalyzer, PolicyConstraintAnalyzer
- **No V3 contract interface generation** — V2 `LowerToContractInterfaces` has no V3 equivalent
- **No V3 test/program generation** — V2 `GenerateTestStatements` has no V3 equivalent

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
