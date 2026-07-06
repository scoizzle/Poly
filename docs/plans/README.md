# Plans & Roadmaps

This directory contains execution-oriented planning documents — roadmaps, phase breakdowns, task trackers, and milestone tracking.

## Purpose

- Keep day-to-day "what still needs to be done" work visible and updatable.
- Separate execution tracking from architectural rationale (which lives in `docs/decisions/`).

## Current Plans

- [Interpretation System Issues](interpretation-system-issues.md) — Tracked issues from the 2026-07-05 Interpretation code review. **P0 sprint DONE ✅** ANA-001/003/004 complete; **1420/1420 tests green**. **Next focus:** INT-018 (finally/using), INT-019 (portable call sites), cross-engine parity (P2).
- [Interpretation System Resolution Plan](interpretation-system-resolution-plan.md) — **154 checkable tasks** (P0–P6); **46 done** — Phase 1 EH MVP + P2 harness + P3 hardening partial. Synced 2026-07-06.
- [Interpretation System Architecture Review](../interpretation-system-architecture-review.md) — **Living holistic review:** component map, pipeline, contradiction register, conceptual issues. Iterative — not a task list.
- [V2 → V3 Master Roadmap](v2-to-v3/master-roadmap.md) — Canonical coordination document for execution status and workstream ownership.
- [V2 → V3 Workstreams](v2-to-v3/workstreams/) — Detailed, execution-facing task breakdowns by workstream.

## Legacy / Redirects

- [Legacy V2 → V3 Roadmap Redirect](v2-to-v3-domain-modeling-port-roadmap.md) — Superseded pointer maintained for compatibility.

**Special support for smaller agents**:
- `v2-to-v3/simple-agent-tasks/` contains micro-tasks deliberately sized for smaller/lower-capability models.
- `v2-to-v3/agent-summaries/` is where agents submit structured reports of their work. Orchestrators use these to maintain the plan without direct edits from executors.
- See `v2-to-v3/simple-agent-tasks/README.md` and the `orchestration-guide.md` for details.

## Guidelines

- Before starting significant work on any item, review the relevant decision(s) in `docs/decisions/`.
- Update this tracker as work progresses (status, new tasks, blockers).
- When a major design choice is made while executing a plan, create or update the corresponding decision record.