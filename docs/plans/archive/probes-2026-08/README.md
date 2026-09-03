# Archived: discovery / fleet-eval probes (2026-08)

**Archived:** 2026-09-01  
**From:** repo-root `probes/`  
**Do not treat as live fixtures.** Findings in `findings/` are historical snapshots.

## Live probes

Test oracles and smoke live under [`docs/probes/`](../../../probes/README.md):

- `docs/probes/dogfood/*.poly`
- `docs/probes/fleet-eval/09-transport/*.poly`
- `docs/probes/fleet-eval/12-mcp/mcp-library.poly`
- `docs/probes/smoke/smoke.poly`

New discovery rounds: `scripts/new-probe.sh` writes under `docs/probes/<name>/`. Findings: `docs/probes/findings/<round>/`.

## What is here

Agent discovery rounds (`agent-a` … `round5-*`), fleet-eval slices (except the live oracles above), and `findings/` write-ups.
