# Dogfood reprobe — PR 51 create/create-in MCP (SHA 89935a56)

**SHA:** `89935a56384de46a8504f6d6518563ac85b83394` (`fix(pr51): F10 BindCreate reverse this via FindAutoWireBackReference`)  
**Event:** pr-pushed on scoizzle/Poly #51 (NEW SHA — not previously probed)  
**Prior tip:** `f7e4b94c` (F9 BindCreate auto-link; Fine orphan N, Type+Rel N; soft reverse patron nav only). This tip adds F10 reverse `this` via FindAutoWireBackReference.  
**Harness:** MCP stdio → `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll` rebuilt at this SHA. Runner: `docs/probes/dogfood/poly_mcp_pr51_reprobe_89935a56.py`.  
**tools/list:** no `simulate_*` tools; path is `create_domain_session` → `apply_dsl` → `create_instance` → `invoke_action` / `evaluate_policy(instanceId)` / `list_instances` / `get_instance`. No bag-mode evaluate.

## Probe paths
- `docs/probes/dogfood/simulate-create-type.poly`
- `docs/probes/dogfood/simulate-create-in.poly`
- `docs/probes/dogfood/simulate-create-create-in.poly`

Raw envelopes: `/tmp/poly-dogfood-pr51-reprobe-89935a56.jsonl` (local harness run).

## Verdict
| Check | Result |
|-------|--------|
| Fine orphan | **N** |
| Type+Rel skew | **N** |
| Pain | **none** |

## Results

### A — AssessByType (`create Fine`, not in rel)
| Check | Result |
|-------|--------|
| invoke AssessByType | **OK** — returned Fine `59e6f979…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| Patron nav | `fines` source→target + `patron` target→source both link Fine |
| Fine nav (spot-check) | `patron` source→target + `fines` target→source both link Patron |

### B — AssessByRel (`create Fine in fines`)
| Check | Result |
|-------|--------|
| invoke AssessByRel | **OK** — returned Fine `ab9932c8…` |
| list_instances Fine | count=**1** |
| HasFines / HasFineCount | **true** |
| Patron nav | same dual wire as A |

### C — Type then create-in (same Patron)
| Check | Result |
|-------|--------|
| after Type: HasFines + Fine list | **true**, count=**1** |
| after Rel: HasFines + Fine list | **true**, count=**2** |
| Patron nav | both Fine ids on `fines` and reverse `patron` |

## Notes
F10 closes the prior soft reverse-`patron` gap: Type-create Fine now wires Fine→Patron (`patron` forward) and Patron←Fine (`patron` reverse / `fines` reverse) the same way create-in already did. No product pain on this tip.
