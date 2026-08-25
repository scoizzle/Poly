# AMU suite gate

**Suite:** [`amu-README.md`](./amu-README.md)  
**Status:** `[x]` — PASSED 2026-08-06 (residual reopen F1–F3/F4 **closed**; see [`pipeline-followups-2026-08-06.md`](./pipeline-followups-2026-08-06.md))

## Checks

| ID | Check | Status |
|----|--------|--------|
| G1 | W0 inventory doc exists under `docs/plans/` or task notes; residual scan list current | `[x]` |
| G2 | EffectAnalyzer / PolicyConstraintAnalyzer / SubscriptionAnalyzer: domain-keyed name resolve via catalog helpers when analysis/context has domain | `[x]` |
| G3 | No new `Relationships.FirstOrDefault` in those three for product domain-bound paths (or justified exception in notes) | `[x]` |
| G4 | Storage path prefers EntityStructure when present; Dependencies declared for consumers of topology/structure | `[x]` |
| G5 | Exporter + EffectLowering: enum/type/rel lookups use metadata when Analysis present | `[x]` |
| G6 | MCP `get_domain_analysis` (or thin facts) exposes aggregate and/or subscription/capability summary without second store | `[x]` |
| G7 | Build + full suite green; pre-ship review | `[x]` |

## Notes

- G1: `docs/plans/amu-inventory-20260806.md` (24 bags publish/consume, residual rows R01–R24) + §3.4 post-review verification.
- G2: W1.1/W1.2/W1.3 replaced linear scans in the three analyzers with catalog/RLM helpers. **Reopen F1 (closed):** EffectAnalyzer now mirrors Policy (`GetRelationshipLookup(domain) ?? RLM`), declares `DomainCatalogPass` in `Dependencies`, and reports a structural failure when bags are missing on domain-bound analyze — **bag-skip is no longer silent** (tests: `EffectAnalyzerFailClosedTests`).
- G3: EffectFactsPass create-in resolve now uses the same catalog/RLM helpers (F3) — no `domain.Relationships.FirstOrDefault` on product domain-bound paths.
- G4: W2.1 — StoragePass depends on EntityStructureAnalyzer; context-first EntityStructure. R14 (`ClassifyProperties` enum scan) fixed (F4) — DTLM-first when analysis present.
- G5: Exporter/EffectLowering improved; residual analysis-null scans documented as reduced-contract rows (R15–R22) and verified analysis-present fail-closed (F4).
- G6: MCP aggregates + subscriptionPlans from LatestAnalysis (quantifier now surfaced — F7).
- G7: 1855/1855 green + follow-up tests; pre-ship review re-run clean. Follow-ups: [`pipeline-followups-2026-08-06.md`](./pipeline-followups-2026-08-06.md) F1–F7, P1 closed.
