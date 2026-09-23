# PR 76 Slice C Final Boss follow-ups — 2026-09-22

- **SHA**: `55b852754135b83828ee7837daa5fc671d06989c`
- **Review**: `docs/agent/reviews/2026-09-22-pr76-55b85275-final-boss.md`
- **Prior review**: `docs/agent/reviews/2026-09-22-pr76-55b85275-razor.md` (+ followups)
- **Verdict**: ship (0 bugs; open suggestions below; nothing upgraded)
- **Disposition summary**: F1–F5 all **still open**, severities unchanged; no fail-open found so none upgraded.

## Open

- [ ] **F1** — `Poly/DomainModeling/Compile/DomainSession.cs:168-170` — Remove or make reachable the Emit empty-catalog throw; `Lower` (`:151-154`) always stamps one `SyntaxModule` descriptor and the property has `private set` (sole writer `:152`), so `Count==0` is unreachable. Add an oracle if the throw stays. (Final Boss Issue 1 / Razor Issue 1)

- [ ] **F2** — `src/Poly.DslCompiler/MinimalApiGenerator.cs:1131-1134` + `SliceCProducerLoopCatalogTests.cs:139-144` — Close the http-null + storage-present sibling: fail-closed without `HttpSurfaceMetadata` **or** document harness emit as intentional and rename/extend `NoHttpBag_NoOps` so it forces that sibling. Reachability proven harness-only (Compile registers the contributor only with the `http` id, and `HttpSurfacePass` total-publishes the bag); still a claim-vs-oracle gap. (Final Boss Issue 2 / Razor Issue 2)

- [ ] **F3** — `DomainSession.ArtifactCatalog` — Document sentinel-only contract **or** record descriptors matching Emit/contributor file names (single catalog writer) and add a subset-of-files honesty oracle. No product consumer today, so this is overstatement, not wrong output. (Final Boss Issue 3 / Razor Issue 3)

- [ ] **F4** — `DslCompiler.OpenCompileSession:245-246` vs `MinimalApiHostArtifactContributor` ctor docs (`MinimalApiGenerator.cs:1112-1114`) — Pass `dbms: null` at Load registration so `ResolveDbms(domain)` runs, **or** correct the docs to "compile DbmsPack always wins" and lock `uses sqlite` + default `Compile` Program.cs provider with a test. Pre-existing shape. (Final Boss Issue 4 / Razor Issue 4)

- [ ] **F5** — `MinimalApiGenerator.cs:1137-1138` — Align exception text with checked preconditions (drop vacuous "behavior"; `BehaviorMetadata.From` never returns null). (Final Boss Issue 5 / Razor Issue 5)

## Process

- [ ] **P1** — Slice C source-scan anti-invent regex covers only `src/Poly.DslCompiler/DslCompiler.cs`. Keep the runtime Compile oracle as primary evidence; treat string scans as secondary. (Razor P1 — unchanged)

## Disposition log

| Item | Status | Evidence (this SHA) |
|---|---|---|
| F1 | still open | `DomainSession.cs:151-154` unconditional stamp; `:152` sole `private set` writer; `:168-170` unreachable |
| F2 | still open | `MinimalApiGenerator.cs:1133-1134` gate; `HttpSurfacePass.cs:13-16` total publish; `HostContributor_ParentWithProducedBillingContract_...` passes (1/1) |
| F3 | still open | `grep ArtifactCatalog` → `DomainSession.cs` + tests only; sentinel `:152-154` |
| F4 | still open | `DslCompiler.cs:246` passes non-null `dbms`; `MinimalApiGenerator.cs:1112-1114` docs; `:1142` `ResolveDbms` falls back |
| F5 | still open | `MinimalApiGenerator.cs:1137-1138` message lists "behavior"; `BehaviorMetadata.cs:16-44` total |
| P1 | still open | regex in `SliceCProducerLoopCatalogTests.cs:83-92` scoped to `DslCompiler.cs` |
| Bugs | none | Razor 0 bugs re-verified; no fail-open on Compile path |
