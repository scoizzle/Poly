# Remaining lower sites after one-body subscriptions

**Date:** 2026-09-27  
**Status:** follow-up — not CURRENT.

## Action- and stage-scoped policies

Closed 2026-10-01. `session.Lower` emits one bool method per policy name (entity, stage, and action-local definitions). A second definition of that name is a structural error. `CompletePolicyBodies` fills only a name the module did not emit. Domain-bound invoke does not re-check stage policies.

## Execute-time `DomainExpressionLoweringPass` sites

Grep of `new DomainExpressionLoweringPass` under `Poly/DomainModeling/Runtime` on 2026-10-05. #94 deleted the uncalled helpers `EvaluateParameterBindings`, `BindPeerInEffect`, and `EvaluateExprOnPeer`.

| File | Site | Caller |
|------|------|--------|
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` | `PrevalidateCreateInitializers` | `ProbeCreate`, with bindings from `ValuesAsLiteralBindings` |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` | `CreateChildInstance` initializer eval | `Create`, and `CreateIn` through `ExecuteCreateInRelationship`, pass literal bindings |

Quantifier predicates no longer re-lower at execute (P3-A #79: `EvaluateBodyOnTarget` / Store quantifier jobs gone). The two sites above still lower literal bindings. That residual is recorded here and is not CURRENT.
