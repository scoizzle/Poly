# Anti-Pattern 003: Extension-Point Accretion

**Problem:** The `AnalyzerBuilder` registration surface accumulates `Use*` extension methods that register no-op passes or pass whose output has no consumer. `UseAnalyzerVisitTracking()` does nothing. `UseStackDepthAnalysis()` registered a pass whose output was never consumed (now removed). Each dead entry point normalizes the pattern, making it harder to distinguish genuinely needed passes from accumulated hooks.

## Current State

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

## Plan

1. **Remove `UseAnalyzerVisitTracking()`.** It returns the builder unchanged. The actual visit tracking (`TryBeginAnalyzerVisit`) works independently through `ConditionalWeakTable` on `AnalysisContext` — it doesn't need registration. This was a future hook that was never wired.

2. **Add a policy:** Every `Use*` extension method must register a pass whose `Analyze` method stores metadata that is consumed by at least one code path outside the pass itself. Passes that store self-referential metadata (data only read within the same pass) are not eligible for their own registration hook — they should be inlined into an existing pass or removed.

3. **Audit existing `Use*` methods** against this policy. If a method fails the check, either wire up the consumer or remove the method.

**Lines saved:** ~5 (`UseAnalyzerVisitTracking`).

**Risk:** None. Removing a no-op registration hook changes nothing.

**Timeline:** 15 minutes.
