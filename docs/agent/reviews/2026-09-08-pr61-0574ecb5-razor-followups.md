# Razor follow-ups — PR 61 @ 0574ecb5 — 2026-09-08

Checkable tasks from `docs/agent/reviews/2026-09-08-pr61-0574ecb5-razor.md`. Implementer closes boxes with evidence; do not mark done from chat alone.

## Open

- [ ] **F1** — **bug** — Fail-close `require not` + unlinked path-prefix at runtime. Soft `Conditional(ExistsRelated, …, false)` at `DomainExpressionLoweringPass.cs:150` inverts under `Not` (`:278-279`; parser `PolyDslParser.cs:317-319`). Unlinked must not make `EvaluatePolicy(not_*)` true / allow InvokeAction. Align with export `EmitPathPrefixRequireGuards` (already walks Not). Add test: unlinked Enrollment-style `require not SectionFull` → Succeeded false (guards or Failure string per F2).

- [ ] **F2** — **bug** — Resolve ONE-TREE for unlinked singular require. Tip runtime: `DomainEntityInstance.cs:511-521` → `FailedGuards` policy name only (`PathPrefixRequireFailureTests.cs:51`). Export: `DomainResult.Failure("requires a linked")` (`:76-77`). Either restore module Failure path (reverted skip in `faee038c`) and assert the Failure string on InvokeAction, **or** rewrite PR/export/tests to FailedGuards-only and stop claiming the Failure string is the runtime outcome. Fix test name/doc theater (`ReturnsFailure` / class summary).

- [ ] **F3** — **suggestion** — Reconcile bare EvaluatePolicy / MCP unlinked contract with PR claims. Tip: false / MCP Success+`false` (`DomainEntityInstanceTests.cs:3153`, `SurfaceExtensionDogfoodTests.cs:243-256`). Update PR body; choose throw vs soft-false explicitly; rename MCP test if Success=true is intentional.

- [ ] **F4** — **suggestion** — Add runtime multi-hop unlinked require coverage (first hop missing; second hop missing) once F2 contract is fixed. Export-only today: `DomainToCSharpExporterTests.Export_PathPrefixMultiHop_GuardsEachNestedNav`.

- [ ] **F5** — **suggestion** — Fix nested-hop Failure entity attribution in `WalkPathPrefixRequireGuards` (`DomainToCSharpExporter.Actions.cs:908`); stop locking `'team' on entity 'Issue'` in `DomainToCSharpExporterTests.cs:1918`.

- [ ] **F6** — **nit** — Rename `RelationshipNavigation_LowersToGuardedHop` (`DomainExpressionLoweringPassTests.cs:83`) to match NullForgiving oracle.

- [ ] **F7** — **process** — When soft-failing a dual-path semantic (ExistsRelated Conditional), run sibling-path checklist for `require` **and** `require not`, bare EvaluatePolicy, MCP evaluate, export guards, and many-cardinality before landing. Update phenomenal-review / suite gate if this class recurs.

## Disposition of prior items

No prior Razor/Final-Boss follow-ups file for PR 61 on this tip. Prior path-prefix throw oracles were intentionally retargeted in-diff (`EvaluatePolicy_ToOne…_Throws` → `_ReturnsFalse`; export coalesce-throw → Failure/NullForgiving) — treat as **superseded by F1–F3**, not silently "fixed."

## Ship gate

**not-ship** until **F1** and **F2** closed with tests that force the chosen sibling paths.
