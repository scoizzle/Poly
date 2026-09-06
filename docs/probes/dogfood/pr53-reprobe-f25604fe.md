# Dogfood reprobe — PR 53 create/create-in MCP (SHA f25604fe)

**SHA:** `f25604fe333be8cdfeff8397b7d4ca1b2079e1b3` (F25 IsNumericWidening gate; Interpretation coverage stack)  
**PR:** https://github.com/scoizzle/Poly/pull/53  
**Ancestry:** `origin/master` (post-PR52 Fine fix `4d97319e` / `d07aabf3`) is **not** an ancestor — PR53 opened off older master. Fine Type auto-link from PR51/52 is absent here.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA.  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr53-reprobe-f25604fe.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **false** |
| NoFines | **true** |
| get_instance Patron.navigationLinks | **[]** (createdChildCount=1) |
| created Fine | registered, **not** Rel-linked |

**Fine orphan: YES** — store Fine exists; policies/nav treat Patron as having no fines.

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
| AssessByType | 1 | false | 0 |
| AssessByRel | **2** | true | **1** (Rel Fine only) |

**Type+Rel skew: YES** — `list_instances(Fine)=2` vs policy-visible / nav-linked fines=1.

## Pain list
1. **Fine orphan (Type-create)** — `docs/probes/dogfood/simulate-create-type.poly` — bare `create Fine` registers child, leaves `navigationLinks` empty; HasFines/HasFineCount false.
2. **Type+Rel skew** — `docs/probes/dogfood/simulate-create-create-in.poly` — after Type then create-in, list Fine=2 but nav/policy only see the Rel Fine.

Soft: reverse `patron` appears alongside `fines` on Patron after create-in (same soft note as prior dogfood).

No hard blocker. No platform fixes in this pass. Contrast: master `@4d97319e` / PR51 `@574e941a` had Fine orphan N / Type+Rel N after Fine auto-link landed — this PR tip predates that merge.
