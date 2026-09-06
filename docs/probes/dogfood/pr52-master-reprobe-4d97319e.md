# Dogfood reprobe — PR 52 merge on master (SHA 4d97319e)

**SHA:** `4d97319e20cafb32e60e0143f648395237da1459` (Merge pull request #52 from scoizzle/cleanup/fine-orphan-type-rel)  
**Prior dogfood:** PR51 reprobe @ `48a9222` (Fine orphan YES, Type+Rel skew YES)  
**Harness:** MCP stdio → `Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA.  
**Path:** `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths (live fixtures; enrolled by PR 52)
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr52-master-reprobe-4d97319e.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, unambiguous many-rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| get_instance Patron.navigationLinks | `fines` + reverse `patron` linked |

**Fine orphan: NO** — Type-create auto-links; policies agree with list/nav.

### B — AssessByRel (`create in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **true** |
| NoFines | **false** |
| navigationLinks | `fines` + reverse `patron` |

**Auto-link: YES** (unchanged healthy path).

### C — Mixed Type then Rel (same Patron)
| After | list Fine | HasFines | fines linked ids |
|-------|-----------|----------|------------------|
| AssessByType | 1 | true | 1 (Type Fine) |
| AssessByRel | **2** | true | **2** (Type + Rel) |

**Type+Rel skew: NO** — list Fine=2 matches policy-visible / nav-linked fines=2.

## Verdicts vs PR51
| Seam | PR51 @ 48a9222 | Master @ 4d97319e |
|------|----------------|-------------------|
| Fine orphan after Type-create | YES | **NO (closed)** |
| Type+Rel skew (list=2 vs policy-visible=1) | YES | **NO (closed)** |

**PR 52 closed both seams** for unambiguous many-rel Type-create.

## Pain list
**none** for these three probes on master.

Soft (not a seam failure): reverse `patron` nav appears alongside `fines` on Patron after link (same observation as PR51; informational).

No hard blocker. No platform fixes in this pass.
