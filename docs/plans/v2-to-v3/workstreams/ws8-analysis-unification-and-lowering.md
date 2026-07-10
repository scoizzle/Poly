# Workstream WS8: Phase 2 — Analysis Unification & Lowering Parity

**Phase**: 2  
**Priority**: **Critical path** (primary focus after Interpretation detour)  
**Owner**: TBD — claim on `master-roadmap.md`  
**Status**: **Active** — DomainExpression→AST done; e2e Domain→VM proofs + V3 contract interface gen are the open gates for Phase 3  
**Last Updated**: 2026-07-10  

> **July 2026 re-entry:** Direct AST→VM-ABI is production-capable; stop treating VM hardening as the WS8 center of gravity. Prefer micro-tasks:
> - `simple-agent-tasks/ws8-e2e-policy-vm-eval.md`
> - `simple-agent-tasks/ws8-inventory-v2-contract-interface-rules.md`
> - `simple-agent-tasks/ws8-domainexpression-lower-smoke-matrix.md`

## Goal

Unify the V2 and V3 analysis surfaces on the shared `Syntax/Analysis` infrastructure and achieve lowering parity for core domain concepts (DomainExpression, policies, effects, constraints) through the V3 compilation pipeline (Syntax AST → LoweringPrep/UopGeneration analysis → VM µops → Vm.Execute).

**Updated: The TreeWalkingInterpreter is removed. The VM (`Poly/Interpretation/Vm/`) is the sole canonical execution engine.** See `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`. LinqExpressionGenerator is test/secondary only. CSharpGenerator is the codegen output.

**Note on recent progress (orchestrator session)**: While the primary deliverables (DomainExpression lowering etc.) remain, the shared foundation has been materially strengthened. See the "Progress Achieved" section below for details on SideEffect DCE refactoring, ControlFlow ownership of reachability/mutation/termination, and the promotion of member mutability semantics to a first-class `Mutability` [Flags] enum on `ITypeMember`. This work directly supports the neurosymbolic "re-analyzable + analysis-driven execution" requirements.

## Current State

### Baseline at Planning (Pre-Phase 2)
- `Syntax/Analysis` infrastructure is complete (AnalysisContext, AnalyzerBuilder, 6 passes, metadata store, incremental support, diagnostics)
- **VM** (`Poly/Interpretation/Vm/`) is the sole canonical execution engine. Pipeline: Syntax AST → LoweringPrep/UopGeneration analysis → µops → Lowering.Assemble → ProgramCompiler.Compile → Vm.Execute
- `LinqExpressionGenerator` compiles 40+ AST node types to `System.Linq.Expressions` trees (secondary — test reference only, may be removed later)
- `CSharpGenerator` (996 lines) emits full C# text from AST nodes (production codegen output)
- `Introspection/` type resolution system is complete (ITypeDefinition, ClrTypeDefinitionRegistry)
- V2 `DomainLoweringGenerator` (1529 lines) lowers V2 domain model to AST — but is coupled to V2 Data/Modeling types

**Deliverable status (July 2026):**
- **DomainExpression Lowering Pass (A)** — ✅ `DomainExpressionLoweringPass.cs` (all expression kinds → Syntax/Nodes). Execution target is direct AST→ABI (`Interpreter.Compile` / `DirectVmAbiEmitter`), not the removed µop/primitive path described in older paragraphs below.
- **Unified Domain → expression → Syntax → VM pipeline** — 🟡 lowering exists; **product gate = e2e tests** (policy/guard on VM with entity args).
- **V3 contract interface generation** — ⬜ still missing vs V2 `LowerToContractInterfaces` (AGENTS.md naming rules).
- **V3 test/program generation** — ⬜ only as first consumers demand (MCP/demos).

**Note on V3 analyzer count (corrected June 2026)**: The original WS8 plan claimed V3 had only 3 analyzers (Structural, Semantic, PolicyConstraint). The actual V3 `DomainModelAnalyzer.cs` registers **17 analyzers**, matching all 10 V2 analyzers plus 7 additional ones. V2 had ~19 analyzers total. The gap is ~2 analyzers — effectively at parity. The shared `Syntax/Analysis` infrastructure is the foundation for both. See the refreshed `ws7-v3-expressiveness-audit.md` for the full corrected audit.

