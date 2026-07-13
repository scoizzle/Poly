# Plan: Post–V2-Delete Naming Cleanup (drop “V3” / “V2” product labels)

**Status:** Done — R0–R2 complete. All product `V3*` type names eliminated.  
**Date:** 2026-07-13  
**Why:** V2 (`Poly.Data.Modeling`) is **gone**. “V3” was a **migration version** relative to V2. Keeping `V3*` type names, `UseV3*`, and “V3 MCP” prose teaches agents and humans a second system that no longer exists. The product surface is simply **DomainModeling + MCP**.

**Entry for simple agents when this plan is opened:** this file + micro-tasks under `docs/plans/v2-to-v3/simple-agent-tasks/rn-*` (to be added when execution starts).  
**Not the daily product queue:** continue `vs-README.md` for vertical slices until M2.

---

## 1. Principle

| Keep | Drop / rename |
|------|----------------|
| **Historical** docs, ADRs, archived plans that describe the V2→V3 *migration* | Product **code** and **active** docs that imply two stacks |
| `docs/plans/v2-to-v3/` path as **historical archive label** (optional later move) | `V3Foo` identifiers in runtime code |
| Mentions of V2 only as “deleted / do not reintroduce” | “V3 domain model” when meaning “the domain model” |

**Naming rule after cleanup:** types and APIs describe **what they are** (`DomainTools`, `SessionState`, `UseDomainModelValidation`) — not which migration generation they are.

Aligns with AGENTS: name for what it is; domain model is the key artifact; no dual-track product language.

---

## 2. Inventory (code — high signal)

### 2.1 Rename (product identifiers)

| Current | Proposed | Location |
|---------|----------|----------|
| `V3DomainTools.cs` | `DomainTools.cs` (or `McpDomainTools.cs` if clash risk) | `Poly.Mcp/Tools/` |
| `V3Response` | `ToolResponse` or `DomainToolResponse` | same |
| `V3SessionTool` | `SessionTool` | same |
| `V3QueryTool` | `QueryTool` | same |
| `V3EvolveTool` | `EvolveTool` | same |
| `V3EvalTool` | `PolicyTool` / `EvalTool` | same |
| `V3SessionState` | `SessionState` / `DomainSessionState` | `Poly.Mcp/Sessions/` |
| `V3McpSmokeTests` | `McpSmokeTests` / `DomainMcpSmokeTests` | `Poly.Tests/Mcp/` |
| `V3LibraryDomain` | `LibraryDomain` | `Poly/DomainModeling/Examples/Demos/` |
| `V3ECommerceDomain` | `ECommerceDomain` | same |
| `UseV3DomainModelValidation` | `UseDomainModelValidation` | `DomainModelAnalyzer.cs` |
| `UseV3DomainModelAnalysisPipeline` | `UseDomainModelAnalysisPipeline` | same |

**Public MCP tool names** (`create_domain_session`, etc.) — **do not rename** unless an external agent contract requires it. Only C# type names and file names.

### 2.2 Prose-only (no type rename)

| Area | Action |
|------|--------|
| `DomainObject`, builders, examples XML docs | “V3” → “domain model” / “immutable DomainModeling” |
| `EvolutionTransaction` tombstone comments | Keep short historical “V2 was mutable…” once; drop “V3 uses” → “Immutable domain uses…” |
| `Poly.Mcp/README.md` | “V3 tools” → “Domain tools”; fix any remaining migration phrasing |
| Module READMEs (`DomainModeling/README.md`) | “V3 immutable core” → “immutable domain model” |
| `AGENTS.md` / `CORE.md` | Prefer “DomainModeling” over “V3”; keep “V2 deleted” where needed for agents |

### 2.3 Explicitly out of scope (do not churn)

| Item | Why |
|------|-----|
| `docs/plans/v2-to-v3/**` path and historical task IDs | Provenance; rename folder only in a **docs archive** pass |
| ADR titles that include “V2→V3” | Historical decisions |
| Branch name `rewrite/domainmodeling-from-scratch` | Optional later; not required for product clarity |
| Git history / old commit messages | Immutable |
| Archived interpretation plans under `docs/plans/archive/` | Leave |
| Re-introducing “V4” | Forbidden — no more version prefixes for the modeling stack |

---

## 3. Execution phases (one PR / one agent task family each)

### Phase R0 — Freeze rules (docs only, ~1h)

- [ ] Accept this plan; link from `docs/plans/README.md` and `vs-README` “Deferred”.
- [ ] Rule: **no new `V3`/`V2` type names** in product code.
- [ ] Rule: product work (`vs-*` slices) **does not** mix large renames unless idle + green.

### Phase R1 — MCP identifiers (highest agent confusion)

**Goal:** MCP layer reads as the only product adapter, not “the V3 one.”

