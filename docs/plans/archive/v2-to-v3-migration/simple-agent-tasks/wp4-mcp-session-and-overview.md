# Micro-Task: MCP session store + overview on V3

**Parent**: WP4  
**Difficulty**: Medium  
**Estimated Tokens**: ~10k  
**Status**: [x] **Done** — structured results (DomainOverviewData, EntityDetailData, AnalysisData); affordances on success/failure; enriched diagnostics pass-through; list_sessions tool added; smoke tests pass

## Objective

MCP **workspace/session** + overview tools as a **consumer** of the DomainModeling API (workspace is an MCP concept, not core).

## Context

- `spikes/mcp-guiding-principles.md`
- `spikes/first-v3-consumer.md`
- `v3-completion-plan.md` §1.2 (workspace in MCP only)
- WP1–WP2 complete (factory + queries)
- Code present: `Poly.Mcp/Sessions/McpSessionStore.cs`, `Poly.Mcp/Tools/V3DomainTools.cs`, `Program.cs` registers V3 tools only

## Exact Steps (original — largely done)

1. Session/workspace store in Poly.Mcp: `sessionId` → V3 `Domain` + revision + last analysis (no V2 Domain). **Do not** put this type in DomainModeling.
2. Tools: CreateDomainSession, GetDomainOverview (and optional ListSessions / Interrogate).
3. Bootstrap via DomainModeling factory/builtins.
4. Descriptions per MCP principles; concise response envelope + affordances.
5. Call DomainModeling evolve/query APIs only — no domain logic in tools.
6. Tests may live under Poly.Tests and reference Poly.Mcp public session types.

## Code-review follow-ups (do these before marking Done)

1. **Structured results** — do not put overview/entity detail **only** in free-text `Message`. Return structured data agents/UI can bind (JSON fields or companion payload) while keeping a short message.
2. **Affordances** — on failure (and optionally success), return suggested next tools/args (even minimal), per MCP principles and old DomainTools pattern.
3. **Diagnostics** — pass through capped analysis diagnostic messages, not only `FailureSummary`.
4. **Smoke tests** — create session → overview (and get entity after evolve) via tool methods; use `InternalsVisibleTo` or make a thin public test surface if types stay `internal`.
5. **Optional:** `list_sessions` tool.

## Verification

- [x] Build Poly.Mcp; V3 tools registered (as of review)
- [x] No `Poly.Data.Modeling` on V3 path
- [ ] Structured overview/entity responses
- [ ] Affordances + richer diagnostics
- [ ] At least one MCP smoke test

## Out of Scope

- Full evolve tool set (see `wp4-mcp-evolve-tools`); export/import; evaluate
