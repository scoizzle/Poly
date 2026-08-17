# Target architecture doc — 2026-08-17

- **Target**: paths `docs/plans/domainmodeling-target-architecture-2026-08-16.md` vs current tree
- **Mode**: standard
- **Issue counts**: 3 bugs, 7 suggestions, 3 nits
- **Verdict**: **useful as a folder sketch, not as a locked architecture.** §0–§1 pipeline is the ADR. M1 is factually wrong. §8 must not lock M1–M6 as “ADR consequences.” Do not implement from this file (the file already says that — keep it).
- **Process notes**: A “target layout” that forbids a second path while the admitted cleanup still *requires* `ExecuteStructured` after slice 3 will train the next agent to “finish the folders” and delete the runtime seam. Same class of AC contradiction as the 08-15 five-wave plan.

## Summary

The organizing idea is right: pipeline phases, not type-family folders; session is the compile; contract fill is not a library; vendors stay in `src/`. The document then overclaims. `DomainToCSharpExporter` already returns Syntax via `ToSyntax` + `CSharpGenerator` — it does not “print C# strings, bypassing the AST.” DomainModeling **does** emit text (`.poly`). Several live types have no home. §8 treats folder moves and “exactly two lowering passes / no ExecuteStructured” as locked, which reopens the 08-16 cleanup’s “still a lie” on purpose.

Oracle: source + greps this pass, no tests run.

## Issues

