# Dogfood reprobe — PR 51 create/create-in MCP (SHA edd1b8a9)

**SHA:** `edd1b8a9c9d205ab02681dea21e8f74aed8c01c6` (`fix: bind DomainResult Success/Failure for PR53 arity check`)  
**Prior dogfood on this PR:** `0b6fcab`/`48a9222`/`feee0d29` (Fine orphan Y, Type+Rel Y); `dc03a193`/`574e941a` (N/N); `79d1c20d` (N/N but hard Assess compile reject)  
**Ancestry:** parent is `79d1c20d`; `4d97319e` (PR52 Fine) and `574e941a` ARE ancestors. Tip claims to fix PR53 arity / DomainResult Success/Failure bind.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `/workspace/poly_mcp_pr51_reprobe_edd1b8a9.py` (adapted from 79d1c20d harness).  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-edd1b8a9.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | **OK** — returned Fine `cb6b490a…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| Patron.navigationLinks | `fines` → Fine; also reverse `patron` (target→source) → same Fine |

**Fine orphan: NO** — Type-create registers Fine and auto-links onto `Patron.fines`; policies agree.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | **OK** — returned Fine `07a9ad51…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| navigationLinks | `fines` + reverse `patron` same as A |

**Auto-link: YES** — create-in registers and links.

### C — Mixed Type then Rel (same Patron)
| After | list Fine | HasFines | fines linked ids |
|-------|-----------|----------|------------------|
| AssessByType | 1 | true | 1 |
| AssessByRel | 2 | true | 2 |

**Type+Rel skew: NO** — `list_instances(Fine)=2` matches `fines` nav (2 ids); policies stay true. No store=2 vs nav/policy=1 skew.

## Pain list
1. **Hard — Assess compile reject (79d1c20d): CLOSED** at `edd1b8a9`. AssessByType / AssessByRel both succeed (vs parent tip VM compile rejected: no matching member for invoke with 1 argument(s)).

Soft only: reverse `patron` nav appears on Patron alongside `fines` (same soft reverse patron nav as `574e941a` / `dc03a193` / PR53 tip). Not a Fine orphan / Type+Rel failure.

No platform fixes in this pass. Probe DSL unchanged (reused `docs/probes/dogfood/simulate-create-*.poly`).
