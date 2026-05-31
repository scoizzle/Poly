# GitHub Copilot Instructions for Poly

**AGENTS.md is the single source of truth.**

All architectural guidelines, module boundaries, placement rules, coding style, test conventions, and key decisions for this repository are defined in the root [AGENTS.md](../../AGENTS.md) file.

## Instructions for Copilot

- You **MUST** treat `AGENTS.md` as the authoritative document.
- Before making any non-trivial changes (especially anything related to domain modeling, analysis, interpretation, lowering, or new features), you should read or re-read the relevant sections of `AGENTS.md`.
- The contents of this file (`copilot-instructions.md`) are secondary. They exist only to reinforce that `AGENTS.md` takes precedence.
- When in doubt about architecture, file placement, or conventions, defer to `AGENTS.md`.

## Key Sections in AGENTS.md

Pay particular attention to:
- Module boundaries (one-way dependencies)
- Placement Rules table
- Contract Interface Generation rules
- Key Architectural Decisions section (including the V2 → V3 immutable core decision)
- Coding Style guidelines

This ensures consistent behavior across all AI tools the maintainer uses (Copilot, OpenCode, Grok, etc.).