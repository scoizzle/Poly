# Follow-ups — PR 57 named-action execute (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr57-6cb49aba-final-boss.md`
- Target: PR 57 SHA `6cb49aba155707ff3b6d76c918d79f3d28a6cf1f` vs `origin/master`
- Mode: re-verify of Razor `docs/agent/reviews/2026-09-06-pr57-6cb49aba-razor-followups.md`
- Model: grok-4.6
- Verdict: ship

## Open bugs (must close before ship)

None.

## Suggestions

None.

## Nits

None.

## Residual (documented, not this PR's ship gate)

Subscriptions / transition batches / missing OnEntry still `LowerActionBody` at execute (`DomainEntityInstance.cs:675`, `:679`; callers `HostAbi.cs:207`, `:280`; `ApplyInitialStageEntryEffects` `:255-256`). Vacuous `TryGetEntryMethod` OnEntry preference remains prior suggestion class (PR 51 F2). Empty `effects.Count == 0` returns before the named bind (`:648-649`) — no Effect-IR fallback.

## Process

None new. Named vs non-named execute is a dual-path; this PR’s stop is `actionName is not null` never `LowerActionBody`. Razor’s DoesNotRelower oracle forces the missing-method sibling (strip module method, assert zero children). Domain-null named sibling is separately asserted.

## Disposition of prior items (Razor @ 6cb49aba)

- **Named-action execute: bind module or throw — never `LowerActionBody`** — **fixed**. `ExecuteEffectList` (`DomainEntityInstance.cs:652-663`); `InvokeActionInternal` passes `action.Name` (`:563-564`). Oracle `InvokeAction_MissingModuleMethod_Throws_DoesNotRelower` (`PipelineTransformationTests.cs:128-161`). Independently re-read this SHA; not chain-trusted.

- **LowerActionBody only for non-named paths** — **confirmed residual**, in scope as documented stop. HostAbi comment (`HostAbi.cs:196-199`) matches.

- **Action-level require stubs** — **fixed**. `EnumerateTypeDefPolicies` (`DomainEntityInstance.Runtime.cs:309-320`).

## Freeze

Filed for ship. Never implement from this review. Never merge. Never force-push product.
