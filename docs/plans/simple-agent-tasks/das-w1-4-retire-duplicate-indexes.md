# DAS W1.4 — Retire duplicate index publishers

**Wave:** W1 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) P1, success §11.4  
**Difficulty:** Medium  
**Status:** `[ ]`  
**Prereq:** W1.3  

## Objective

End dual-write. Delete or reduce RuntimeContract/Semantic bags that only re-index the catalog graph. Keep stage-keyed **subscription dispatch plans** if still required for notify identity—but build them from catalog.

## Tasks

- [ ] W1.4.1 Grep producers of DTLM/RLM/MTI/ARM; classify keep-as-alias vs delete.
- [ ] W1.4.2 Remove dead fields (e.g. historical ARM.StageByName-class residue).
- [ ] W1.4.3 Update tests that assert old metadata types; prefer catalog assertions.
- [ ] W1.4.4 Document remaining bags in CORE or future-state ownership matrix.

## Acceptance criteria

- [ ] One authoritative catalog publisher for name→member maps.
- [ ] Grep shows no second full action/policy map publisher.
- [ ] Build + tests green.

## Progress notes

(empty)