**Note**: A dedicated fundamentals review of the Syntax + Interpretation Analysis pipeline (the shared substrate) was performed. It confirms the IR analysis layer is now solid for neurosymbolic use (purity/DCE/elision, CF reachability+mutation via first-class Mutability, const folding, resolutions, insight hooks). See agent-summary and master-roadmap for full catalog + gated gaps. This does not change WS8 primary deliverables (lowering parity) but improves the target quality and reduces risk.

**The VM (`Poly/Interpretation/Vm/`) IS the primary consumer of the unified analysis surface.** The "RISC IR + stack VM" (originally planned under `Poly/Interpretation/VirtualMachine/`) was delivered under `Poly/Interpretation/Vm/`. It is the sole canonical execution engine. The tree-walking interpreter has been removed. See `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`.

The VM µop instruction set is fully implemented (30+ instruction types: LoadConst, LoadSlot/StoreSlot/IncSlot, BinOp, UnaryOp, Call/CallClosure/CallExternal/CallExternalDirect, BranchIfFalse, Jump, PhiMarker, Dup/Pop, NewArrayOp/ArrayLoad/ArrayStore, etc.). Analysis unification (this workstream) directly enables the "lower once after mature frontend, execute on VM" model: DomainExpression → Syntax AST → LoweringPrep/UopGeneration analysis → µops → Lowering.Assemble → ProgramCompiler.Compile → Vm.Execute.

### Progress Achieved (VM Execution Engine — June 2026 Session)

The VM execution pipeline was substantially hardened with the following deliverables completed in a single extended session:

