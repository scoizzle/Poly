# Fleet-eval 2026-08-12 — slice 14: Docs & product consistency

Slice: `Poly.Mcp/Docs/poly-dsl-guide.md` (product-true reference), `docs/CORE.md`,
`docs/agent/`, `docs/decisions/`, `docs/plans/`. Probes in `probes/fleet-eval/14-docs/`.
All probed through `scripts/run-probe.sh` (0/0 gate) unless noted. Docs were NOT edited.

> Cross-cut: `Poly.Mcp/Docs/poly-dsl-agent-guide.md` (the "agent guide") is a **second,
> diverged copy** of the product guide, also embedded and served by `get_dsl_guide`
> (fallback). It carries the SAME stale claims below (enum(...) constraint at its line 410,
> "invoke quantifiers any/all" at its line 328, `unlink_instances` deferred at its line 369)
> but with different section numbering and no §0 Modeling Principles. Two sources of
> product-truth for one surface is itself a consistency finding (F12).

## F1 — `Total * 0.9` documented as shipped arithmetic is rejected (decimal literals typed as Text)
- **Signal:** guide-drift / compile-fail of a documented example
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-21-decimal-documented.poly` +
  `scripts/run-probe.sh` → `Compilation failed: arithmetic operand is not numeric (got 'Number' and 'Text')`. Also `doc-14-decimal.poly`, `doc-14b-decimal-assign.poly`, `doc-14d-decimal-literal.poly` (`Total >= 0.5` → "comparison between incompatible types 'Number' and 'Text'").
- **Expected:** guide §8 "Expression Gaps" table (line 834) documents `| Arithmetic (+, -, *, /) | ✅ | ✅ shipped | Total + 5 > 10, Total * 0.9 |`; the shipped-surface list (line 728) says "Arithmetic (+, -, *, /) in expressions".
- **Actual:** decimal literals (`0.9`, `0.5`) are parsed by the tokenizer as Number tokens but `DslExpressionParser.ParsePrimary` falls back to `DomainExpression.Literal(numText)` (a **Text** literal) when `long.TryParse` fails — so any expression containing a decimal is rejected as Number/Text type confusion. Integer arithmetic (`Total * 2`) compiles 0/0 (`doc-14c-int-arith.poly`); decimal `range(0.01, 1.0)` bounds compile 0/0 (`doc-14f-range-only.poly`). The guide's own shipped example does not compile.
- **Proposed patch (do not apply):** `ParsePrimary` must parse decimal literals as numeric (decimal/double) literals, or the guide must restrict documented arithmetic to integers.

## F2 — `enum(v1, v2, ...)` constraint documented in §11 is rejected by the parser
- **Signal:** guide-drift
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-06c-enum-constraint.poly` +
  `scripts/run-probe.sh` → `Compilation failed: Parse error: Inline enum(...) constraints are no longer supported. Use a top-level enum type declaration`.
- **Expected:** guide §11 Constraint Reference (line 892) documents
  `| Enum | enum(v1, v2, ...) | Color: Text enum(Red, Green, Blue) |` as a shipped Text constraint; §8 "Shipped in the current product surface" (line 732) lists "`default` and `enum` constraints".
- **Actual:** the parser rejects `enum(...)` on any property. Commit `5bd482af` ("prune EnumConstraint dead-dual") removed it; the guide was not updated. §3's constraint table correctly omits `enum(...)`, so the guide contradicts itself (§3 vs §11 vs §8).
- **Proposed patch (do not apply):** drop `enum(...)` from §11 Constraint Reference and from the §8 shipped list; §3's table is already correct.

