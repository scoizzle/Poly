# PR 76 Slice C Final Boss follow-ups — 2026-09-23

- **SHA**: `0752ed6b982d61230b07731072160cc025bfb0e6`
- **Review**: `docs/agent/reviews/2026-09-23-pr76-0752ed6b-final-boss.md`
- **Prior**: `docs/agent/reviews/2026-09-22-pr76-55b85275-final-boss.md` (+ followups), `...-razor.md` (+ followups)
- **Verdict**: ship (0 bugs, 0 suggestions, 0 nits; no item upgraded)
- **Disposition summary**: **F1–F5 all fixed** this SHA; Razor P1 remains open as a process follow-up. No fail-open or stop-condition regression found.

## Disposition log (prior open items, re-verified from this SHA)

| Item | Status | Evidence (this SHA) |
|---|---|---|
| F1 — dead Emit empty-catalog throw | **fixed** | Throw removed from `DomainSession.Emit` (`Poly/DomainModeling/Compile/DomainSession.cs:164-196`); `Lower` sole writer `:153-155`, `private set` `:47`; grep no residual throw. Unreachable before/after, no reachable path changed. |
| F2 — MinimalApi NoOp claim vs http-null+storage sibling | **fixed** | Gate comment `src/Poly.DslCompiler/MinimalApiGenerator.cs:1133-1134`; renamed test `SliceCProducerLoopCatalogTests.cs:140`; new sibling oracle `:150-171` forces http-null + storage-present and asserts `["Program.cs","demo.http"]`. |
| F3 — `ArtifactCatalog` sentinel vs inventory docs | **fixed** | `DomainSession.cs:42-46` and `ArtifactDescriptor.cs:3-6` narrowed to "Lower sentinel … not an emit/contributor file inventory". |
| F4 — Compile DbmsPack-wins comment/test | **fixed** | Ctor doc `MinimalApiGenerator.cs:1112-1117`; registration non-null at `DslCompiler.cs:246`; locking test `SliceCProducerLoopCatalogTests.cs:174-189` (Generic vs Sqlite provider). |
| F5 — HTTP exception text said "behavior" | **fixed** | `MinimalApiGenerator.cs:1138-1140` now "storage and aggregate"; grep `require storage` → only this string. |
| P1 — anti-invent source-scan regex scoped to `DslCompiler.cs` | **still open** (process, unchanged) | `SliceCProducerLoopCatalogTests.cs:75-89` reads only `src/Poly.DslCompiler/DslCompiler.cs`. Runtime `Compile_*` oracle is primary; scan is secondary. Not a correctness bug. |
| Bugs | **none** | Razor 0 bugs re-verified; Compile path fails closed; no invent restored. |

## Open

- [ ] **P1** — `Poly.Tests/DomainModeling/Compile/SliceCProducerLoopCatalogTests.cs:75-89` — Anti-invent source-scan regex covers only `src/Poly.DslCompiler/DslCompiler.cs`. Either broaden the scan to other host producers or state in-test that the runtime `Compile_*` oracle is the primary anti-invent evidence. Low priority; the behavior is already covered at runtime. (Razor P1 — unchanged)

## Closed this SHA (no action)

- [x] **F1** — dead Emit empty-catalog throw removed; reachability argument stands (`Lower` always stamps; sole writer).
- [x] **F2** — NoOp narrowed to both-null and the http-null + storage-present sibling is documented and forced by an oracle.
- [x] **F3** — `ArtifactCatalog` / `ArtifactDescriptor` docs narrowed to sentinel-only.
- [x] **F4** — `dbms` ctor comment corrected and Compile-wins behavior locked by a test.
- [x] **F5** — HTTP exception text aligned with the storage+aggregate gate.

## Process

- [ ] **P2** — No recurrence of the F1–F5 class in this diff; the honesty pass added oracles rather than weakening them. Keep the runtime Compile oracle primary over string scans (P1).
