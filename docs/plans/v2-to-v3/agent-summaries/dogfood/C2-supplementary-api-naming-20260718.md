# C2-supplementary — API Naming Confusion Findings

**Date:** 2026-07-18
**Mission:** C2 Micro Incremental (post-hoc analysis)

## Findings

### C2-F5: `AddActionWithEffect` naming implies modification, creates duplicate
- **Category:** W (Workflow / Affordances)
- **PainScore:** 14 (S=3 F=2 B=3 C=4)
- **Notes:** 
  The `EvolutionBuilder` method `AddActionWithEffect(entity, actionName, effect)` is named
  to suggest it adds an effect to an existing action, but it **creates a new action** with
  an initial effect. When an action named "Return" already exists, calling
  `AddActionWithEffect("Loan", "Return", assignEffect)` produces a "Duplicate member name"
  error instead of adding the effect to the existing action.
- **Expected:** The method should either:
  - (a) Be renamed to `AddActionWithInitialEffect` to clarify it creates a new action, OR
  - (b) Find the existing action by name and add the effect to it
- **Actual:** `AddActionWithEffect` creates a new action causing `Duplicate member name 'Return'`
- **Repro:**
  1. `.AddAction("Loan", "Return")`
  2. `.AddActionWithEffect("Loan", "Return", assignEffect)` ← intent: add effect to existing action
  3. Evolution fails: "Duplicate member name 'Return' in entity 'Loan'"
- **Workaround:** Use `AddEffectToAction(entityName, actionName, effect)` instead
- **SuggestedBacklogBucket:** effect-micro (effect editing APIs)

### C2-F6: No unified "add effect to action" convenience in builder chain
- **Category:** T (Tool gap)
- **PainScore:** 12 (S=3 F=1 B=3 C=4)
- **Notes:**
  The fluent builder chain has `AddActionWithEffect` (creates new action + effect) and
  `AddStageTransitionEffect` (adds transition effect to existing action), but there's no
  consistent pattern for adding an arbitrary effect to an existing action in the chain.
  `AddEffectToAction` exists but is not discoverable — it's not named like other effect methods.
- **Expected:** Consistent naming like `AddEffectToAction`, `AddStageTransitionEffectToAction`, etc.
- **Actual:** Mix of `AddActionWithEffect` (prefix), `AddStageTransitionEffect` (no "action" in name), `AddEffectToAction` (suffix)
- **Repro:**
  1. Looking at API: `AddActionWithEffect` suggests "add effect to action"
  2. But it creates a new action
  3. Correct method `AddEffectToAction` is easy to miss
- **Workaround:** Once discovered, `AddEffectToAction` works correctly
- **SuggestedBacklogBucket:** effect-micro
