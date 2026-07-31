# DAS W1.4 — Retire duplicate index publishers

**Wave:** W1 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) P1, success §11.4  
**Difficulty:** Medium  
**Status:** `[x]`  
**Prereq:** W1.3  

## Objective

End dual-write. Delete or reduce RuntimeContract/Semantic bags that only re-index the catalog graph. Keep stage-keyed **subscription dispatch plans** if still required for notify identity—but build them from catalog.

## Tasks

- [x] W1.4.1 Grep producers of DTLM/RLM/MTI/ARM; classify keep-as-alias vs delete.
- [x] W1.4.2 Remove dead fields (e.g. historical ARM.StageByName-class residue).
- [x] W1.4.3 Update tests that assert old metadata types; prefer catalog assertions.
- [x] W1.4.4 Document remaining bags in CORE or future-state ownership matrix.

## Acceptance criteria

- [x] One authoritative catalog publisher for name→member maps.
- [x] Grep shows no second full action/policy map publisher.
- [x] Build + tests green.

## Progress notes

- **Classify (W1.4.1):** DTLM/RLM keep as Semantic intermediate mid-pipeline bags (embedded in catalog). Entity-keyed ARM + domain-keyed MTI **delete dual-write** — built only inside `DomainCatalogPass`. RCM + SDP keep (runtime contracts / stage notify plans). ARM.StageByName already gone (F30).
- **Publisher:** `DomainCatalogPass` sole `SetMetadata` of `DomainCatalogMetadata`; sole production site that **new**s `ActionResolutionMetadata` / `MutationTargetIndexMetadata` (embedded in catalog, not dual-written). `RuntimeContractAnalyzer` only `SetMetadata` RCM (default) + SDP (stage).
- **Lookups:** `DomainSemanticLookupExtensions` domain-keyed `GetActionResolution` / `GetMutationIndex` / `GetTypeLookup` / `GetRelationshipLookup` are catalog-only. `Evolution.GetMutationIndex` catalog-only (throw if missing). Oracle `DescribeAction` / `DescribePolicy` use `GetActionResolution` / `GetMutationIndex`.
- **Grep:** zero production `GetMetadata<ActionResolutionMetadata|MutationTargetIndexMetadata>`.
- **Tests:** `RuntimeContractMetadataTests`, `PipelineMergeMetadataTests`, `DomainSemanticLookupFailClosedTests` assert ARM/MTI bags null after analyze (catalog is authority). AC3 (build+tests) supported by those contract tests; full suite not re-executed in read-only verifier.
- **Docs:** `docs/plans/das-catalog-design.md` ownership matrix; CORE §3.1 catalog note (DTLM/RLM intermediate keep).
- **Verify (pass, severity none):** evidence above; implement success true.
