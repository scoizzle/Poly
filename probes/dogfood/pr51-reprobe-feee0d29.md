# Dogfood reprobe — PR 51 create/create-in MCP (SHA feee0d29)

**SHA:** `feee0d2971c69e7146fbb5e90cb5f57b11ca6731` (after F1 adapter fail-closed)  
**Prior dogfood:** `48a9222`  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA.  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy` / `list_instances` / `get_instance`.

## Probe paths
- `probes/dogfood/simulate-create-type.poly`
- `probes/dogfood/simulate-create-in.poly`
- `probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-feee0d29.jsonl` (local mill run).

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | Success; returns Fine id |
| list_instances Fine | count=1 |
| HasFines / HasFineCount | **false** |
| NoFines | **true** |
| get_instance Patron.navigationLinks | **[]** |
| createdChildCount | 1 |

**Fine orphan: YES** — store has Fine; Rel/policies do not.

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
| AssessByType | 1 | false | (none) |
| AssessByRel | **2** | true | **1** (create-in only) |

**Type+Rel skew: YES** — `list_instances(Fine)=2` vs policy-visible / nav-linked fines=1. `createdChildCount=2`.

## Pain list (unchanged vs 48a9222 / 0b6fcab)
1. **Type-create orphans Fine:** store + `createdChildCount` increment; `fines exists` / `count fines` stay false; `navigationLinks` empty.
2. **Mixed Type+Rel skew:** list Fine=2 while policy-visible / linked fines=1.
3. Soft: after create-in, reverse `patron` link appears alongside `fines` on Patron.

No hard blocker. No platform fixes in this pass. Probe DSL unchanged. F1 adapter simulate fail-closed did not change create/create-in orphan seams.