### Issue 1 -- Severity: bug
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:88` (code: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:12-53`, `DomainProgramProjection.cs:102-136`)
- Description: M1 says the exporter “walks the domain and prints C# strings, bypassing the AST,” and that collapsing it into `ToSyntax` + `CSharpGenerator` *creates* that path. Today `Export` **is** `return DomainProgramProjection.ToSyntax(...)`. The class xmldoc already says the output is Syntax for `CSharpGenerator`. `ToSyntax` still *calls into* the exporter for subscription collection, value types, `DomainResult`, contract adapters, and entity type defs — a split walk, not a string printer. Selling M1 as “close the printer dual by making it the VM’s path” mis-states the dual. The remaining dual is **emit-mode `LoweringContext` / `LowerStageTransitions` vs runtime `ExecuteStructured`**, plus C# idiom nodes (`DomainResult`, `Create` factories) inside the projection.
- Suggestion: Rewrite M1: *finish* projection (move builder methods off the exporter type; C# idiom as a named contributor). Do not claim a string-printf exporter. Complexity-map “printer dual” for C# is already `ToSyntax` → `CSharpGenerator`; `.poly` print is `DomainDslPrinter` (separate, and product).
- Status: open

### Issue 2 -- Severity: bug
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:14` and `:29`
- Description: §0: DomainModeling “never emits text, never runs a second evaluator.” Product **must** emit `.poly` (`DomainDslPrinter`, MCP `export_dsl`, session print). That is not an “accreted layer.” §1 puts print nowhere except Interpretation’s `CSharpGenerator`. Agents will treat `Language/` as parse-only and orphan the printer, or move `.poly` print into Interpretation (wrong seam).
- Suggestion: Pipeline: `… → print (.poly, Language/) · lower → Interpretation`. “Never emit **host** text (C#/SQL/HTTP) from DomainModeling” is the lock. `.poly` print stays.
- Status: open

### Issue 3 -- Severity: bug
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:153-154`, `:66-67`, `:92`, `:171`
- Description: §8 says M1–M6 are “direct consequences of the ADR/CORE” and must not be reopened here. They are not:
  - M3 “`Lowering/` contains exactly two passes” + “nowhere for a second path” **forbids** `ExecuteStructured`. The 08-16 cleanup’s slice 3 **keeps** that seam and says one-lowering is still a lie until host-ABI (not this layout).
  - M1 “C# idiom becomes a target library” **decides O2**, which §7 still calls open.
  - M4 “runtime shrinks by losing its reason to be big” is host-ABI work, not a folder.
  Locking these as ADR means a folder move can delete the runtime seam “because the target has nowhere for it.”
- Suggestion: §8 lock only: pipeline phases, Domain/catalog/session, contract fill ≠ library, no new plugin host. Mark M1/M3/M4 as *target after host-ABI*, same “still a lie” as cleanup slice 3. Keep O2 actually open or delete it.
- Status: open

### Issue 4 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:35-82`
- Description: Target layout omits live product types with no stated delete:
  - `Bootstrap/` (`DomainFactory`, built-in primitives) — every MCP session and test starts here.
  - `Queries/` (`DomainQueries`) — MCP overview/detail.
  - `Packs/DomainCompilation.cs` (`PeekExtensions`, `WithSeed`, `HostForSource`) — compile, not a library.
  - `ExpressionMeaning.cs` — session field; not in `Meaning/` list.
  - `SqlAnnotationSyntax`, `IAnnotationSyntax`.
  - `Runtime/DomainExpressionRewriteBase.cs` — product-local rewriter (deep-research S6).
  - Analysis lint/fact types that are not Catalog/Capability/Structure/Subscriptions/Storage: `AuthoringSuggestionAnalyzer`, `RuleCoverageAnalyzer`, `EffectAnalyzer`, `PolicyConstraintAnalyzer`, `CrossReferencePass`, `RuntimeAnalysisCache`, `BehaviorMetadata`/`BehaviorModel`, `ExpressionTypeAnalyzer` (file already under Analysis; target also lists `ExpressionTypeCheckRegistry` under Meaning/).
- Suggestion: Add homes or an explicit **delete / MCP-only / later** column. `DomainCompilation` → `Compile/`. `ExpressionMeaning` → `Meaning/`. Queries/Bootstrap stay or fold into Compile/MCP.
- Status: open

### Issue 5 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:37`, `:72-73`
- Description: `Ontology/` is “immutable facts — zero behavior.” `Domain.Relationships` is a computed flatten (`Domain.cs:28-29`). `Ontology/Dispatch/` is closed-world *behavior* (the switch). Putting dispatch under “zero behavior” teaches the wrong rule: records may derive; dispatch is not a fact.
- Suggestion: Ontology = records + derived flatten. Dispatch is Compile- or Lowering-adjacent (walkers over IR), not “facts.”
- Status: open

### Issue 6 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:66-68`
- Description: “Exactly two passes plus the projection.” `ToSyntax` is a third walk and still depends on exporter statics. Storage mapping types (`IStorageConvention`, `TypeMappingRegistry`) are session tables, not lowering, and §4 already moves them to Meaning/Storage — good — but then Lowering is not “exactly two files + projection.”
- Suggestion: Lowering = DE pass + effect pass + `ToSyntax`. Session tables are not lowering. Do not count “two” as an invariant.
- Status: open

### Issue 7 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:116-117` vs `:146`
- Description: §4 moves `DbContextGenerator` / `MinimalApiGenerator` / `HttpFileGenerator` into `Poly/DomainModeling/Libraries/`. O3 asks whether vendors live in `src/` (the 08-14 ADR: vendor assemblies). HTTP as a DomainModeling folder puts a host door inside the core library the lock says must not emit `Program.cs`.
- Suggestion: Mechanism (`IArtifactContributor`) in `Compile/`. Vendor + HTTP stay `src/Poly.Packs.*` / `src/Poly.DslCompiler` until a library assembly exists. In-tree `Libraries/` is only Temporal + StorageFacet (in-assembly seeds).
- Status: open

### Issue 8 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:29`
- Description: “Each phase is a **pure transform**.” Analyze writes metadata bags (not a new Domain). Evolution is analysis-gated mutation. Session load is not a transform of Domain facts into a Domain — it binds tables. Overselling purity will produce “session must not call analysis” or “evolution cannot sit beside compile” arguments.
- Suggestion: “Each phase has one input and one output; session threads frozen tables.” Do not say pure.
- Status: open

### Issue 9 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:171`
- Description: “Slices 1–3 are the first steps toward this layout” and “M3 and M6 are the end state the still-a-lie list names.” Slice 3 of the cleanup **does not** implement M3. An agent admitting both docs will treat folder moves (this file) as how you finish M3 without host-ABI.
- Suggestion: Relationship table: slices 1–2 ≈ M2 (session door + analyze). Slice 3 ≈ honesty on Comment, **not** M3. M1/M3/M4 wait on host-ABI + emit-on-session.
- Status: open

### Issue 10 -- Severity: suggestion
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:7`
- Description: “Not a migration plan — do not implement from this document” is correct. §2–§4 still read like a move checklist (`Parsing/` → `Language/`, etc.). Folder renames are the 08-16 plan’s parked “highest churn, lowest semantics” item. This doc will get executed as a rename suite if admitted by vibe.
- Suggestion: One line under §2: **no folder moves until cleanup slices 1–2** (session exists without Host). This file does not admit a rename CURRENT.
- Status: open

### Issue 11 -- Severity: nit
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:137`
- Description: “`DomainModeling` → `Syntax` for lowering.” CORE still says Syntax; the module is `Poly.Ast`. Stale the same way CORE is stale.
- Suggestion: `DomainModeling` → `Poly.Ast` for lowering. Fix CORE in the same honesty pass, not only here.
- Status: open

### Issue 12 -- Severity: nit
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:57-59`
- Description: `ExpressionTypeCheckRegistry` is listed under `Meaning/` but lives at `Analysis/ExpressionTypeCheckRegistry.cs`. `ExpressionFoldTable` is already under `Parsing/`. The map mixes current and target paths without saying which column is which (table §4 is clearer than the tree in §2).
- Suggestion: §2 lists **target** names only. Current paths only in §4.
- Status: open

### Issue 13 -- Severity: nit
- File: `docs/plans/domainmodeling-target-architecture-2026-08-16.md:71`
- Description: Runtime target omits `DomainExpressionRewriteBase` (present today). If M4 is “hand the program to Interpretation,” this rewriter is either deleted (CORE: use `SetNodeReplacement`) or it is a lie in the target tree.
- Suggestion: Name it: delete in favor of analysis replacement, or it is pre-lower (and then it is Lowering/, not Runtime/).
- Status: open

## What is sound (not issues)

- Data-flow folders over GoF / type-family dumps.
- Contract fill out of `Packs/`.
- Catalog + `IDomainLibrary` as compile mechanism, Temporal/Storage as libraries.
- Vendors as separate assemblies (O3, if §4 is fixed).
- Dispatch bases kept (closed world).
- Not CURRENT; cleanup plan remains the executable deletion doc.
- Open questions O1 and O4 are real.
