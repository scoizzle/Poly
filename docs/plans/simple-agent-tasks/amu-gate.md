# AMU suite gate

**Suite:** [`amu-README.md`](./amu-README.md)  
**Status:** `[ ]`

## Checks

| ID | Check | Status |
|----|--------|--------|
| G1 | W0 inventory doc exists under `docs/plans/` or task notes; residual scan list current | `[ ]` |
| G2 | EffectAnalyzer / PolicyConstraintAnalyzer / SubscriptionAnalyzer: domain-keyed name resolve via catalog helpers when analysis/context has domain | `[ ]` |
| G3 | No new `Relationships.FirstOrDefault` in those three for product domain-bound paths (or justified exception in notes) | `[ ]` |
| G4 | Storage path prefers EntityStructure when present; Dependencies declared for consumers of topology/structure | `[ ]` |
| G5 | Exporter + EffectLowering: enum/type/rel lookups use metadata when Analysis present | `[ ]` |
| G6 | MCP `get_domain_analysis` (or thin facts) exposes aggregate and/or subscription/capability summary without second store | `[ ]` |
| G7 | Build + full suite green; pre-ship review | `[ ]` |

## Notes

_Gate ceremony after W4._
