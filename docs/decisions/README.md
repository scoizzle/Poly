# Architectural Decisions

This directory contains high-level, cross-cutting architectural decisions that affect large parts of the Poly system.

## Purpose

These documents exist so that:

- Humans and AI agents working on the codebase can quickly understand major directional choices.
- Future work respects previously agreed constraints (especially around the domain model).
- We avoid repeatedly re-litigating the same big decisions.

## Guidelines

- Only major, cross-cutting decisions should live here.
- Detailed or module-specific decisions should remain co-located with the code they govern (e.g. inside `Poly/DomainModeling/`).
- Keep entries concise and scannable. Link to more detailed records where they exist.
- Update `AGENTS.md` when adding new decisions that agents must be aware of.

**Note on Plans vs Decisions**: Execution roadmaps, phase breakdowns, and task tracking now live in `docs/plans/`. See `docs/plans/v2-to-v3-domain-modeling-port-roadmap.md` for the current V2→V3 port tracker.

## Current Decisions

- [2026-05-31: Immutable Core for Domain Modeling (V2 → V3)](2026-05-31-immutable-core-domain-modeling.md) — Strategic shift to immutable records while preserving the transactional evolution/correctness guarantees required by LLM agents.
- [2026: V2 → V3 Domain Modeling Port Plan](2026-v2-to-v3-domain-modeling-port.md) — Living plan for the port to the immutable core + thin evolution layer (includes integration with the documentation and agent instruction structure).
- [2026-05-31: Neurosymbolic Platform Vision](2026-05-31-neurosymbolic-platform-vision.md) — Architectural vision for Poly as a neurosymbolic platform: models codify discovered algorithms as composable macros in a symbolic IR, validated by a tree-walker interpreter, compiled to native backends. Reframes domain modeling as compiler frontend for program synthesis.