## F3 — §6 fan-out example uses dotted binder arg `item.Qty`; parser requires space form
- **Signal:** guide-drift
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-06a-fanout-dot.poly` →
  `Parse error: Expected parameter name, got '.'` on `invoke item.Mark(amount: item.Qty)`.
  Space form `invoke item.Mark(amount: item Qty)` compiles 0/0 (`doc-06a2-fanout-space.poly`).
- **Expected:** guide §6 fan-out examples (lines 483-486) show
  `invoke item.Mark(amount: item.Qty)` — a dotted binder path-prefix as an invoke arg.
- **Actual:** the parser rejects dotted binder path-prefix in `for` invoke args; only the space-separated path-prefix form (`item Qty`, the §7 `order Code` shape) is accepted. The guide's own fan-out examples do not compile as written.
- **Proposed patch (do not apply):** fix the §6 examples to `item Qty`, or make the parser accept dotted binder path-prefix in fan-out args.

## F4 — §8 claims invoke "quantifiers any/all; filter where" but §6 removed them; parser rejects
- **Signal:** guide-drift (guide-internal contradiction)
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-06b-invoke-quant.poly` →
  `Parse error: Expected effect (transition, assign, create, invoke, for, if), got 'items'` on `invoke any items.Mark(amount: 5)`.
- **Expected:** §8 "Shipped in the current product surface" (line 730) lists
  "Invoke effect (… quantifiers `any`/`all`; filter `where`)".
- **Actual:** §6 (line 461) states "One fan-out mode, no `any`/`all`/`each` quantifier," and the parser rejects `invoke any/all …`. The §8 shipped-list bullet is stale; the guide contradicts itself on the same page.
- **Proposed patch (do not apply):** remove "quantifiers any/all; filter where" from §8 line 730; the `for` fan-out is the only mode.

## F5 — §0.4 example uses `;`-separated create initializers; parser requires whitespace
- **Signal:** guide-drift
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-08-exact04.poly` (verbatim §0.4 block) →
  `Parse error: Unexpected character ';'` at `create Fine { Amount: 5; Reason: "Overdue" }`.
  Comma form fails too (`doc-08b-comma-init.poly`); space form compiles 0/0 (`doc-08c-space-init.poly`).
- **Expected:** §0.4 (lines 124-127) shows `create Fine { Amount: 5; Reason: "Overdue" }` with semicolons, and §0.3 shows `create in tokens { Lexeme: "let" Kind: Keyword }` with whitespace — two different separators in the same doc.
- **Actual:** only whitespace-separated initializers parse. The §0.4 example as written does not compile.
- **Proposed patch (do not apply):** standardize the guide on whitespace-separated initializers in both examples.

## F6 — §0.3 DMEFF011 example is authored as a top-level action, which the grammar rejects
- **Signal:** guide-drift
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-11-exact03.poly` (verbatim §0.3 block:
  `Token: entity {...}` then a bare `Lex: action { create in tokens {...} }`) →
  `Parse error: Expected 'entity' or 'enum' definition, got 'Lex'`. Embedding the action inside an entity with a `tokens` nav (`doc-11d-in-entity.poly`) then fails differently: `error CS1061: 'Parser' does not contain a definition for 'Keyword'` — the bare initializer value `Keyword` is lowered as a member reference, not a string literal.
- **Expected:** §0.3 "✅ Correct" example should compile as the DMEFF011 happy path.
- **Actual:** the example as printed is not a valid document (action at top level), and its `Kind: Keyword` initializer value (bare identifier on a `Text` property) generates a CS1061 member reference. DMEFF011 itself works when the value is a quoted string (`doc-11e-dmeff011-clean.poly` 0/0; missing-required rejection `doc-11f-missing.poly` works).
- **Proposed patch (do not apply):** wrap the `Lex` action in a containing entity with a `tokens` nav, and quote the `Keyword` value.

