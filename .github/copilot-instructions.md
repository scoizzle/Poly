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

## DSL Guide Maintenance

**`Poly.Mcp/Docs/poly-dsl-agent-guide.md` must be updated whenever the DSL surface changes.**

This includes:
- Adding or removing a parser keyword or syntax construct
- Adding or removing a Phase 1a/1b effect type
- Changing constraint syntax (`range`, `length`, `pattern`, etc.)
- Changing relationship declaration syntax (N1 nav properties)
- Adding or removing lifecycle stage syntax (`entry`/`exit`, `when` subscriptions)
- Adding, removing, or renaming an MCP tool that authors DSL

Before merging any change that affects what `apply_dsl` accepts or what `export_dsl` emits,
verify the guide at `Poly.Mcp/Docs/poly-dsl-agent-guide.md` is still accurate.
The smoke test `GetDslGuide_ReturnsProductSurface` will catch drift, but the
maintainer must update the guide content proactively.

This ensures consistent behavior across all AI tools the maintainer uses (Copilot, OpenCode, Grok, etc.).

## Pre-Ship Review Gate

Before marking any task or slice as complete, you **must** execute the
**[uncommitted-change review gate](../../AGENTS.md#pre-ship-review-gate)** defined in AGENTS.md.

The process:
1. Run `git diff --stat HEAD` then `git diff HEAD` to audit dirty files.
2. Categorize findings by severity: 🔴 Structure, 🟠 Contract, 🟡 Edge case, ⚪ Hygiene.
3. For every 🔴/🟠 finding, verify **three-layer defense**: parse-time rejects, analyze-time catches, runtime fails loud.
4. **Fail-closed:** Empty sets, missing matches, invalid configs — fail loud, no vacuous success.
5. Apply the smallest fix, re-review, and only ship when the tree is clean and all 🔴🟠 are resolved.

Full task definition: [`docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../../docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)