| Step | Work |
|------|------|
| R1.1 | Rename `V3Response` → chosen name; update all tool return types |
| R1.2 | Rename tool classes `V3*Tool` → `*Tool`; update `Program.cs` registration |
| R1.3 | Rename `V3SessionState` → `SessionState` (or `DomainSessionState`) |
| R1.4 | Rename file `V3DomainTools.cs` → `DomainTools.cs` |
| R1.5 | Rename `V3McpSmokeTests` → `McpSmokeTests`; fix usings/calls |
| R1.6 | Grep: zero `V3` type names under `Poly.Mcp/` and `Poly.Tests/Mcp/` |

**Exit:** Build + MCP tests green; MCP tool **string names** unchanged.

### Phase R2 — Domain demos

| Step | Work |
|------|------|
| R2.1 | `V3LibraryDomain` → `LibraryDomain` (+ file rename) |
| R2.2 | `V3ECommerceDomain` → `ECommerceDomain` (+ file rename) |
| R2.3 | Update any benchmarks/tests that reference old names |

**Exit:** Build + any demo/benchmark references green.

### Phase R3 — Analysis extension methods

| Step | Work |
|------|------|
| R3.1 | `UseV3DomainModelAnalysisPipeline` → `UseDomainModelAnalysisPipeline` |
| R3.2 | `UseV3DomainModelValidation` → `UseDomainModelValidation` |
| R3.3 | Obsolete aliases **only if** external packages might call old names (today: internal — prefer hard rename) |
| R3.4 | Grep `UseV3` zero in repo product code |

**Exit:** Domain analysis still runs via `DomainModelAnalyzer`; tests green.

### Phase R4 — Active prose cleanup

| Step | Work |
|------|------|
| R4.1 | `Poly/DomainModeling/**` comments/READMEs — drop “V3” product voice |
| R4.2 | `Poly.Mcp/README.md`, `AGENTS.md` placement notes if any “V3-only” can become “DomainModeling-only” |
| R4.3 | Active plans (`vertical-slice-finish-plan`, `vs-README`, review-fix-plan) — say “domain model” not “V3 stack” where it means current product |
| R4.4 | Leave `docs/plans/v2-to-v3/` and ADRs as historical |

**Exit:** New agents reading CORE/AGENTS/MCP README never think a V2 stack still exists or that “V3” is a second product.

### Phase R5 — Optional docs archive (later)

| Step | Work |
|------|------|
| R5.1 | Optionally move `docs/plans/v2-to-v3/` → `docs/plans/archive/v2-to-v3-migration/` when M2 closed and no one needs “active” path |
| R5.2 | Redirect stubs: short README at old path pointing to archive + `vertical-slice-finish-plan` / `vs-README` |
| R5.3 | Do **not** rewrite every historical micro-task filename |

---

## 4. Suggested micro-task seeds (when execution starts)

Create under `docs/plans/v2-to-v3/simple-agent-tasks/` (or a new `docs/plans/rename/`):

| ID | File (proposed) | Phase |
|----|-----------------|-------|
| rn-0 | `rn-0-freeze-no-new-v3-names.md` | R0 |
| rn-1 | `rn-1-mcp-type-rename.md` | R1 |
| rn-2 | `rn-2-demo-rename.md` | R2 |
| rn-3 | `rn-3-analyzer-extension-rename.md` | R3 |
| rn-4 | `rn-4-active-prose-cleanup.md` | R4 |

Do **not** create the full `rn-*` suite until R0 is accepted and product slices are not in flight (or a dedicated rename day is declared).

---

## 5. Risk and order relative to vertical slices

```text
vs slices 0–3 / M2 product close   ← prefer finish first
        │
        ▼
   Naming cleanup R0–R4            ← mechanical, high conflict if mixed with evolve/MCP feature work
        │
        ▼
   Optional R5 archive path
```

| Risk | Mitigation |
|------|------------|
| Merge conflicts with MCP/policy tools | Run R1 when no Slice 3 MCP tasks open |
| Agents invent “V4” | Plan + AGENTS: modeling stack is unversioned `DomainModeling` |
| Broken external callers of `UseV3*` | Grep solution; hard rename if no external package |
| Over-renaming docs | R4 only **active** docs; archive stays historical |

---

## 6. Definition of done (whole plan)

- [ ] No product type/file named `V3*` under `Poly/`, `Poly.Mcp/`, `Poly.Tests/` (except quotes in historical comments if unavoidable)
- [ ] No `UseV3*` APIs
- [ ] Active READMEs / AGENTS / CORE speak of DomainModeling + MCP, not “V3 product path”
- [ ] Build + full test suite green
- [ ] `docs/plans/v2-to-v3/` either clearly labeled historical or moved per R5

---

## 7. One-line recap

> **V2 is gone — stop calling the only stack “V3.” Rename product identifiers to what they are; keep migration history in plans/ADRs, not in type names.**
