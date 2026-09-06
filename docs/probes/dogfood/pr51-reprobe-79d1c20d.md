# Dogfood reprobe — PR 51 create/create-in MCP (SHA 79d1c20d)

**SHA:** `79d1c20d1fcd39b6e8f3702dea80333d61a25c57` (Merge branch `master` into `cursor/pipeline-transformation-1a9d`)  
**Prior dogfood on this PR:** `0b6fcab`, `48a9222`, `feee0d29` (Fine orphan Y, Type+Rel skew Y); `dc03a193`, `574e941a` (Fine orphan N, Type+Rel N)  
**Ancestry:** `4d97319e` (PR52 Fine fix) and `574e941a` (Fine Type auto-link) **ARE** ancestors of this tip (merge parents: `574e941a` + `a9d15c48` / PR53). Expect Fine orphan N / Type+Rel N **unless regression**.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `/tmp/poly_mcp_pr51_reprobe_79d1c20d.py` (adapted from pr53/pr51 harness).  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-79d1c20d.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | **FAIL** — `VM compile rejected: no matching member for invoke with 1 argument(s)` |
| list_instances Fine | count=**0** (no Fine registered) |
| HasFines / HasFineCount | **false** |
| NoFines | **true** |
| get_instance Patron.navigationLinks | empty `[]` |
| created Fine | never created |

**Fine orphan: NO** — Fine was never registered (orphan verdict requires Fine registered but Rel-unlinked). Verdict blocked by compile failure on Type-create action.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | **FAIL** — same VM compile reject |
| list_instances Fine | count=**0** |
| HasFines / HasFineCount | **false** |
| NoFines | **true** |
| navigationLinks | empty |

**Auto-link: N/A** — create-in never ran.

### C — Mixed Type then Rel (same Patron)
| After | list Fine | HasFines | fines linked ids |
|-------|-----------|----------|------------------|
| AssessByType | 0 (invoke failed) | false | 0 |
| AssessByRel | 0 (invoke failed) | false | 0 |

**Type+Rel skew: NO** — `list_instances(Fine)` never reaches 2; skew verdict requires store=2 vs nav/policy=1. Both Assess actions fail before any Fine exists.

## Pain list
1. **Hard — create / create-in action compile reject (regression):** `invoke_action` AssessByType and AssessByRel both fail with `VM compile rejected: no matching member for invoke with 1 argument(s)` (SyntaxTypeCompatibilityAnalyzer). Same probe DSL + MCP path succeeded at parent SHAs `574e941a` (PR51 Fine Type auto-link) and `d658a34b` / PR53 tip (`a9d15c48`). Appears introduced by merge `79d1c20d` combining pipeline Create/CreateIn trees with master Interpretation coverage.

Soft: reverse `patron` nav alongside `fines` — **not observed** (no Fine created / no nav links).

Hard blocker for Fine orphan / Type+Rel product checks at this tip: Assess actions do not execute. No platform fixes in this pass. Probe DSL unchanged (reused `docs/probes/dogfood/simulate-create-*.poly`).
