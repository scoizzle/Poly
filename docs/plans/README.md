# Plans & Roadmaps

This directory contains execution-oriented planning documents — roadmaps, phase breakdowns, task trackers, and milestone tracking.

## Purpose

- Keep day-to-day "what still needs to be done" work visible and updatable.
- Separate execution tracking from architectural rationale (which lives in `docs/decisions/`).

## Current Plans

- [V2 → V3 Domain Modeling Port Roadmap & Task Tracker](v2-to-v3-domain-modeling-port-roadmap.md) — Main tracker for the immutable core + evolution layer migration.

**Special support for smaller agents**:
- `v2-to-v3/simple-agent-tasks/` contains micro-tasks deliberately sized for smaller/lower-capability models.
- `v2-to-v3/agent-summaries/` is where agents submit structured reports of their work. Orchestrators use these to maintain the plan without direct edits from executors.
- See `v2-to-v3/simple-agent-tasks/README.md` and the `orchestration-guide.md` for details.

## Guidelines

- Before starting significant work on any item, review the relevant decision(s) in `docs/decisions/`.
- Update this tracker as work progresses (status, new tasks, blockers).
- When a major design choice is made while executing a plan, create or update the corresponding decision record.