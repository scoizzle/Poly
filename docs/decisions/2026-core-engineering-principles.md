# ADR: Core Engineering Principles for Poly

**Date:** 2026-05-31  
**Updated:** 2026-07-11 — seven principles; **Go well to go fast** added; AGENTS carries **Rule + How** for each; **order** = domain → E2E ownership → customer → test loops → thin slice → abstract late → guardrails; conflict-priority sentence in AGENTS  
**Updated:** 2026-09-28 — retire dogmatic “extract only on a second real use.” **Seam when multiplicity is known** replaces **Working code before abstractions**. Thin slice (§5) must hit the *right* seam — not a known-wrong single-path shape. Cautionary tale: DEI (`DomainEntityInstance`) as “working” public path; libraries / artifact producers / `uses` doors already proved multi-impl. Conflict rule in AGENTS updated accordingly.  
**Status:** Accepted  
**Deciders:** Primary author (with review)

## Context

Over time, a set of strong, recurring principles emerged for how work should be conducted in this repository. These principles were originally captured as a dense paragraph in `AGENTS.md`. As the project grew (particularly with the major V2 → V3 domain modeling shift), it became clear that these ideas deserved explicit treatment as foundational decisions rather than just inline instructions.

A further gap: “ship fast” without a feedback discipline produces the opposite of speed (rework, ABI hacks, parallel evaluators, speculative frameworks). The TDD / Clean Code dynamic—“the only way to go fast is to go well,” with tests growing more specific and production code more generic—needed an explicit seat in the principles set.

A later correction (2026-09-28): “working code before abstractions / wait for a second real use” was read as a license to ship single-path host shapes (notably DEI) when multiplicity was already certain. Libraries, artifact producers, and `uses` doors proved the multi-impl seam; pretend-one-impl “working” code became the wrong public path. Speculative frameworks for *imagined* futures remain out; known multiplicity gets a named seam now.

One-line principles alone proved insufficient for smaller-context agents and earlier-career humans: they need a short **how** (ordered steps) without opening this ADR. As of 2026-07-11, `AGENTS.md` therefore holds the enforceable summary; this file remains rationale and history. After the 2026-09 lean of AGENTS, the always-on form is **Core tenets** (one-liners + conflict rule); depth stays here and in CORE.

## Decision

The principles below are the authoritative foundation for the Poly workspace. The **enforceable operational version** (rule + short procedure) lives in `AGENTS.md` under Core principles, because that file is injected into agent context.

This document preserves deeper history and cross-links. Prefer not to duplicate full “How” lists here — update AGENTS first, then note material changes here.

### Authoritative operational version

See **`AGENTS.md` → Core tenets** for the scannable always-on summary (first section): seven how-we-work lines, intentional order, and the conflict rule. Tenets first is what agents are expected to load.

**Order rationale (Poly-specific):** domain first (this platform’s center of gravity); end-to-end ownership next (CORE pipeline); customer/scope filter; then motion (test→code loops), amount (thin slice through the right seam), structure (**seam when multiplicity is known** — not speculative frameworks), and process last (guardrails as servant, not master).

### Principle: Go well to go fast (extra detail)

Uncle Bob’s line *“The only way to go fast is to go well”* is policy, not slogan. Speed is quality under feedback.

- **Asymmetry over time:** tests become **more specific** (pin behavior); production code becomes **more generic** (special cases collapse under those pins).
- **Not** coverage theater or “never spike.” Spikes for learning are fine; they do not ship until pulled through the test→code loop in AGENTS.
- Complements **shipped capability over completeness** and **seam when multiplicity is known**.

**Violations:** large production changes with no new/tightened check; special-casing production to silence a vague test; parallel product paths without a failing case that forced a pipeline-native solution.

**Satisfies:** red → green → refactor (or characterization tests on legacy); thin vertical slices grown one pinned behavior at a time.

## Rationale

These principles repeatedly proved their value during the two independent cost-benefit analyses of the domain modeling rewrite and during day-to-day development. They provide a consistent filter that prevents both over-engineering and under-engineering.

We deliberately keep the short, enforceable version in `AGENTS.md` (rather than only here) because:
- AGENTS.md is automatically loaded/injected into context by the tools the maintainer actually uses.
- Decision files in `docs/decisions/` do not have the same guaranteed visibility as AGENTS.md.

This document exists to preserve the deeper history and rationale without diluting the signal in AGENTS.md.

## Consequences

- The authoritative short version of the principles lives in `AGENTS.md`.
- This decision file provides the "why," historical context, and examples.
- New proposals, guardrails, or major changes should be explicitly evaluated against the full set (referencing this document for depth when needed).
- When updating the short version in AGENTS.md, update this record for the historical trail.
- Agents and humans should prefer **small test→code loops** over multi-day untested implementation trains; review can ask “what got more specific in tests, and what got more generic in production?”

## Relationship to Other Documents

- `AGENTS.md` (Core principles) — Short, enforceable version.
- This decision file — Full rationale and history.
- `docs/CORE.md` — Platform machinery (how to extend); not a substitute for this feedback discipline.
- `docs/decisions/2026-05-31-immutable-core-domain-modeling.md` — Major application of the earlier principles.
- Other decision records should reference these principles when relevant.
