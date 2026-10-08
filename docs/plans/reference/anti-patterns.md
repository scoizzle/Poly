# Anti-patterns

## Anti-Pattern 001: Duplicate Tree Walks

**Problem:** `TypeResolver` and `MemberResolver` both walk the entire AST independently calling the same `MethodInvocationSemanticResolver`/`ConstructorInvocationSemanticResolver`. The first pass resolves the type and discards the resolved member. The second pass re-resolves the same nodes to store member metadata. ~O(2n) work for ~O(n) results.

### Plan

1. **Add `SetResolvedMember` to `TypeResolver`.** When `TypeResolver.ResolveMethodInvocationType` calls `MethodInvocationSemanticResolver.ResolveMethod`, the returned `ITypeMethod` is already available. Store it via `context.SetResolvedMember(invoke, method)` at the same time as storing the resolved type. Same for constructor invocations.

2. **Remove the `MemberResolver` pass registration from all pipelines.** The `MemberResolver.Analyze` method walks the entire tree, but its work is now done inline in `TypeResolver`. Remove `UseMemberResolver()` from every pipeline.

3. **Remove `MemberResolutionPass.cs`.** The file and its extension methods become dead and can be deleted.

**Lines saved:** ~159 (the entire `MemberResolutionPass.cs`), minus ~15 lines added to `TypeResolver` = ~144 net.

**Risk:** Low. Both passes call the same resolvers. The only difference is TypeResolver currently drops the result after extracting the return type. The MemberResolutionMetadata type and its query methods (`GetResolvedMember`) stay unchanged — they just get populated by a different pass.

**Timeline:** 1-2 hours.

### Why This Wasn't Done Before

The original audit flagged this but the recommendation was "keep separate — distinct semantic concerns." That was the right call for code clarity in isolation. But the performance cost (a second full tree walk) is real and the merge is mechanically simple. The passes call the same resolvers for the same nodes. There's no semantic coupling beyond what already exists.

## Anti-Pattern 003: Extension-Point Accretion

**Problem:** The `AnalyzerBuilder` registration surface accumulates `Use*` extension methods that register no-op passes or pass whose output has no consumer. `UseAnalyzerVisitTracking()` does nothing. `UseStackDepthAnalysis()` registered a pass whose output was never consumed (now removed). Each dead entry point normalizes the pattern, making it harder to distinguish genuinely needed passes from accumulated hooks.

### Current State

| Extension | Status |
|---|---|
| `UseTypeResolver()` | Active |
| `UseMemberResolver()` | Active |
| `UseVariableScopeValidator()` | Active |
| `UseConstantFolding()` | Active |
| `UseSideEffectAnalysis()` | Active |
| `UseControlFlowAnalysis()` | Active |
| `UseDefiniteAssignmentAnalysis()` | Active |
| `UseThisReferenceContext()` | Active |
| `UseLambdaReturnTypeResolution()` | Active |
| `UseStackDepthAnalysis()` | Removed |
| `UseAnalyzerVisitTracking()` | Still present, does nothing |

### Plan

1. **Remove `UseAnalyzerVisitTracking()`.** It returns the builder unchanged. The actual visit tracking (`TryBeginAnalyzerVisit`) works independently through `ConditionalWeakTable` on `AnalysisContext` — it doesn't need registration. This was a future hook that was never wired.

2. **Add a policy:** Every `Use*` extension method must register a pass whose `Analyze` method stores metadata that is consumed by at least one code path outside the pass itself. Passes that store self-referential metadata (data only read within the same pass) are not eligible for their own registration hook — they should be inlined into an existing pass or removed.

3. **Audit existing `Use*` methods** against this policy. If a method fails the check, either wire up the consumer or remove the method.

**Lines saved:** ~5 (`UseAnalyzerVisitTracking`).

**Risk:** None. Removing a no-op registration hook changes nothing.

**Timeline:** 15 minutes.

## Anti-Pattern 004: Interface Inheritance with `new` (ITypeMethod / ITypeConstructor)

**Problem:** `ITypeMethod` and `ITypeConstructor` both use `new IEnumerable<IParameter> Parameters` to tighten `ITypeMember.Parameters` (nullable) to non-null. This is legal C# but creates a runtime ambiguity — callers holding an `ITypeMember` reference see nullable; callers holding `ITypeMethod` see non-null. Same pattern in `ClrMethod`, `ClrConstructor`, `AstMethodDefinition`, `AstConstructorDefinition`.

### Plan

1. **Make `ITypeMember.Parameters` non-null.** Change the signature from `IEnumerable<IParameter>?` to `IEnumerable<IParameter>`. Have field implementations return `[]` instead of null.

2. **Remove the `new` declaration from `ITypeMethod` and `ITypeConstructor`.** They no longer need to override the nullability. The two interfaces become empty — they add nothing over `ITypeMember`. Decide whether to keep them as empty marker interfaces or remove them and have `ITypeDefinition.Methods` return `IEnumerable<ITypeMember>`.

3. **Update implementations.** `ClrTypeField` currently returns null for `Parameters`. Change to `[]`. Same for the Ast equivalents.

