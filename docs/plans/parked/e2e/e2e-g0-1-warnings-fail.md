# e2e-g0-1 — probe-check fails on warnings

**Difficulty:** S  
**Status:** `[ ]`  
**Fleet:** P0-0a  

## Objective

`scripts/probe-check` exit 1 if any warning. A warning-bearing export must fail the gate.

## Exact steps

1. Change `scripts/probe-check/Program.cs` so exit is 0 only when `errors == 0 && warnings == 0`.
2. Add a tiny fixture or existing generated snippet that produces a warning; assert probe-check exits 1. Prefer a checked-in test under `Poly.Tests` that shells or invokes the same logic — do not require a new framework.
3. Keep the `errors: N, warnings: M` line so `discovery-round.sh` can be updated in g0-2.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `scripts/probe-check/Program.cs` | DomainModeling |
| a test or fixture proving warning → exit 1 | `run-probe.sh` (task 2) |

## Status

**Status:** Not Started  
**Claimed by:**  
