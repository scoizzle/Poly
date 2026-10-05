# e2e-g0-3 — 09/13 probes are gate fixtures

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** g0-2  
**Fleet:** P0-0c  

## Objective

Document and wire these as the full-solution fixtures (they currently generate broken Program/DbContext — that is the regression proof):

- `probes/fleet-eval/09-transport/{warehouse,orders,clinic}.poly`
- `probes/fleet-eval/13-packs/{warehouse,booking,library}.poly`

## Exact steps

1. Make `run-probe.sh` (or discovery-round) include those paths in the default/full sweep.
2. Confirm they fail 0/0 full-solution **now**. Paste the fail into `e2e-g0-gate.md` notes or a one-line comment in the script.
3. Do not fix generators.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `scripts/run-probe.sh` / `discovery-round.sh` | `src/Poly.DslCompiler/**` |
| probe README only if one exists | `.poly` domain meaning |

## Status

**Status:** Not Started  
**Claimed by:**  
