# Dogfood reprobe — PR 51 create/create-in MCP (SHA dc03a193)

**SHA:** `dc03a1936fe4c693416d5ff9a64bea22dfa3a5fb` (CRM Capture fail-closed + Type-create auto-link)  
**Prior dogfood on this PR:** `0b6fcab`, `48a9222`, `feee0d29` (all Fine orphan Y, Type+Rel skew Y)  
**Master post-PR-52 (`4d97319e`):** Fine orphan N, Type+Rel skew N  
**Ancestry note:** this SHA contains merge of `origin/master` (includes PR52 `4d97319e`) plus `5633940b` Store Type-create auto-link — so Y findings from pre-rebase SHAs are expected closed here.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA.  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-dc03a193.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| get_instance Patron.navigationLinks | `fines` auto-linked + reverse `patron` |
| createdChildCount | 1 |

**Fine orphan: NO** — store Fine is Rel-linked; policies agree.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| navigationLinks | `fines` auto-linked + reverse `patron` |

**Auto-link: YES.** Policies agree with linked child.

### C — Mixed Type then Rel (same Patron)
| After | list Fine | HasFines | fines linked ids |
|-------|-----------|----------|------------------|
| AssessByType | 1 | true | 1 (Type Fine) |
| AssessByRel | **2** | true | **2** (Type + Rel) |

**Type+Rel skew: NO** — `list_instances(Fine)=2` matches policy-visible / nav-linked fines=2. `createdChildCount=2`.

## Pain list
**none** (Fine orphan and Type+Rel skew closed on this SHA vs prior PR51 dogfood).

Soft: after Type or create-in, reverse `patron` link appears alongside `fines` on Patron (unchanged soft note).

No hard blocker. No platform fixes in this pass. Probe DSL unchanged (reused `docs/probes/dogfood/simulate-create-*.poly`).
