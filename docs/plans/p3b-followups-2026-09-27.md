# Remaining lower sites after one-body subscriptions

**Date:** 2026-09-27  
**Status:** follow-up — not CURRENT.

## Action- and stage-scoped policies

Closed 2026-10-01. `session.Lower` emits one bool method per policy name (entity, stage, and action-local definitions). A second definition of that name is a structural error. `CompletePolicyBodies` fills only a name the module did not emit. Domain-bound invoke does not re-check stage policies.

## Execute-time `DomainExpressionLoweringPass` sites

Grep of `new DomainExpressionLoweringPass` under `Poly/DomainModeling/Runtime` on 2026-10-01:

| File:line | Site | Caller |
|-----------|------|--------|
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:667` | `EvaluateParameterBindings` | none |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:496` | `EvaluateExprOnPeer` | only `BindPeerInEffect` (`:487`), which itself has no external caller (`:430`) |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:645` | `PrevalidateCreateInitializers` | live; callers pass literal bindings |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:707` | `CreateChildInstance` initializer eval | live; callers pass literal bindings |

Quantifier predicates no longer re-lower at execute (P3-A #79: `EvaluateBodyOnTarget` / Store quantifier jobs gone). The uncalled three are not the semantic hole. Deleting them is still open (`docs/agent/reviews/2026-10-01-platform-contract-followups.md` F7).
