# Remaining lower sites after one-body subscriptions

**Date:** 2026-09-27  
**Status:** follow-up — not CURRENT.

## Action- and stage-scoped policies

`session.Lower` / the operation module carries entity-level policy methods, subscription handlers, and OnEntry/OnExit bodies. Action- and stage-scoped policies are still side-lowered in `RuntimeAnalysisCache.CompletePolicyBodies` and are not module methods.

## Execute-time `DomainExpressionLoweringPass` sites

These still lower a `DomainExpression` at execute time rather than binding a module body:

| File:line | Site |
|-----------|------|
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:646` | `EvaluateParameterBindings` |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:510` | `EvaluateExprOnPeer` |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:659` | `PrevalidateCreateInitializers` |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:721` | `CreateChildInstance` initializer eval |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs:451` | `GetRelatedTargets` filter |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs:506` | `EvaluateBodyOnTarget` (any/all/none/count predicates) |