4. **Remove the marker interfaces** if the decision is to eliminate them. This requires updating all consumers that accept `ITypeMethod` or `ITypeConstructor` — mostly in MemberResolutionPass and TypeResolutionPass — to use `ITypeMember` instead.

**Lines saved:** ~24 (2 interface files) + ~30 lines of implementation boilerplate across Clr and Ast types = ~54 net.

**Risk:** Medium — this is an interface-breaking change. Every consumer that references `ITypeMethod` or `ITypeConstructor` needs to be updated. The change itself is mechanical (replace with `ITypeMember`) but requires touching ~20 call sites.

**Timeline:** 1-2 hours.

## Anti-Pattern 005: Second-System Effect (V2 + V3 Domain Modeling)

**Problem:** Two complete implementations of the same concept at ~92,000 lines combined. V3 was designed to fix V2's mutation tax but already has 66 `DomainChange` subtypes vs V2's 42 `DomainMutationIntent` subtypes — 57% larger. V3 has 17 registered analyzers + 3 unregistered vs V2's 10. V3 builders have one consumer (an example file). V3 evolution layer has zero production consumers.

### Plan

#### Option A: Cut Over to V3

1. **Port the remaining V2→V3 type gaps:** `Actor`, `ActorClaimMapping`, `Rule` system (5 subtypes), `ActionTrigger` (Command/Event/Cron), `EventSubscriptionAudience`. These are required for MCP migration.

2. **Port the V2-specific analyzers to V3:** `ActionEventQualityAnalyzer` is the only V2 analyzer not ported.

3. **Register the 3 unregistered V3 analyzers:** `SemanticCoherenceAnalyzer`, `IdempotencySafetyAnalyzer`, `AuthoringSuggestionGenerator` are complete but never wired into `DomainModelAnalyzer.BuildPipeline()`.

4. **Build V3→V2 adapter for MCP:** The MCP server (`DomainTools.cs`) is the sole production consumer. Rather than rewriting it, build a thin adapter that translates V3 domain models back to V2 shapes for the MCP endpoints.

5. **Cut over:** Deploy the adapter, switch the MCP server to V3, verify the 32 test files pass against V3 output.

**Timeline:** 4-6 weeks.

#### Option B: Consolidate Into V2

1. **Port the V3-unique analyzers to V2:** `EffectOrderingAnalyzer`, `EventFlowAnalyzer`, `ReplaySafetyAnalyzer`, `CorrelationAnalyzer`, `CausalityAnalyzer`, `EventContractAnalyzer`, `RuleCoverageAnalyzer`, `ActionParameterUsageAnalyzer` — each adds analysis capability that V2 doesn't have.

2. **Port the V3-unique types to V2:** `ValueType`, `InvocationResult`, `OnEntry/OnExitEffects`, `DomainExpression`-based policies.

3. **Freeze V3:** No new V3 code. Remove the 3 unregistered analyzers. Archive the evolution layer.

4. **Single codebase:** Maintain only V2 going forward.

**Timeline:** 2-3 weeks.

#### Option C: Do Nothing

Continue dual maintenance with V2 as production and V3 as strategic target. Accept the codebase bloat as a cost of the ongoing migration.

**Risk:** Low but cumulative — the divergence between V2 and V3 grows over time, making eventual cutover harder.

### Recommendation

Option B is the fastest path to a single codebase. The V3 analyzers are the primary value — they represent analysis logic that V2 doesn't have. Porting them to V2 is cheaper than porting V2's 43 external consumers (including MCP) to V3. Option B leaves V3 as a migration artifact that can be archived once the analyzer port is complete.

## Anti-Pattern 007: Single-Point Production Dependency

**Problem:** The entire V2 domain modeling system (~56,000 lines) has exactly one production consumer: `Poly.Mcp/DomainTools.cs`. Benchmarks and tests exercise it, but the MCP server is the sole path that ships. This concentrates risk — any refactoring, simplification, or migration must not break the MCP contract, and there's no second production consumer to validate changes against.

### Plan

1. **Build a command-line domain validation tool.** A small CLI that loads a domain model, runs the full analysis pipeline, and exits with a summary of diagnostics and model statistics. This exercises the same paths as MCP but independently and on-demand.

2. **Create benchmark profiles for the MCP's domain mutation path.** The `DomainMutationIntentEngine` → `Mutation.Apply()` → `DomainModelAnalyzer.Analyze()` chain is the MCP's critical path. A benchmark that exercises this end-to-end gives a second signal that changes to domain modeling are safe.

3. **Add integration tests that exercise the full lowering pipeline** from domain model → `DomainLoweringGenerator` → `Lowering.Lower` → `Vm.Execute`. This validates the complete code path without needing the MCP server running.

4. **Document the MCP contract surface explicitly.** Define which types and methods constitute the MCP contract (`DomainMutationIntent` subtypes, `DomainMutationIntentEngine`, the analysis pipeline). This makes it explicit which code is under the highest change scrutiny.

**Timeline:** 1-2 weeks for the CLI tool and benchmarks. Documentation is ongoing.

### Risk Reduction

These steps don't eliminate the single-point dependency — MCP is still the only shipping consumer. But they provide independent signals that changes are safe. A CLI tool catches breakage. Benchmarks catch regressions. Integration tests validate the full pipeline. Documentation makes the contract explicit rather than implicit.
