# Dogfood reprobe — PR 51 create/create-in MCP (SHA f7e4b94c)

**SHA:** `f7e4b94ccab28f86e07025911d3d5423fa60abfd` (`fix(pr51): F9 BindCreate auto-link for unambiguous Type-create`)  
**Event:** pr-pushed on scoizzle/Poly #51 (NEW SHA — not previously probed)  
**Prior dogfood on this PR:** `0b6fcab`/`48a9222`/`feee0d29` (Fine orphan Y, Type+Rel Y); later tips N/N including `2a362ea4` (pre-F9 export fix). This tip closes F9 BindCreate auto-link for unambiguous Type-create.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `docs/probes/dogfood/poly_mcp_pr51_reprobe_f7e4b94c.py` (adapted from `/workspace/poly_mcp_pr51_reprobe_2a362ea4.py`).  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy(instanceId)` / `list_instances` / `get_instance`. No bag-mode evaluate.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-f7e4b94c.jsonl` (local harness run).

## Verdict
| Check | Result |
|-------|--------|
| Fine orphan | **N** |
| Type+Rel skew | **N** |
| Pain | **none** (soft reverse `patron` nav on Patron only) |

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | **OK** — returned Fine `caa850f6…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| Patron.createdChildCount | 1 |
| Patron.navigationLinks | `fines` → Fine `caa850f6…`; also reverse `patron` (target→source) → same Fine |

**Fine orphan: NO** — Type-create registers Fine and auto-links onto `Patron.fines`; policies agree with nav.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | **OK** — returned Fine `daa94f8a…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| Patron.createdChildCount | 1 |
| navigationLinks | `fines` + reverse `patron` same Fine |

**Auto-link: YES** — create-in registers and links; policies agree.

### C — Mixed Type then Rel (same Patron)
| After | list Fine | HasFines / HasFineCount | fines linked ids |
|-------|-----------|-------------------------|------------------|
| AssessByType | 1 | true / true | 1 (`fe3c707a…`) |
| AssessByRel | **2** | true / true | **2** (`fe3c707a…`, `147bdbf2…`) |

Patron after Rel: `createdChildCount=2`; `fines` nav has both Fine ids; reverse `patron` also lists both.

**Type+Rel skew: NO** — `list_instances(Fine)=2` matches linked `fines` count and policy-visible presence.

## Pain list
None for Fine orphan / Type+Rel.

**Soft only:** reverse `patron` nav still appears on Patron alongside `fines` (same soft reverse-patron note as prior dogfood tips). Not scored as Fine orphan.

No hard blocker. No platform fixes in this pass. Probe DSL unchanged.