- **LoadHeapConst lazy allocation (`.rodata` model)** — `LoadHeapConst.ToExpression` now allocates constants on the heap at runtime via `Heap.Allocate(Value)` instead of pushing a pre-loaded handle index. Eliminates the entire constant pre-loading step from `Vm.Execute`. No more handle ordering conflicts between constants and arguments.
- **Clean argument passing** — `VmState.SetArgs(params object?[])` seeds arguments on the value stack, allocating reference types on the heap and placing handles in the correct parameter slots. `PolicyEvaluator.CompileVMPredicate` simplified from `state.Heap.Allocate(e)` + unchecked `RawSlots[0]` to `state.SetArgs(e)`. The fragile pattern that relied on stale `ArrayPool` data (which returns non-zeroed arrays) is gone.
- **String equality via `IsReferenceEquality`** — `BinOp` now has an `IsReferenceEquality` flag (set at compile time by `EmitBinary` based on resolved types). When true, `BinOp.ToExpression` dereferences heap handles via `ArrayAccess(ctx.HeapRawSlots, handle)`, casts to the CLR type via `EqualityType`, and uses `Expression.Equal` which resolves to `op_Equality` (value equality for strings/records, reference equality for entities). Fixed `Policy_NameBasedGuard_EvaluatesCorrectly`.
- **Type system unification via `IsStackValue`** — `PrimitiveType.IsStackValue` property in `Introspection` returns true for numeric types that fit directly on the VM eval stack (int, long, short, byte, float, double, bool), false for heap-indirected types (string, DateTime, Guid, byte[], etc.). `ITypeDefinition.IsStackValue()` extension for domain entities. All 6 scattered type-check chains (`CallExternalDirect` param/return/instance, `CallSiteCompiler.ResolveArg`/`ConvertToStackInt`, `UopGenerationPass.IsHandleType`) consolidated to a single source.
- **CLR Type → PrimitiveType mapping** — `GetPrimitiveType(this Type)` added to `PrimitiveTypeExtensions`, the reverse of the existing `GetClrType()`. Enables `type.GetPrimitiveType()?.IsStackValue() ?? false` in Interpretation layer code.
- **Reflection consolidation via `Ref<T>`** — 8 `typeof(X).GetMethod(nameof(...))` calls replaced with type-safe `Ref<T>.Method(...)` or `Ref.Method(...)` helpers across `LoadHeapConst`, `CallExternalDirect`, `NewArrayOp`, `CountBits`, and `LinqExpressionGenerator` (string.Concat, GetEnumerator, MoveNext, Dispose). Eliminates runtime reflection in hot paths. One exception kept (`"DebugView"` — internal property, not accessible at compile time).
- **`NoDebug` compilation mode** — Strips `_pc` sync preamble (initializes to 0 instead of reading `state.ProgramCounter`), ring register restore preamble, and loop limit checks from compiled delegates. `_pc` writes for `Jump`/`BranchIfFalse` kept (required for φ at convergence points). Benchmark results: Mandelbrot 2ms→1ms (2x), Collatz 391ms→342ms (~12%).
- **`InterpreterResult.GetResult<T>()`** — Generic extraction method handling VM type conversions (long→bool, long→int, long→short, long→byte, `Convert.ChangeType` fallback). Combined with `Vm.Execute` auto-dereference of heap handles (handles > 1 and < Heap.Count return the CLR object directly).
- **Parameter slot assignment fix** — `EmitParameter` now registers bare top-level `Parameter` nodes (not wrapped in a Lambda) at the next available parameter slot. Previously fell through to local slot path, getting `LoadSlot(1)` instead of `LoadSlot(0)`. `SetArgs` wrote to `_slots[0]` but the delegate read `_slots[1]` (stale `ArrayPool` data → `IndexOutOfRangeException`).
- **1316 passing tests** (was 1282 at last count) — New Collatz chain test (871 chain, full benchmark comparison), `LoadHeapConst_StringLiteral_AllocatesOnHeapAndReturnsHandle`. Existing `CountPrimes` tests restored (had been corrupted by an earlier edit that accidentally changed `i = 2` to `i = 3`).
- **Benchmark infrastructure** — `run.sh`: result validation against expected output (`validate_result`), Docker status check, bash 3.2 compat (replaced `declare -A` with `case`). `plot_benchmarks.py`: handles FAILED rows (skips them), handles missing `prep_ms` columns, renders each CSV to matching `.png` (sorted, latest first).
- **Performance benchmark results (all validated correct)**:
  - Sieve: 3ms (tied #1 with C, ahead of Rust 4.7ms, C# 4ms)
  - Mandelbrot: 1ms (after NoDebug 2x improvement)
  - NQueens: 0ms (tied for fastest)
  - Collatz: 342ms (correct output `837799:524`, beats Python 5393ms)

### Progress Achieved (Core Analysis Unification + Introspection — Orchestrator Session)
Significant strengthening of the *shared* `Syntax/Analysis` + introspection foundation that WS8 will rely on (even while DomainExpression lowering remains the main open deliverable):

- **SideEffectAnalysisPass** evolved into a proper, efficient DCE precursor:
  - `AggregateChildren<T>` + direct indexed `Block` handling for true one-pass fused visitation + aggregation (no AnalyzeChildren + separate Compute* walk).
  - Flyweight singletons + sparse metadata (only emit `SideEffectMetadata(false)` for pures; default = "has side effects").
  - Elision of unused pure non-last expressions in blocks + pure initializer/increment inside ForLoops (even when loop is kept).
  - `CanElide` / `HasSideEffects` extensions; two consumers updated (LinqExpressionGenerator, CSharpGenerator — TreeWalker was removed).
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

- **Analysis-before-execution discipline**:
  - The VM pipeline requires a full analysis pass (`LoweringPrep` + `UopGeneration`) before any lowering to µops. Analysis is a prerequisite, not an optimization.
  - Integration test observations (e.g. `ArithmeticParserEvaluatorTests`) highlighted that some paths used lighter analysis (`BuildExpression` only does Type+Member+Scope) vs full pipeline. This surfaced the need for explicit policy in tests vs real usage.
  - `EnsureAnalysisCanDriveExecution` + rich metadata now more consistently available for elision, breakpoints, etc.
- **Fundamentals review of Syntax/Interpretation Analysis pipeline** (orchestrator diagnostic session in response to explicit query): exhaustive catalog + gap analysis (see `agent-summaries/orchestrator-fundamentals-analysis-pipeline-review-2026.md` for details). Confirmed:
  - Present: dual pipeline profiles (full analysis for VM/DCE/insight vs lighter structural for codegen); strong sparse/flyweight/Aggregate/AnyChild/direct-index/hoisted precedent with comments; SideEffect as efficient DCE (elision of pures + loop controls; 2 consumers: LinqExpressionGenerator + CSharpGenerator); ControlFlow owning CFG + const pruning + all 9 reachability/termination items + sound external mutation + pure-vs-effectful infinite + MustExecute; ConstantFolding + resolutions + scope; Mutability integration; extensible Insight + live-state at suspend; domain analyzers sharing substrate (DomainObject : Node) with effect/policy/capability/coherence/suggestion analyzers (some manual walks remain).
  - Integration: Introspection (Mutability + resolved) → purity/CF/mutation/emit; analysis metadata/elision/diags/CFG → VM µop lowering + generators; re-analyzable + suspendable as required.
  - Gaps (real but gated): no dataflow (live vars/reaching defs) or alias/points-to (current conservative soundness via non-pure Invoke + member assigns + volatile + params suffices for existing elision/CF); no interprocedural purity/effect summaries for defined methods (Invoke purity subtree-only); limited const value propagation (no auto fold of CompileTimeConst member reads into GetConstValue); no volatile memory model beyond flag (no barriers/ordering); CFG/MustExecute/Infinite have limited consumers outside tests/diags (lean per principles); demand-driven/lazy subsets not present (eager passes); post-lowering insight layer partial (hooks + domain analyzers + Hint severity + ExecutionInsight exist; DiagnosticSeverity lacks proposed Suggestion/Explanation; decision remains Proposed; no automatic full insight on every lowered tree).
  - Policy surfaced: VM pipeline requires full analysis before execution (`LoweringPrep` + `UopGeneration` passes). `BuildExpression` (and some integration tests) use lighter analysis — explicitly called out in the tree-walking design doc (historical).
  - Overall: core IR analysis (Syntax + Interpretation) is a high-fidelity, efficient, sound substrate for the neurosymbolic "codify → (analyze for DCE/insight/reachability) → lower to VM µops → execute". Directly de-risks WS8 DomainExpression lowering and V3 parity (the lowered trees will benefit from elision, const pruning, mutation awareness via Mutability, etc.).

These changes make the shared `Syntax.Node` IR + `AnalysisResult` a much stronger "executable symbolic medium" for the neurosymbolic platform (aligns with `tree-walking-interpreter-design.md` and `2026-06-post-lowering-insight-analysis.md`). WS8 can now assume better elision, reachability, and mutation awareness when lowering DomainExpression and V3 concepts.

DomainExpression lowering (A–C), V3 analyzer porting, policy/effect lowering, contract generation, and integration tests remain the primary open deliverables for this workstream. The introspection + core IR analysis work above is enabling infrastructure that reduces risk for those deliverables.

## Entry Criteria

- Phase 1 workstreams complete or stable enough to hand off (WS1 foundation, WS5 proofs, incremental analysis)
- DomainChange expanded coverage sufficient to build candidate V3 domains
- `DomainExpression` model is stable (no breaking changes expected)

## Deliverables

### A. DomainExpression Lowering Pass (`DomainExpression -> Syntax/Node`)
- ✅ **COMPLETED June 2026**. File: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`
- Converts all 21 DomainExpression types to Syntax AST (PropertyAccess, ParameterAccess, Literal, OwnedAccess, RelationshipNavigation, Exists, NotExists, Add, Subtract, Multiply, Divide, And, Or, Not, Comparison with 6 ComparisonKinds, DateOperation with 3 kinds)
- Context propagation: PropertyAccess resolves against caller-provided `subject` node; OwnedAccess/RelationshipNavigation update the subject via `Member` chains and recurse
- 29 passing tests covering every node type + complex combinations

### B. VM Integration (replaces old INodeCompiler approach)
- **Not needed.** The lowering pass produces standard Syntax AST. The existing `LoweringPrep` + `UopGeneration` analysis passes convert Syntax AST to VM µops. No custom `INodeCompiler` is required — the VM pipeline consumes Syntax AST natively.
- If specific DomainExpression types need µop-level optimization (e.g., eliding `Member` chains pre-resolution), add a `DomainExpressionPrepPass` in `Interpretation/Analysis/` analogous to `LoweringPrepPass`.

### C. CSharpGenerator DomainExpression Support
- Leverage the lowering pass output — DomainExpression nodes become standard Syntax/Node trees that `CSharpGenerator` already handles
- Add dispatch in `CSharpGenerator.WriteExpression` for any DomainExpression-derived nodes not handled by the lowered Syntax AST (low priority — most will be handled automatically)

### D. V3 Domain Analyzer Passes
- ✅ **COMPLETED (June 2026)** — V3 has 17 analyzers, near parity with V2
- Type resolution for DomainExpression trees handled by existing `StructuralDomainAnalyzer`, `PolicyConstraintAnalyzer`, `ConstraintPropagationAnalyzer`
- `PolicyEvaluator` Analyzer configured with `TypeAndMemberResolver` + `LoweringPreparation` + `UopGeneration` — correct pass pipeline for VM compilation
- No additional analyzer work needed

### E. V3 Policy/Effect/Constraint Lowering
- Port V2 `LowerPolicy`, `LowerRule`, `LowerConstraint`, `LowerEffect` logic to operate on V3 types
- V3 Policy already uses `DomainExpression` — this is primarily about adapting the lowering orchestration
- File: `Poly/DomainModeling/Lowering/PolicyLoweringPass.cs`

### F. V3 Contract Interface Generation
- Port `LowerToContractInterfaces` logic from V2 to operate on V3 Entity/Stage/Action types
- Contract interface naming rules from AGENTS.md: `I{StageName}{EntityName}`, inheritance chain, action placement

### G. Integration Tests
- 29 tests exist for the lowering pass itself (all passing, 1316 total)
- **`PolicyEvaluator.CompileVMPredicate` + `Evaluate`** — End-to-end integration: DomainExpression → Syntax AST → analysis → µops → `ProgramCompiler.Compile` → `Vm.Execute` → `GetResult<bool>()`. Tests cover numeric comparisons (Age > 18), composite guards (Age >= 18 && Age < 21), string comparisons (Name == "Alice"), and full benchmark-style loops (Collatz chain, CountPrimes). All pass.
- **Benchmark integration** — 4 real-world algorithms (Sieve, Mandelbrot, NQueens, Collatz) run through the full VM pipeline and produce correct results validated against expected output. Benchmark runner (`run.sh`) creates temporary projects, runs in Release mode, and validates results.
- Test full pipeline: V3 Domain → evolution → DomainExpression lowering → Syntax AST → VM µops → execute

## Non-Goals (Explicitly Out of Scope for WS8)
- Full 1:1 parity with V2's ~19 analyzers (deferred to later refinement; V3 already at 17)
- Actor/claims-aware lowering (requires V3 Actor model — Phase 4)
- Event subscription + correlation lowering (model exists in V3; lowering deferred to Phase 4)
- Visual metadata/projection support (Phase 4)
- Imported contracts/recipes (Phase 4)

## Exit Criteria

- ✅ **DomainExpression lowering to Syntax/Nodes** — complete (29 tests)
- ✅ **`DomainExpression` → Syntax AST trees can be executed through the VM pipeline** — complete. `PolicyEvaluator.CompileVMPredicate` + `Evaluate` provide the full pipeline: DomainExpression → LoweringPass → analysis → µops → ProgramCompiler → Vm.Execute. Validated with numeric, string, and composite guard expressions.
- ✅ **Benchmark-level VM execution** — 4 real algorithms (Sieve, Mandelbrot, NQueens, Collatz) run through the full pipeline in `CompilationMode.NoDebug` with correct validated results.
- ✅ **1316 tests passing** (was 1282).
- `CSharpGenerator` can emit correct C# for DomainExpression-derived expressions (mostly automatic via lowered Syntax AST)
- At least one end-to-end test: V3 Domain with policy/effect → lower → Syntax AST → VM µops → execute
- Lowering parity for the core concepts listed in deliverables E, F

## Dependencies
- Phase 1 stable (✅)
- DomainChange coverage sufficient for V3 domain construction (expanded in June 2026)

## Parallelism Notes
- ✅ **Deliverable A is complete.** No dependency chain to B/C (VM integration is automatic — no custom compiler needed).
- Deliverables E and F can proceed in parallel (both are V2→V3 porting of existing lowering logic).
- Deliverable G (end-to-end VM integration test) depends on a clear definition of "execute a V3 domain model" — may require E first.

## Related Documents
- `docs/decisions/2026-v2-to-v3-domain-modeling-port.md`
- `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md` (new DomainExpression subtypes that will need lowering in WS8)
- V2 `DomainLoweringGenerator.cs` at `Poly/Data/Modeling/CodeGeneration/`
- V2 `DomainLoweringToCSharpIntegrationTests.cs`
- **Deliverable A**: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` (29 tests at `Poly.Tests/DomainModeling/Lowering/`)
