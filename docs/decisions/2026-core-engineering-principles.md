# ADR: Core Engineering Principles for Poly

**Date:** 2026-05-31  
**Status:** Accepted  
**Deciders:** Primary author (with review)

## Context

Over time, a set of strong, recurring principles emerged for how work should be conducted in this repository. These principles were originally captured as a dense paragraph in `AGENTS.md`. As the project grew (particularly with the major V2 → V3 domain modeling shift), it became clear that these ideas deserved explicit treatment as foundational decisions rather than just inline instructions.

## Decision

The six principles below are the authoritative foundation for the Poly workspace. The concise, enforceable version lives in `AGENTS.md` (under the "Core Principles" section), because that file is directly injected into agent context by the tools the maintainer uses (OpenCode, Copilot via our instructions file, etc.).

The full expanded rationale, historical context, and examples of application are documented here.

### The Six Principles (authoritative short version)

See `AGENTS.md` → Core Principles for the current wording. These are the statements that agents are expected to follow on every relevant task.

## Rationale

These principles repeatedly proved their value during the two independent cost-benefit analyses of the domain modeling rewrite and during day-to-day development. They provide a consistent filter that prevents both over-engineering and under-engineering.

We deliberately keep the short, enforceable version in `AGENTS.md` (rather than only here) because:
- AGENTS.md is automatically loaded/injected into context by the tools the maintainer actually uses (OpenCode treats it as first-class, Copilot is directed to it via our instructions file, etc.).
- "Not all models are created equally" — decision files in `docs/decisions/` do not have the same guaranteed visibility as AGENTS.md.

This document exists to preserve the deeper history and rationale without diluting the signal in AGENTS.md.

## Consequences

- The authoritative short version of the six principles lives in `AGENTS.md`.
- This decision file provides the "why," historical context, and examples.
- New proposals, guardrails, or major changes should be explicitly evaluated against the six principles (referencing this document for depth when needed).
- When updating the short version in AGENTS.md, consider whether the change also warrants an update here for the historical record.

## Relationship to Other Documents

- `AGENTS.md` (Core Principles section) — Contains the short, enforceable version that agents are expected to follow. This is the primary document for day-to-day use.
- This decision file — Provides the full rationale, history, and context.
- `docs/decisions/2026-05-31-immutable-core-domain-modeling.md` — A major real-world application of these principles.
- Other decision records should reference these principles when relevant.