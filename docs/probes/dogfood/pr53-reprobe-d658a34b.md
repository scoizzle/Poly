# Dogfood reprobe — PR 53 create/create-in MCP (SHA d658a34b)

**SHA:** `d658a34b1f6e212c76fac49571fa6e783574e4df` (PR53 tip merges master; Interpretation TUnit coverage stack)  
**PR:** https://github.com/scoizzle/Poly/pull/53  
**Ancestry:** `4d97319e` (PR52 Fine auto-link) **IS** an ancestor — unlike prior tip `f25604fe` which opened off older master without Fine Type auto-link.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `/tmp/poly_mcp_pr53_reprobe_d658a34b.py` (adapted from f25604fe / pr51 shape).  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr53-reprobe-d658a34b.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | Success; returns Fine id (message); store registers Fine |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| get_instance Patron.navigationLinks | `fines` + reverse `patron` linked to Type Fine |
| created Fine | registered **and** Rel-linked |

**Fine orphan: NO** — Type-create Fine is auto-linked; policies/nav agree with store.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| navigationLinks | `fines` + reverse `patron` linked |

**Auto-link: YES** for create-in. Policies agree with linked child.

### C — Mixed Type then Rel (same Patron)
| After | list Fine | HasFines | fines linked ids |
|-------|-----------|----------|------------------|
| AssessByType | 1 | true | 1 (Type Fine) |
| AssessByRel | **2** | true | **2** (Type + Rel) |

**Type+Rel skew: NO** — `list_instances(Fine)=2` and nav/policy both see both Fines.

## Pain list
none

Soft: reverse `patron` appears alongside `fines` on Patron after Type-create and create-in (same soft note as prior dogfood). Harness `returnInstanceId` helper still null under current MCP envelope shape (`invokeActionResult`); Fine ids visible in action message / list / nav.

No hard blocker. No platform fixes in this pass. Contrast: prior tip `f25604fe` had Fine orphan Y / Type+Rel Y because PR52 Fine auto-link was not an ancestor; this tip includes `4d97319e`.
