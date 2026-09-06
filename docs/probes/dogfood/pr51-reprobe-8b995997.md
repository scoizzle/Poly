# Dogfood reprobe — PR 51 create/create-in MCP (SHA 8b995997)

**SHA:** `8b9959977be1c8e5c439452d83c65a29e9660f92` (`Merge remote-tracking branch 'origin/cursor/pipeline-transformation-1a9d' into cursor/pipeline-transformation-1a9d`)  
**Prior dogfood on this PR:** `0b6fcab`/`48a9222`/`feee0d29` (Fine orphan Y, Type+Rel Y); `dc03a193`/`574e941a` (N/N); `79d1c20d` (N/N + hard Assess compile reject); `edd1b8a9` (N/N, Assess reject CLOSED)  
**Ancestry:** tip includes `edd1b8a9` + `55b5a588` (CLR object assignable from modeled AST); `4d97319e` (PR52 Fine) and `574e941a` ARE ancestors.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `/workspace/poly_mcp_pr51_reprobe_8b995997.py`.  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-8b995997.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | **OK** — returned Fine `ce615af6…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| Patron.navigationLinks | `fines` → Fine; also reverse `patron` (target→source) → same Fine |

**Fine orphan: NO** — Type-create registers Fine and auto-links onto `Patron.fines`; policies agree.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | **OK** — returned Fine `ce48365d…` |
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

**Type+Rel skew: NO** — `list_instances(Fine)=2` matches `fines` nav (2 ids); policies stay true.

## Pain list
None for Fine orphan / Type+Rel. Soft only: reverse `patron` nav appears on Patron alongside `fines` (same soft reverse patron nav as `edd1b8a9` / `574e941a` / PR53 tip). Assess compile reject remains CLOSED.

No platform fixes in this pass. Probe DSL unchanged (reused `docs/probes/dogfood/simulate-create-*.poly`).
