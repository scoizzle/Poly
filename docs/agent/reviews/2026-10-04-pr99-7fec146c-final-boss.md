# Final Boss review: PR 99 (N5, MCP `add` and `create_domain_session` use the DSL name rule) at 7fec146c

- **PR:** https://github.com/scoizzle/Poly/pull/99, "N5: MCP add and create_domain_session use the DSL name rule". One commit by Scot Murphy (hand/Grok lineage).
- **Head:** `7fec146cff1f208481c2109176f3c9741f34a872`. Checked with `gh pr view 99` and `git fetch origin pull/99/head`; it matches the assignment.
- **Base:** `89098229` (A3a #95, after K0 #96).
- **Master at filing:** `076a90143aa11883f40fba93f0e9d2a3819036bb`. Master moved: B1 #97 merged. `git merge-tree --write-tree origin/master 7fec146c` is clean. The merged tree builds and was tested (see Suites).
- **Review:** Review 1, Final Boss only, no Razor, one pass.
- **Mill:** OpenCode `opencode-go/deepseek-v4.1-flash`. It ran read-only from a throwaway `git archive` snapshot. Every finding it reported was spot-checked; see Mill disposition.
- **Clock:** 2026-10-04, 22:31–23:20 CDT.
- **Scope source:** `docs/domain-modeling/pipeline-convergence-plan.md` has **no N5 card**. `git grep N5` and `git grep F263` under `docs/` are empty on master and on the head. Scope was taken from Foreman's assignment and Scot's F263 ruling: "MCP add accepts a name exactly when the DSL parser accepts it, one rule". Scot's A3a decision also applies: reject bad domain/entity names at creation with a clear error, and keep `Lower`'s throw as the backstop.

## Verdict: NOT SHIP. Hand-editability gate: NO

There are two blocking bugs:

- **F300.** MCP `add property` accepts `Text`, `Number` or `Boolean` as a property name with any `typeName`. The DSL accepts those names only when the type is a primitive. For example, `Text: Money` (a value type) and `Text: Order` (an entity) are a parse error in the DSL, but MCP `add` accepts both. The exported domain then fails to re-apply. This breaks the one-rule requirement, and it makes three sentences false:
  - the PR body shape-matrix row,
  - the "cannot drift" claim,
  - the `IsPropertyName` doc comment, which is incomplete.
- **F301.** The new test `CreateDomainSession_WithNameTheDslRefuses_CreatesNoSession` is flaky. It compares the process-global `McpSessionStore.ListSessions().Count` before and after the call while other tests create sessions in parallel. CI runs TUnit with default parallelism, so CI will go red at random.

The rest of the work is sound and well proven:

- The scanner refactor is byte-identical. A differential run over 1.5M inputs gave identical output in two cultures.
- For every one of the 13 kinds plus the domain, the identifier rule agrees with the parser over 262k names. The only exception is the property/type case in F300.
- A refusal leaves no state behind.
- `ArtifactId.cs` is restored byte-identical to the pre-A3a version.
- Red-before and mutation testing are strong.

Both fixes are small. A fix for F301 is already verified (see F301).

## Checklist

| # | Check | Result |
|---|---|---|
| 1 | Shape matrix true; scanner unchanged; MCP acceptance = DSL acceptance | Scanner proven unchanged. Identifier kinds and the domain name agree with the DSL. **Property: diverges (F300).** Matrix row 9 is false as written; row 11 is slightly off (F306). |
| 2 | A refusal creates no session/entity/stage/property and leaves the revision unchanged, for every add kind | **Yes**, for all 12 name-introducing kinds plus `create_domain_session` (7,800 cases, 0 state changes). `add` introduces one name per call, so there is no multi-part add. |
| 3 | Property names allow only Text/Number/Boolean, exactly as the DSL does | The keyword set is right: only these three, via `primitive-name`. The DSL also requires a primitive type, and MCP does not check that (**F300**). |
| 4 | `IsValidPart` removed, `RequireValid` restored, `Lower` backstop, domain rule | **Yes.** `git diff 6dbab628 7fec146c -- ArtifactId.cs` is empty. `git grep IsValidPart` finds nothing anywhere. `Lower_DomainNameWithWhitespace_Throws` still passes (FormatException). `create_domain_session` equals the DSL `domain NAME` rule (identical over the corpus). |
| 5 | Tests not vacuous: red-before, mutations, message asserted, goldens | **Yes**, with gaps: F301 (flake), F303 (`_` at the start of a name is not pinned), F309. Red-before is 16 of 18. Every per-kind mutation goes red. The message is asserted. The `*.golden` diff is empty, and `POLY_UPDATE_GOLDEN=1` leaves the tree clean. |
| 6 | MCP mutation-path audit complete; contract bug | The audit is complete and correct: 24 tools enumerated. The contract `Success=false`-though-stored bug reproduces identically on master and head and is recorded accurately (F305, not held). The mill found a related pre-existing round-trip gap (F304). |
| 7 | Hand-editability gate | **NO** (F300 makes sentences false; F301). Otherwise the code is small and clear: one rule in `DslTokenReader`, one line per kind, no dead guards. One twin rule exists outside the diff (F304, pre-existing). |
| 8 | Principles / scope / PR body vs diff | Scope matches Foreman's description; there is no card (F308). Decision 11 (every mutation enforces every invariant) is advanced, and F300 is the remaining gap. The PR body is mostly accurate; corrections are in F300 and F306. |

## 1. Shape matrix, scanner and acceptance parity

### Scanner: differential proof that behavior is unchanged

- The diff only replaces the inline predicates with `IsWordStart(ch) => char.IsLetter(ch) || ch == '_'` and `IsWordPart(ch) => char.IsLetterOrDigit(ch) || ch == '_'`. The text is identical, and the `char.IsDigit → ScanNumber` branch still runs first.
- **Harness.** A scratch TUnit class tokenized a corpus with `new DslTokenReader(text)` until `EndOfFile` and wrote `input → token stream (kind:text@line:col) or exception` for each input. It ran in a master worktree and a head worktree. The corpus had **1,503,590 inputs**:
  - all 65,536 BMP chars in 13 contexts: alone, `a?`, `?a`, `a?b`, after a digit, inside a keyword, and so on (this covers unpaired surrogates, NBSP, ZWSP/ZWJ/ZWNJ, BOM, control chars, combining marks, the `-`, `.`, `/`, `#` and whitespace variants);
  - every supplementary code point from U+10000 to U+2FFFF, plus every 97th one above that;
  - the 42 keywords in case variants and with decorations;
  - 300k seeded random adversarial strings;
  - every sample `.poly` file.
- **Result.** The master and head outputs are byte-identical, sha256 `cc0ac04e…5cdf50`. Under `TZ=America/Los_Angeles LANG/LC_ALL=tr_TR.UTF-8` both runs are identical again, with the same hash. The keyword map (`WordToKind`, 42 entries, ordinal `switch`) is unchanged.
- The Parsing test namespace (151 tests) and `EmitGoldenTests` (21) pass under four environments.

### Predicate vs parser (head)

- `IsIdentifier` and `IsPropertyName` were compared with `new PolyDslParser(template, session).Parse()`. The corpus was **262,411 names**: every BMP char as `c`, `ac`, `ca` and `acb`, the keywords and their variants, and adversarial names. They were checked against 10 templates: domain, entity, property, stage, action, stage action, relationship (N1 nav), policy, value type and contract.
- The only divergences are names with leading or trailing whitespace such as `a\n` or `\ta`. The DSL treats that whitespace as a separator, while MCP refuses the padded string. That is an artefact of pasting the name into a template, not a real divergence.

### MCP `add` vs DSL, per kind (head)

- For each of the 12 name-introducing kinds plus `create_domain_session`, I compared "refused by the name rule" against the DSL template for that kind. The corpus was 600 names: the curated characters in four positions, all keywords in six forms, `Text`, `Number`, `Boolean`, `entry`, `when`, `transition`, `stage`, unicode letters and digits, surrogates, and so on. That is 7,800 cases.
- Divergences: none, apart from two artefacts:
  - blank and whitespace-only names are refused through the missing-field message rather than the name-rule message (both refuse);
  - padded names, as above.
- Matrix spot checks:

| Name | Every identifier kind and domain | Property |
|---|---|---|
| `Text`, `Number`, `Boolean` | DSL and MCP refuse | accepted |
| `entry`, `when`, `transition`, `stage` | refused everywhere | refused everywhere |
| `1x`, `a-b`, `a.b` | refused everywhere | refused everywhere |

### F300: property names depend on the type in the DSL, but not in MCP

- `PolyDslParser.cs:277-293` (grammar pattern `primitive-name`) accepts a property named `Text`, `Number` or `Boolean` only when the type after the colon is a primitive keyword or a known primitive name, such as `Date`.
- Observed on the head:

| Input | DSL parse | MCP `add property` | export → `apply_dsl` |
|---|---|---|---|
| `Text: Text`, `Text: Number`, `Text: Date` | accepts | accepts | round-trips |
| `Text: Money` (value type) | `Expected type after 'Text:', got 'Money'` | **accepts** | **Parse error** |
| `Text: Order` / `Text: C` (entity, N1 nav) | refuses | **accepts** | **Parse error** |
| `Number: many C` | refuses | n/a | n/a |

- **What this means.** MCP accepts a name the DSL refuses, which is the exact divergence the F263 rule forbids. Re-applying the export fails, so this is also the "simulate = print" failure that N5 exists to prevent.
- **False sentences:**
  - PR body matrix row 9: "`Text`, `Number`, `Boolean` as a property name | accepts".
  - The body's "so the DSL and the MCP cannot drift".
  - The `IsPropertyName` doc comment, "the parser also accepts those as a property name (`Number: Text`)", is true only for primitive types.
  - The `add` description: "a new name the DSL would not accept".
- **Fix.** When the name is a primitive keyword, also require `typeName` to be a primitive type: `Text`, `Number`, `Boolean`, or a primitive the session knows, which is the same set as the parser's `_primitiveTypeNames`. The simplest single-rule shape is a `DslTokenReader`/`DslGrammar` helper that takes the type, used by both the parser's `primitive-name` branch and MCP. Add tests:
  - `Text: Money` and `Text: Order` are refused through `add`;
  - `Text: Date` is accepted;
  - a parity test with the property template on a non-primitive type.

## 2. A refusal leaves no state

- **Name-introducing `add` kinds** (from the `add` switch, `DomainTools.cs:527-688`): entity, property, stage, action, stage_action, relationship, policy, value_type, contract, contract_value_type, contract_endpoint, contract_binding.
  - `constraint` has no name field: it takes `entityName`, `propertyName`, `type`, and `BuildConstraint` types Range/Required/Length/Pattern/Unique.
  - Each name check returns before `Evolve`, or before `AddPolicyCore` for policy.
  - `create_domain_session` checks before `McpSessionStore.Create`.
- **Probe.** For every refused case in the 7,800-case run, I compared the session `Revision` and `ReferenceEquals(Domain)` before and after the call: **0 changes**. The PR's own `Add_WithNameTheDslRefuses_ChangesNothing` covers all 12 kinds with 4 bad names each. `create_domain_session` returns `SessionId = null` and creates no session.
- **Multi-part add.** No `add` kind takes more than one new name (`action` has no parameters, and `contract_value_type` builds an empty `ValueType`), so there is no partial-add path. `Add_PropertyAndStage_..._LeaveTheEntityAsItWas` checks the entity afterwards.

## 3. Property name kinds

- The grammar accepts a property name if it is an `Identifier` or a primitive keyword (`Text`/`NumberType`/`BooleanType`, via `DslGrammar.IsPrimitiveTypeKind`). No other keyword is accepted.
- `IsPropertyName` matches that keyword set exactly; this is confirmed over the whole corpus.
- The missing piece is the type condition (F300).
- `Boolean` is accepted by the code, but the positive MCP test covers only `Text` and `Number` (F309).

## 4. `ArtifactId`, the `Lower` backstop and the domain-name rule

- `git diff 6dbab628 7fec146c -- Poly/DomainModeling/Compile/ArtifactId.cs` is **empty**: the file is byte-identical to the version before A3a.
- `git grep IsValidPart 7fec146c` finds nothing in code, tests, docs or comments.
- `LowerTreesTests.Lower_DomainNameWithWhitespace_Throws` (direct `Lower`, `Name = "My Domain"`) still throws `FormatException` and passes under all four environments.
- The body's claim "every DSL name satisfies the ArtifactId rule, so the backstop is unreachable for names that pass" holds. `IsWord` admits only `char.IsLetterOrDigit` or `_`, so there is no whitespace, `/` or `#`. The backstop is still there, as Scot asked.
- `create_domain_session` uses `IsIdentifier`, and the DSL's `domain NAME` uses `ExpectIdentifier`. They agree on all 600 names and on the predicate run over 262k names. Neither accepts a name the other refuses.

## 5. Tests and mutations

- **Red-before.** I put a trimmed copy of `McpNameRuleTests` on master `89098229`, leaving out the parity tests because `IsIdentifier` does not exist there: **16 of 18 fail**. The 2 that pass are the `Text`/`Number` positive controls. This matches the PR body.
- **Goldens.** `git diff --stat origin/master 7fec146c -- '*.golden'` is empty. A `POLY_UPDATE_GOLDEN=1` full run left `git status` clean.
- **Existing tests.** Only `UnifiedAddTests` changed, with two message assertions. That is justified because the message text changed, but the test names are now stale (F302).
- **Mutations.** These ran in a scratch worktree off the head and were never pushed. A run was counted red when tests other than the F301 flake failed. Filters: "MCP" means the `McpNameRuleTests` class; "full" means the whole suite.

| Mutation | Scope | Red |
|---|---|---|
| drop the name check, `entity` | MCP | 1 |
| drop, `property` | MCP | 2 |
| drop, `stage` | MCP | 2 |
| drop, `action` / `stage_action` / `relationship` / `policy` / `value_type` / `contract` / `contract_value_type` / `contract_endpoint` / `contract_binding` | MCP | 1 each |
| drop, `create_domain_session` | MCP | 3 |
| `IsIdentifier` accepts keywords | MCP / full | 32 / 32 |
| `IsPropertyName` = `IsIdentifier` | MCP | 5 |
| `IsPropertyName` accepts every keyword | MCP | 5 |
| helper `IsWord` accepts a digit first (scanner untouched) | MCP | 17 |
| helper `IsWord` accepts `-` (scanner untouched) | MCP | 17 |
| shared `IsWordStart` accepts a digit | full | 17 |
| shared `IsWordPart` accepts `-` | full | 13 |
| shared `IsWordStart` refuses `_` | full | **0 (F303)** |
| scanner reverted to the old inline predicates | full | 0 (expected: no behavior change, proven by the differential run) |
| error message text changed | MCP | 12 (+3) |

- PR-body mutation counts: "dropping the stage check, 3 red" and "keywords accepted, 34 red" are a little off from what I measured (2 and 32). The differences are within the flake's noise (F306, nit).

### F301: the new test is flaky, and CI is exposed

- `McpNameRuleTests.cs:59/66` reads the process-global `McpSessionStore.ListSessions().Count` before and after `CreateDomainSession`. TUnit runs tests in parallel by default, and the repo has no `[NotInParallel]`, `ParallelLimiter`, `.runsettings` or TUnit config. CI runs `dotnet run --project Poly.Tests/Poly.Tests.csproj`, so CI has the same parallelism. The PR's one green CI run was luck.
- Measured failure rates:

| Run | Runs with a failure |
|---|---|
| Full suite, head | 2 of 5 |
| Merged tree (master 076a9014 + PR) | 1 of 2 |
| Class alone, first sample | 2 of 5 |
| Class alone, later sample | 8 of 8 |
| Class alone, four-environment sweep | 6 of 8 |

- Every failure is `Expected to be N but found N+1`.
- **Verified fix** (scratch only, not pushed): drop the count and assert that no session carries the name:
  ```csharp
  var created = McpSessionStore.ListSessions()
      .Any(id => McpSessionStore.TryGet(id, out var state) && state.Domain.Name == name);
  await Assert.That(created).IsFalse();
  ```
  With the fix, 10 of 10 class runs were green. With `create_domain_session`'s check removed, the fixed test still goes red (3 tests).
- A new test that fails CI at random is a blocking defect.

## 6. MCP mutation-path audit

- **The tools.** `[McpServerTool]` gives 24 tools:
  - In `DomainTools.cs`: `create_domain_session`, `list_sessions`, `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_suggestions`, `get_relationships`, `add`, `remove`, `get_constraints`, `get_policy_expression`, `evaluate_policy`, `apply_dsl`, `export_dsl`, `get_dsl_guide`.
  - In `RuntimeTool.cs`: `create_instance`, `link_instances`, `unlink_instances`, `get_instance`, `list_instances`, `invoke_action`.
  - In `OracleTool.cs`: `export_domain_to_csharp`, `describe_domain_element`, `oracle_expression`.
- **Which tools change the model:** only `create_domain_session`, `add`, `remove` and `apply_dsl`. The PR body is right about this.
  - `remove` looks names up.
  - `apply_dsl` is the DSL itself.
  - The runtime tools change instances only.
  - `oracle_expression` builds a throwaway in-memory `Domain("Subject", …)` and does not touch any session.
- **Reference fields** are resolved against the domain, as the PR body says.
- **Contract `source`/`version`.** It is true that the DSL accepts any string (as a string literal). But the printer leaves `default` and `1` unquoted, so the export fails to re-apply. That is pre-existing (F304), not a name-rule divergence in `add`.
- **The contract bug (F305, not held).**
  - With a probe on both master `89098229` and the head, `add` of `contract`, `contract_value_type` and `contract_endpoint` each return `Success=false` with "No changes applied: target entity not found or change had no effect…". Yet the revision goes from 1 to 2 to 3 to 4 and the element is stored.
  - `contract_binding` behaves the same once a valid action with a parameter is set up through `apply_dsl` (revision 1 to 2, `bindings=1`).
  - The results are identical on both, so the bug is pre-existing and the PR body records it accurately. Foreman is carding it.

## 7. Hand-editability gate: NO

Could Scot open these files cold and change them by hand? Mostly yes:

- The rule lives in one place: `DslTokenReader.IsWordStart`, `IsWordPart`, `IsWord`, `IsIdentifier`, `IsPropertyName`, with `WordToKind` shared with the scanner.
- Each `add` kind has one readable check line, and there is one `InvalidName` helper.
- There are no dead guards or unreachable throws. `Field()` returns null for blank input, so the `MissingField` check and then the `IsIdentifier` check are both reachable.

The gate is NO because of the following.

- **False sentences.** These are all F300: matrix row 9, "cannot drift", the `IsPropertyName` comment's unconditional claim, and the `add` description's "a new name the DSL would not accept".
- **Flaky test.** F301.
- **Smaller wording issues:**
  - F302: test names say "ArtifactIdRule" for what is now the DSL rule.
  - F307: the message and the description say "not a DSL keyword", but property names accept three keywords.
  - F306: PR body row 11 says empty names were "refused only for entity and domain" before this PR. On master a blank name was already refused for every kind, through the missing-field check.
- **Twin rule outside the diff.** `DomainDslPrinter.NeedsQuotes` is a second identifier test, and it is weaker (F304, pre-existing). It is worth switching to `!DslTokenReader.IsIdentifier(value)` now that the helper exists.

## 8. Principles, scope, PR body

- **Decision 11** (every mutation enforces every invariant): this PR advances it for names. F300 is the remaining hole.
- **One rule / no twins:** this is the PR's own aim. It is met for identifiers, but the property rule still lacks the type condition, and the printer has a twin (F304).
- **Decision 1** (ids are name path plus type): unaffected, because `ArtifactId` was restored unchanged.
- **V1–V11, HELD:** nothing here touches held items. The diff is 5 files, +175/−22, which is in scope.
- **Plan:** there is no N5 card in the plan and the PR makes no plan edit (F308, Foreman housekeeping).
- **PR body vs diff:** accurate, except for F300 and F306.

## Suites

- **Head `7fec146c`, full suite (`-p:NuGetAudit=false`):** 3167 total, made up of master's 3073 plus 94 new tests. Two of five runs had a failure; every failure was F301. The other runs passed with 0 failures.
- **Master `89098229`:** 3073 total, 0 failed.
- **Merged tree** (`076a9014` + PR, `git merge --no-ff`): 3173 total. Run 1 had 3 failures, all F301. Run 2 had 0. That implies 3079 on the new master. The merge is clean (merge-tree rc 0).
- **New and affected classes, each run twice** under UTC, Asia/Tokyo + de_DE, America/Los_Angeles + tr_TR and invariant globalization. All pass except the F301 flake. There are no culture-specific failures.

| Class | Tests |
|---|---|
| `McpNameRuleTests` | 94 |
| `UnifiedAddTests` | 27 |
| `LowerTreesTests` | 9 |
| `EmitGoldenTests` | 21 |
| `Poly.Tests.DomainModeling.Parsing.*` | 151 |

- **Full suite under de_DE (Tokyo) and tr_TR (LA):**
  - head: 3167 total, 5 failed;
  - master: 3073 total, 5 failed.
  - In every case the failures are exactly the 5 known comma-decimal tests: `Export_RangeNegativeAndFractionalBounds_Parse`, `OpenRange_VerifiedEnvelope_KeepsBoundOpen`, `DecimalNumbers_Addition_ReturnsDouble`, `MixedTypes_ComplexExpression_PromotesCorrectly`, `MixedTypes_IntAndDouble_PromotesToDouble`. No new culture failure.
- **CI on the head:** "Build & test" passed once. Given F301, that was luck.

## Findings

| ID | Sev | Blocks | Where | What |
|---|---|---|---|---|
| F300 | bug | **yes** | `DomainTools.cs:541`; `DslTokenReader.IsPropertyName` and its doc comment; PR body row 9 and "cannot drift"; `add` description | MCP accepts property name `Text`, `Number` or `Boolean` with a non-primitive `typeName` (value type, entity). The DSL refuses these (`primitive-name`, `PolyDslParser.cs:277-293`), and export → `apply_dsl` fails. This breaks the one rule. Fix: make the property-name rule depend on the type, through one shared helper, and add tests (see §1). |
| F301 | bug | **yes** | `McpNameRuleTests.cs:59,66` | `CreateDomainSession_WithNameTheDslRefuses_CreatesNoSession` races parallel tests on the global session count and fails at random (see the rates in §5). CI is exposed. Fix (verified): assert that no session has `Domain.Name == name`. |
| F302 | N | no (fix with F300) | `UnifiedAddTests.cs:25,40` | The test names `…WithNameTheArtifactIdRuleRejects_Fails` are stale: these names are now refused by the DSL rule. Rename them, e.g. `…WithNameTheDslRefuses_Fails`. |
| F303 | S | no | tests | No test goes red when `IsWordStart` stops accepting `_`. The parity tests change on both sides at once, and no absolute test pins it. Add an absolute pin, e.g. `add entity "_x"` succeeds and `_x: entity {…}` parses. |
| F304 | S (pre-existing) | no, card | `DomainDslPrinter.cs:197-198` `NeedsQuotes` | A second, weaker identifier rule: it does not quote keywords or digit-first values. Contract `source "default"` or `version "1"` is exported unquoted and fails to re-apply ("Expected contract source/version"). Same on master. Fix: `NeedsQuotes = !DslTokenReader.IsIdentifier(value)`. |
| F305 | bug (pre-existing) | no, not held (Foreman cards it) | `add` contract kinds / the no-op guard | `contract`, `contract_value_type`, `contract_endpoint` and `contract_binding` return `Success=false` "No changes applied…" although the change is stored and the revision is incremented. Reproduced identically on master and head. The PR body records it accurately. |
| F306 | S (PR body) | fix with F300 | PR body row 11, Checks | Row 11 says the MCP "before" behavior refused `empty` "only for entity and domain". On master a blank name was already refused for every kind (missing field). The mutation counts are approximate (stage 3 vs the measured 2; keywords 34 vs 32). |
| F307 | N | no | `InvalidNameMessage`, `add` description | Both say "not a DSL keyword", but a property may be `Text`, `Number` or `Boolean`. Mention the property exception, together with the F300 type condition. |
| F308 | N | no (Foreman) | `pipeline-convergence-plan.md` | There is no N5 card or F263 entry, so the scope and done-when exist only in the assignment. Card it and mark it when merged. |
| F309 | N | no | `McpNameRuleTests.Add_Property_NamedAfterAPrimitiveKeyword_Succeeds` | It covers `Text` and `Number` but not `Boolean`. |
| F310 | N (pre-existing) | no | `add` `kind` parameter `[Description]` | It lists 8 kinds, but the tool takes 13 (the tool description and the error message list all 13). |

Totals: bugs from this PR 2 (F300, F301), both blocking. Pre-existing bug, not held: 1 (F305). Open: 11.

## Mill disposition (OpenCode `opencode-go/deepseek-v4.1-flash`, read-only throwaway snapshot)

| Mill # | Claim | Disposition |
|---|---|---|
| 1 | `add property` ignores the type; `Text: Money` is accepted by MCP and refused by the DSL | **Confirmed**, independently found by my probe → F300 |
| 2 | No other name divergence | Agrees with my harnesses |
| 3 | No nested name paths; runtime tools do not touch the model | Confirmed |
| A2 | Scanner refactor changes nothing | Confirmed by the differential run |
| A3 | No partial state | Confirmed |
| 4 | Stale `UnifiedAddTests` names | Confirmed → F302 (nit) |
| 5 | `NeedsQuotes` twin rule; contract source/version round-trip | Confirmed by probe on master and head → F304 (pre-existing) |
| 6 | Message and description say "not a DSL keyword" | Confirmed → F307 |
| 7 | "`RequireValid` back as it was" can't be verified | **Dropped**: the snapshot had no history; `git diff 6dbab628` is empty |
| 8 | Flaky session-count test | Confirmed → F301 |
| 9 | Parity tests never call `Add`; `Boolean` untested | The first part is true but harmless: the `Add_*` tests wire every kind, and every drop mutation goes red. The second part → F309 |

## What Scot needs to merge (after a fix round)

- **Rebase.** Master moved to `076a9014` (B1 #97). The merge is clean and the merged tree passes apart from F301. No rebase is required; a rebase on top of the fix round is fine.
- **Before merging, the fix round needs:**
  - F300: a property-name rule that depends on the type, plus tests;
  - F301: the verified test fix;
  - the stale names and wording: F302, F306, F307.
  - Optionally fold in F303 and F309.
- **Risk.** The change is low-risk. The scanner is proven identical, and `ArtifactId` is identical to the version before A3a. The one behavior change is that MCP clients can no longer create names that are digit-first, contain `-` or `.`, or are keywords. Any existing agent scripts that relied on that will now get the "is not a valid name" error. That is the intended behavior.
- **After merging, check:**
  - `git grep IsValidPart` is empty;
  - the suite count is master + new tests, with 0 failures over a few runs (flake gone);
  - `create_domain_session("1Domain")` is refused;
  - `add stage` named `stage` is refused;
  - `add property` `Text: Number` is accepted and `Text: Money` is refused (after F300);
  - `export_dsl` → `apply_dsl` round-trips on a mixed domain.
- **Plan housekeeping (Foreman):**
  - add the N5/F263 card (F308);
  - card F304 (printer quoting) and F305 (contract no-op guard);
  - note that F263 is closed once N5 merges.
