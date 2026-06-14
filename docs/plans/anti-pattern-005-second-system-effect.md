# Anti-Pattern 005: Second-System Effect (V2 + V3 Domain Modeling)

**Problem:** Two complete implementations of the same concept at ~92,000 lines combined. V3 was designed to fix V2's mutation tax but already has 66 `DomainChange` subtypes vs V2's 42 `DomainMutationIntent` subtypes — 57% larger. V3 has 17 registered analyzers + 3 unregistered vs V2's 10. V3 builders have one consumer (an example file). V3 evolution layer has zero production consumers.

## Plan

### Option A: Cut Over to V3

1. **Port the remaining V2→V3 type gaps:** `Actor`, `ActorClaimMapping`, `Rule` system (5 subtypes), `ActionTrigger` (Command/Event/Cron), `EventSubscriptionAudience`. These are required for MCP migration.

2. **Port the V2-specific analyzers to V3:** `ActionEventQualityAnalyzer` is the only V2 analyzer not ported.

3. **Register the 3 unregistered V3 analyzers:** `SemanticCoherenceAnalyzer`, `IdempotencySafetyAnalyzer`, `AuthoringSuggestionGenerator` are complete but never wired into `DomainModelAnalyzer.BuildPipeline()`.

4. **Build V3→V2 adapter for MCP:** The MCP server (`DomainTools.cs`) is the sole production consumer. Rather than rewriting it, build a thin adapter that translates V3 domain models back to V2 shapes for the MCP endpoints.

5. **Cut over:** Deploy the adapter, switch the MCP server to V3, verify the 32 test files pass against V3 output.

**Timeline:** 4-6 weeks.

### Option B: Consolidate Into V2

1. **Port the V3-unique analyzers to V2:** `EffectOrderingAnalyzer`, `EventFlowAnalyzer`, `ReplaySafetyAnalyzer`, `CorrelationAnalyzer`, `CausalityAnalyzer`, `EventContractAnalyzer`, `RuleCoverageAnalyzer`, `ActionParameterUsageAnalyzer` — each adds analysis capability that V2 doesn't have.

2. **Port the V3-unique types to V2:** `ValueType`, `InvocationResult`, `OnEntry/OnExitEffects`, `DomainExpression`-based policies.

3. **Freeze V3:** No new V3 code. Remove the 3 unregistered analyzers. Archive the evolution layer.

4. **Single codebase:** Maintain only V2 going forward.

**Timeline:** 2-3 weeks.

### Option C: Do Nothing

Continue dual maintenance with V2 as production and V3 as strategic target. Accept the codebase bloat as a cost of the ongoing migration.

**Risk:** Low but cumulative — the divergence between V2 and V3 grows over time, making eventual cutover harder.

## Recommendation

Option B is the fastest path to a single codebase. The V3 analyzers are the primary value — they represent analysis logic that V2 doesn't have. Porting them to V2 is cheaper than porting V2's 43 external consumers (including MCP) to V3. Option B leaves V3 as a migration artifact that can be archived once the analyzer port is complete.