## F7 — `create in` a to-one navigation passes analysis but the export breaks (CS1061)
- **Signal:** compile-fail (late-rung; analysis accepts, codegen fails)
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-19-create-in-toone.poly` and
  `doc-19b-create-in-toone-plain.poly` → 0/0 parse+analysis, then
  `error CS1061: 'Order' does not contain a definition for 'CreateInvoice'` — the exporter emits `this.CreateInvoice(100L)` for `create in invoice` where `invoice: Invoice` is a to-one nav.
- **Expected:** guide §9 line 853 says graph wiring happens through `create in Rel { … }` without restricting cardinality; §0.3 says "To-one navigation bindings in `create in` initializers remain rejected (analyzer fail-closed)" — i.e., to-one create-in shapes should fail at analysis, or compile.
- **Actual:** analysis accepts `create in <to-one-nav>`; the C# export emits a nonexistent `Create{Nav}` method → CS1061. The earliest rung that should reject this (per §0.3's fail-closed claim) does not.
- **Proposed patch (do not apply):** reject `create in` on OneToOne navs at analysis (DMEFF007-style), or emit the factory correctly.

## F8 — §0.1 claims a `*Id`/`*Code`/`*Isbn` Text-near-relationship diagnostic that does not exist
- **Signal:** guide-drift (documented analysis check absent)
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-15-idsmell.poly` — `Loan: entity { book: Book; BookIsbn: Text }` → `errors: 0, warnings: 0`.
- **Expected:** §0.1 (lines 36-37) states "A property typed as `Text` with a name matching `*Id`, `*Code`, or `*Isbn` on an entity that already has a relationship to that type triggers a diagnostic."
- **Actual:** no diagnostic exists; a grep of `Poly/DomainModeling` and the diagnostic-code table finds no such rule. The analysis check is documented but not implemented.
- **Proposed patch (do not apply):** implement the check, or mark §0.1's analysis claim as aspirational/remove it.

## F9 — duplicate annotations silently last-win instead of producing the documented parse error
- **Signal:** silent-gap (guide-drift)
- **Severity:** 🟠
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-18-annos.poly` —
  `Item: entity table("A") table("B") { Code: Text column("X") column("Y") }` → 0/0; export emits `ToTable("B")` (first annotation silently dropped).
- **Expected:** §3 line 241: "Multiple annotations of the same keyword on the same target produce a parse error."
- **Actual:** no error — the last annotation wins silently. Fail-closed claim violated (silent data loss).
- **Proposed patch (do not apply):** enforce the duplicate-annotation parse error the guide documents.

## F10 — §9 says `unlink_instances` is deferred, but the MCP tool ships
- **Signal:** guide-drift (surface ships what the guide omits/marks deferred)
- **Severity:** 🟡
- **Slice:** docs & product consistency
- **Repro:** static — `Poly.Mcp/Tools/RuntimeTool.cs` implements `UnlinkInstances`
  (`[McpServerTool(Name = "unlink_instances")]` with full validation); guide §9 line 853 says "`unlink_instances` is deferred."
- **Expected:** per guide, no unlink tool exists.
- **Actual:** `unlink_instances` is registered and functional; the guide under-sells the shipped surface.
- **Proposed patch (do not apply):** update §9 to document the shipped `unlink_instances` tool.

## F11 — `create Type in Rel { … }` shape referenced by §9 does not parse
- **Signal:** guide-drift (stale/ambiguous wording)
- **Severity:** 🟡
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-17-create-relation.poly` —
  `create Order in orders { Total: 100 }` → `Parse error: Expected LBrace, got 'in'`.
- **Expected:** §9 line 853 says graph wiring happens "through `create in Rel { … }` (or `create` with `RelationshipName`)" — implying a `create Type in Rel` form.
- **Actual:** no such form exists; only `create Type {…}` and `create in Rel {…}` parse. The parenthetical is stale.
- **Proposed patch (do not apply):** remove the parenthetical or spell out the two real forms.

