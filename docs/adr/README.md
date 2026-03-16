# Architecture Decision Records (ADR)

This folder stores short, immutable records of architecture decisions.

## Goals
- Capture why a decision was made.
- Record alternatives considered.
- Make trade-offs explicit for future contributors.

## Rules
- ADRs are append-only. Do not rewrite history; supersede with a new ADR.
- Keep each ADR focused on one decision.
- Include concrete impact on code, tests, and operations.

## Naming
- Use `ADR-###-short-title.md`.
- Start with `ADR-001-...` for the first accepted decision.

## Required Sections
1. Status
2. Context
3. Decision
4. Consequences
5. Alternatives Considered
6. Validation

## Lifecycle Status
- Proposed
- Accepted
- Superseded
- Deprecated

## Example Workflow
1. Copy `docs/adr/ADR-000-template.md`.
2. Fill in all required sections.
3. Link the ADR in the related PR.
4. If changed later, create a new ADR that supersedes the old one.
