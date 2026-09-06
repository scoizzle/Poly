# Dogfood reprobe — PR 51 create/create-in MCP (SHA 2a362ea4)

**SHA:** `2a362ea45bd8d91f09d806d3c3f5793f7cd19037` (`merge: master into pipeline-transformation (PR 54/56)`)  
**Prior dogfood on this PR:** `0b6fcab`/`48a9222`/`feee0d29` (Fine orphan Y, Type+Rel Y); `dc03a193`/`574e941a`/`8b995997`/`79d1c20d`/`edd1b8a9` (N/N)  
**Ancestry:** tip merges master (PR 54/56); `8b995997` and `574e941a` ARE ancestors.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `/tmp/poly_mcp_pr51_reprobe_2a362ea4.py`.  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-2a362ea4.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | **OK** — returned Fine `73688736…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| Patron.navigationLinks | `fines` → Fine; also reverse `patron` (target→source) → same Fine |

**Fine orphan: NO** — Type-create registers Fine and auto-links onto `Patron.fines`; policies agree.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | **OK** — returned Fine `6dfa471c…` |
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
None for Fine orphan / Type+Rel. Soft only: reverse `patron` nav appears on Patron alongside `fines` (same soft reverse patron nav as prior tips). Assess compile reject remains CLOSED.

No platform fixes in this pass. Probe DSL unchanged (reused `docs/probes/dogfood/simulate-create-*.poly`).