## F12 — two embedded guide files diverge (product vs agent guide)
- **Signal:** consistency (dual source of truth for one surface)
- **Severity:** 🟡
- **Slice:** docs & product consistency
- **Repro:** `diff Poly.Mcp/Docs/poly-dsl-guide.md Poly.Mcp/Docs/poly-dsl-agent-guide.md` → 736 diff lines; different section numbering, agent guide lacks §0 Modeling Principles, and `get_dsl_guide` serves product-first with the agent guide as fallback (both are EmbeddedResource in `Poly.Mcp.csproj`).
- **Expected:** one product-true reference (AGENTS.md: "The product guide is the single source of truth").
- **Actual:** a second, structurally-divergent guide is packaged and reachable; the smoke test `GetDslGuide_GoldenExample_AppliesCleanly` searches for `## 11. Example (Round-Trip Safe)` (agent-guide numbering) with a substring fallback to find the product guide's `## 13` section — a fragile dependency on the duplicate.
- **Proposed patch (do not apply):** remove the agent guide or regenerate it from the product guide; make the smoke test read the product guide's real section number.

## F13 — `required` on a navigation property is rejected, but §0.3 DMEFF011 prose implies navs can be required
- **Signal:** guide-drift (prose implies an authorable shape that is a parse error)
- **Severity:** 🟡
- **Slice:** docs & product consistency
- **Repro:** `probes/fleet-eval/14-docs/doc-10c-nav-required.poly` —
  `Loan: entity { borrower: Patron required }` → `Parse error: Expected property, stage, action, or policy, got 'required'`.
- **Expected:** §0.3 DMEFF011 text (lines 93-98) says "every `required` property of the created entity must be provided … The back-reference navigation is exempt from this check," which reads as: navs can be `required` and the back-ref is the exemption.
- **Actual:** `required` is only authorable on scalar/enum-typed properties; `borrower: Patron required` is a parse error, so the "exemption" covers a shape the DSL cannot express. The DMEFF011 check itself (scalar required coverage, `doc-11f-missing.poly`) works.
- **Proposed patch (do not apply):** clarify in §0.3 that only scalar properties carry constraints; navs cannot be `required`.

## Cross-checks (no findings)

- **AGENTS.md build/test claims:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` and
  `dotnet run --project Poly.Tests/Poly.Tests.csproj` — both csprojs exist, `net10.0` + TUnit
  (`Poly.Tests.csproj` references TUnit 1.53.0), and both reference `Poly/Poly.csproj`. Claims hold.
- **CORE.md placement/seams:** `Poly/Ast`, `Poly/Analysis`, `Poly/Interpretation/Vm`,
  `Poly/Interpretation/Analysis`, `Poly/Introspection`, `Poly/DomainModeling`, `Poly/Grammar`,
  `Poly/Extensions`, `Poly.Mcp` all exist; `Poly/Validation` and V2 `Poly/Data/Modeling` are
  deleted as documented. Claims hold.
- **`-> Number` primitive return:** guide §6 says "not product — analysis error"; `doc-13b-num-return.poly` rejects with "only entity returns produced by create / create-in are supported." Guide accurate.
- **DMEFF009/010/DMSS003/reverse-side/OneToMany-invoke rejections:** all reject as the guide documents (`doc-09b`, `doc-16`, `doc-09`, `doc-12e`, `doc-12d`).
- **Date surface status:** `doc-06d-dates.poly` (now/today/guid in defaults + assign RHS) compiles 0/0; the previously-broken `default(guid)`/`now`/`today` export paths were fixed (export now emits `Guid.NewGuid().ToString()`, `DateOnly.FromDateTime(DateTime.Today)`, `DateTime.UtcNow`). Guide §8 shipped-surface boundaries (policy-body date rejection `doc-06e`, `default(today)` on Number rejection `doc-06f`) hold. Plan `docs/plans/dates-to-pack-2026-08-12.md` records the pack intent. Guide's "Date operations [not yet shipped]" line remains consistent with the deferred-pack plan.
- **Guide §13 example:** `doc-12-example13.poly` compiles 0/0 and round-trips.
