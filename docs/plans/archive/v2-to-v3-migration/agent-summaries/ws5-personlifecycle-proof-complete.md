# Agent Summary: WS5 PersonLifecycle Proof — Unblocked and Passing

**Date**: 2026-05-31  
**Agent**: Grok (opencode orchestrator)  
**Workstream**: WS5 (Proof on Examples)  
**Previous agent**: Left off with `PersonLifecycle_DocumentedShape_ProvenViaEvolutionLayer` test failing (1/1024 failed).

## What was blocking

The test constructs the documented `PersonLifecycle` shape entirely through the evolution layer API. The `Die` action is defined on the `Alive` stage (via `AddActionToStage`), then enhanced with parameters, effects, and result via entity-level helpers like `AddParameterToAction("Person", "Die", ...)` and `AddEffectToAction("Person", "Die", ...)`.

The action-targeting `DomainChange` subtypes (`AddParameterToActionChange`, `AddEffectToActionChange`, etc.) only searched **entity-level** actions (`e.Actions`), never **stage-level** actions (`e.Stages[*].Actions`). Since `Die` lives on the stage, these changes silently did nothing.

## What was fixed

All 7 action-targeting change types in `Poly/DomainModeling/Evolution/DomainChange.cs` were updated to **fall back to stage-level actions** when the named action is not found at entity level:

- `AddParameterToActionChange`
- `RemoveParameterFromActionChange`  
- `AddEffectToActionChange`
- `RemoveEffectFromActionChange`
- `SetActionResultChange`
- `AddPolicyToActionChange`
- `RemovePolicyFromActionChange`

Additionally, a test assertion was checking `step3.Analysis.Diagnostics` for "Add Stage 'Alive'" which happened in `step2`. Fixed to check `step2` for stage messages and `step3` for action messages.

## Current state

- **Build**: Clean (0 warnings, 0 errors)
- **Tests**: 1024/1024 passing
- **Key proof**: `PersonLifecycle_DocumentedShape_ProvenViaEvolutionLayer` passes — the canonical documented PersonLifecycle shape (with complex DomainExpression policies, Exists/NotExists + Owned guards, OnEntry Publish bindings including Subtract for LifeSpan, events, ValueTypes, stage actions, Create + Transition effects) is proven end-to-end through the public `DomainEvolution.Evolve()` surface.

## Remaining WS5 work per master roadmap

The PersonLifecycle proof is now green. Next steps per the plan:
- Tackle the smallest slice of a real roadblock (Library domain recommended — start with a working `CheckoutBook` or the calculation aspect of `RenewLoan` to surface exact remaining gaps).
- Build the working proof test first; only enhance `EvolutionBuilder` if the documented shape literally cannot be expressed.
- Treat discovered gaps as high-fidelity input to Phase 4.
