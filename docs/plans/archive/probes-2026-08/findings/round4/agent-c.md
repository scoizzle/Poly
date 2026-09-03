# Round 4 findings — agent-ut-c (COMMON UTILITIES: find + grep family, uutils-style)

Agent: `agent-ut-c`. Protocol: [`docs/agent/poly-discovery-loop.md`](../../../docs/agent/poly-discovery-loop.md).
Round: 4 (findings to `probes/findings/round4/agent-c.md`).
Slice: model real-world COMMON UTILITIES as Poly domains, uutils-style (findutils + grep-family).
Utilities modeled: **find**, **grep**, **sed**, **awk**.

Probes (new, round 4) under `probes/agent-ut-c/`:
- `find.poly` — GNU `find`: recursive traversal as a self-referencing tree (`children: many FsNode`,
  `parent: FsNode`), predicates as policies (`-name`→policy, `-size`→range, `-type`→enum,
  `-mtime`→threshold, negation), `-exec`→cross-entity invoke.
- `find-encoded.poly` — Directory/File split workaround for the traversal (compile-fail — see F1).
- `grep.poly` — GNU `grep`: lines as entities, match patterns as policies, exit-status
  quantifier policies (`count/any/none`), `-c`-style counts, scan as cross-entity invoke.
- `grep-pattern.poly` — grep's match pattern expressed as a `pattern(regex)` **constraint**.
- `sed.poly` — GNU `sed`: editing pipeline as stages (`Raw`→`Edited`), line-address ranges as
  `where LineNo >= 1 and LineNo <= 3` filters, `/pattern/d` as filtered invoke.
- `awk.poly` — awk: input records with field properties (`Field1..Field3`, `FieldCount`),
  accumulation actions, cross-entity invoke over the stream.

Probe status through `scripts/run-probe.sh`:
`grep.poly` 0/0 PASS · `grep-pattern.poly` 0/0 PASS · `awk.poly` 0/0 PASS ·
`sed.poly` 0/1 **PASS-by-exit but CS0162 warning** · `find-encoded.poly` **CS1503 compile-fail** ·
`find.poly` **analysis reject** (self-relationship invoke).

Runtime evidence: via MCP tools (own `sessionId`s `159c2ff8…`, `d0e60c39…`, `10dcc061…`,
`c9be61e3…`, `64c44176…` — create_instance / link_instances / invoke_action / evaluate_policy /
simulate_policy; same runtime path as `McpSmokeTests`). No source edited, no commits.

---

## F1 — Self-referencing `many`/`one` navigation: analysis ACCEPTS the declaration, export emits CS1503 (broken `Create(name, this)` into an `IEnumerable<>` param)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** find (recursive traversal as self-referencing tree)
- **Repro:** `probes/agent-ut-c/find-encoded.poly` and minimal
  `Directory: entity { Name: Text required; subdirs: many Directory }` — the self-`many`
  declaration alone triggers:
  ```
  error CS1503: Argument 3: cannot convert from 'Directory' to 'System.Collections.Generic.IEnumerable<Directory>'
  ```
  Export emits the per-nav factory:
  ```csharp
  private Directory CreateSubdirs(string name) {
      var directoryResult = Directory.Create(name, this);   // 'this' (a Directory) into IEnumerable<Directory>
      ...
  }
  ```
  Same for a self-`one` (`parent: Node`) plus `children: many Node`:
  `Node.Create(name, this, this)` → CS1503 on the `IEnumerable<Node>` param.
- **Expected:** find's recursive tree is the *defining* shape of the utility (directory →
  files + subdirectories). The DSL guide documents navigation properties, including "many"
  and "one", and only rejects **invoke/quantifier** on self-relationships (`DMEFF007`) — the
  *declaration* itself is not documented as rejected, and analysis passes it with no diagnostic.
  Either the declaration must be rejected at analysis with a clear message, or the export must
  compile (the auto-wire of the child's back-reference is the offender — for a self-relationship
  the "back-ref" IS the collection, and the exporter passes `this` instead of wrapping it).
- **Actual:** analysis accepts (0 diagnostics), export generates uncompilable C# → the whole
  find-traversal modeling surface is a compile-fail. Minimal repro compiles-fails on just the
  nav declaration with zero actions.
- **Proposed patch (not applied):** in the exporter's create-factory / back-reference auto-wire,
  when source and target entity types are identical, do not pass `this` into a collection
  parameter (either wrap as `new List<Directory> { this }` when semantically the singular
  back-ref, or omit); or emit an analysis diagnostic "self-referencing navigation is not
  supported" at declaration time.

## F2 — Entity-level policies silently gate EVERY action (guide documents only `require`); grep's exit-status policies make `Scan` permanently un-runnable on both paths
- **Signal:** guide-drift / modeling-trap (runtime + export agree, but the guide's `require`-only
  contract does not)
- **Severity:** 🟠
- **Slice:** grep / sed (exit-status policies, edit-pipeline guards)
- **Repro:** `probes/agent-ut-c/grep.poly` — `File` declares four quantifier policies
  (`IsMatch: count lines where Matched is true > 0`, `IsNoMatch: … == 0`, `AnyMatch`, `NoMatch`).
  - Export: `Scan()` begins with `if (!this.IsMatch()) return Failure("'Scan' blocked by policy 'IsMatch'.");`
    … and the same for ALL FOUR policies — none of which any action `require`s.
  - Runtime (MCP `invoke_action Scan`): `blocked by guards: IsMatch, AnyMatch`.
  - `IsMatch` and `IsNoMatch` are mutually exclusive, so **`Scan` can never succeed on any path**
    (when no line matches, `IsMatch` blocks; when a line matches, `IsNoMatch` blocks). grep's own
    scan loop is dead.
- **Expected:** the guide (§6, §11) presents `require PolicyName` as the ONLY action-gating
  mechanism ("Require gates reference named policies defined on the entity"). An entity-level
  policy is a reusable *expression*, not an implicit action precondition — a grep model should be
  able to declare `IsMatch`/`IsNoMatch` as query predicates without every action on `File`
  becoming dead. The taxonomy in the protocol lists "entity-level policies gating every action"
  as a known modeling trap, but the guide does not document it, and the grep case sharpens it:
  complementary exit-status policies (the natural encoding of grep's exit 0/1) make the action
  unconditionally un-runnable on both export and runtime.
- **Actual:** every action on an entity with ≥1 entity-level policy is guarded by ALL of them
  (export + runtime agree); `grep.Scan` is blocked before it can mark any line. This is not a
  silent wrong result — it fails loud — but it silently *redefines the DSL contract* the guide
  states and dead-ends the utility's core action.
- **Proposed patch (not applied):** gate only explicitly `require`d policies (per the guide), or
  document entity-level-policies-gate-every-action prominently, or add an analysis diagnostic when
  an entity has mutually-exclusive policies that can never all pass on any input.

## F3 — Shipped cross-entity invoke (`invoke [any|all] Rel.Action [where …]`) ALWAYS dead-ends in the export: `throw new NotSupportedException` replaces the body; a following `transition` becomes unreachable (CS0162)
- **Signal:** guide-drift (guide ships it; export cannot compile it) + fail-loud-but-sharp
- **Severity:** 🟠
- **Slice:** all four utilities (find `-exec`, grep scan, sed pipeline, awk accumulation)
- **Repro:** `probes/agent-ut-c/sed.poly` — `ApplyEdit` (shipped form
  `invoke all lines.SubstituteText(text: "edited") where LineNo >= 1 and LineNo <= 3` then
  `transition to Edited`). Export:
  ```csharp
  public DomainResult ApplyEdit() {
      ...
      throw new NotSupportedException("invoke all lines.SubstituteText requires store-aware evaluation and cannot be compiled to standalone C#.");
      this.CurrentStage = DocumentStage.Edited;   // CS0162: unreachable
      return DomainResult.Success();
  }
  ```
  `grep.poly` `Scan`, `awk.poly` `SumAll`, `find-encoded.poly` `Prune` all export the same
  bare `throw` for their `invoke [any|all] Rel.Action` (with and without `where`).
  `sed.poly` additionally trips the 0-warning gate (CS0162), since a `transition` follows the
  throw. Only OneToOne invoke exports as a real call (`this.Worker!.Process();`).
- **Expected:** the guide documents `invoke [any|all] RelName.Action [where expr]` as shipped
  surface (§6 effect table, §9 summary, with the `where` examples) and never states it cannot
  compile to standalone C#. The runtime DOES execute it (verified: `SubstituteRange` → line 1
  Body became "bar", line 4 untouched; zero-match `all`/`any` fail loud as documented). The
  export should either compile the fan-out or the guide must state the construct is
  runtime-only and analysis should reject it for `--mode entities`.
- **Actual:** the DSL accepts it, the runtime runs it, but the exported method always throws.
  When the invoke is not the final statement (the common pipeline shape: invoke then transition),
  the following effects are emitted as unreachable code → CS0162 warning → the 0-warning gate
  fails on shipped surface. Note: discovery-a F3 (same class, trailing `return Success()` after
  the throw) was fixed for the tail-return case; this probe shows the throw still breaks when a
  *transition/assign* follows the invoke.
- **Proposed patch (not applied):** for quantified invoke, either (a) emit a store-aware loop in
  standalone C# (mirror runtime semantics: `all`→fail on zero matches, `any`→first success) or
  (b) when the final lowered statement is a `ThrowStatement`, stop emitting subsequent effects
  and (c) document in the guide that quantified cross-entity invoke is runtime-only in this
  phase; analysis could reject it in export mode to fail loud at DSL time.

## F4 — `pattern(regex)` is a WRITE-TIME constraint (rejects non-matching lines at `create_instance`); grep's match is a READ-TIME filter — no expression-level regex/substring match exists, so the match predicate must be precomputed
- **Signal:** modeling-trap / modeling-friction (silent semantics shift) 🟠 (forces a wrong
  encoding) — see also F8 (no regex operator in expressions).
- **Severity:** 🟠
- **Slice:** grep (match patterns as constraints)
- **Repro:** `probes/agent-ut-c/grep-pattern.poly` — `Body: Text pattern("grep")`.
  - Runtime (MCP `create_instance`): `Body="no-match-here"` → **create FAILS**
    ("does not match the required pattern"); only matching lines can ever be stored.
  - Export: `Create` factory emits `Regex.IsMatch(body, "grep")` → Failure on non-match (same).
- **Expected:** grep does not reject a file for containing non-matching lines — it *filters at
  read time* and the file still holds every line. A faithful model must store all lines and
  evaluate "does this line match" as a query/predicate, not as a write-time gate. Using
  `pattern()` as the match mechanism silently turns a filter into a validator: files with any
  non-matching line become un-modelable.
- **Actual:** `pattern` is enforced in the Create path (export + runtime agree) — semantically
  `grep -n` over a file that "contains only matches". The DSL offers no alternative:
  expressions have `==`/`is` (exact equality only) — see F8 — so the only faithful grep needs
  an `IsMatch` boolean written by an external writer, which the DSL cannot do on existing
  instances either (no bulk-edit effect).
- **Proposed patch (not applied):** either document `pattern` as a write-time validation
  constraint (it already is, per §3) and clarify grep modeling needs an external matcher, or
  add a substring/regex operator to expressions (see F8).

## F5 — No glob/negation/regex predicate operators in policy expressions: find `-name "*.c"` and `! -name` cannot be faithfully encoded
- **Signal:** modeling-friction (forces a silent/wrong encoding) 🟠
- **Severity:** 🟠
- **Slice:** find (predicates as policies)
- **Repro:** `probes/agent-ut-c/find.poly` / `find-encoded.poly` — `IsSource: policy { Name is "*.c" }`.
  `simulate_policy("Name is \"*.c\"", {"Name":"main.c"})` → **false**; `"Name is \"main.c\""` → true.
  `Name is "*.c"` is exact equality, so find's shell-glob `-name '*.c'` matches only a file
  literally named `*.c` — a **silent wrong result** (compiles 0/0).
  Negation: `not (Name is ".git")` works with parentheses, but `not Status is "active"` is a
  **parse error** ("Expected RBrace, got 'is'") — `not` only binds a primary, not a comparison,
  so every negated predicate must be parenthesized (`not (… is …)`), which the guide's
  precedence table (`not` above `and`/`or`, below comparisons) does not make obvious.
- **Expected:** find's predicate language (`-name` glob, `! expr` negation, `-newer`, etc.) is
  the utility's core; a faithful model must express "name matches pattern" and "not (predicate)".
  Exact-equality `is` silently changes glob→equality; bare `not` over a comparison won't parse.
- **Actual:** glob is silently downgraded to exact match (no error, wrong result); negated
  comparisons require mandatory parentheses (fail-loud but surprising). Combined with F8
  (no regex in expressions) and F7 (no dates), find's predicate surface has no faithful encoding.
- **Proposed patch (not applied):** document these gaps in the guide (glob→equality is silent,
  so it should at least be flagged); consider allowing `not` over comparisons, or add a
  `matches`/glob operator.

## F6 — `invoke all Rel.Action where …` with zero matches fails loud (runtime, documented no-vacuous-`all`); sed's `/pat/d` no-op case has NO faithful encoding (`any` also fails on zero matches)
- **Signal:** modeling-friction 🟡 (surface gap — forces a guarded workaround, not a silent wrong value)
- **Severity:** 🟡
- **Slice:** sed (line-address commands)
- **Repro:** `probes/agent-ut-c/sed.poly` `DeleteBadNoMatch` — `invoke all lines.Delete where Body is "baz"` with no `baz` line:
  - Runtime (MCP): `invoke all 'lines.Delete' matched zero targets after where filter` (fail loud — matches guide's "zero matches fail").
  - `invoke any lines.Delete where …` with zero matches: **also fails** (`invoke any … matched zero targets`), despite the guide only stating the no-vacuous rule for `all`.
- **Expected:** in sed, `/baz/d` when no line matches is a perfectly normal **no-op** (success, exit 0). A faithful sed model needs "if nothing matches, do nothing and succeed". The guide's empty-set semantics for `all` is fail-closed (correct for its contract), but there is no shipped form that gives sed's no-op-on-empty behavior — the author must hand-guard with `if (count … > 0)`, and `invoke any` cannot be used as the "maybe zero" escape hatch because it too fails.
- **Actual:** every attempt to run a sed delete with an empty match set fails loud; there is no `invoke … where` that tolerates an empty result. Modeling-friction: the utility's normal case (no matching lines) is a hard failure.
- **Proposed patch (not applied):** document that `invoke any/all … where` both fail on empty, and consider an optional no-op-on-empty form, or a `require`-free conditional that guards on quantifiers.

## F7 — No date operations: find's `-mtime`, `-atime`, `-newer` cannot be modeled (mtime must be a precomputed number)
- **Signal:** modeling-friction 🟡 (surface gap — guide §8 already lists "Date operations" as not shipped)
- **Severity:** 🟡
- **Slice:** find (`-mtime` thresholds)
- **Repro:** `probes/agent-ut-c/find.poly` / `find-encoded.poly` — `IsRecent: policy { ModifiedDaysAgo <= 7 }`.
  Compiles and runs, but `ModifiedDaysAgo` must be a `Number` written by an external agent;
  there is no way to compute "modified within 7 days" from a timestamp, no `now`, no date
  comparisons. `find -mtime -7` against a real `DateTime` property is un-authorable.
- **Expected:** find's time thresholds are date-based; a faithful model wants
  `LastModified: Date` + `LastModified >= now - 7 days`.
- **Actual:** only the precomputed-days workaround exists (documented in the guide as not-yet-shipped). Honest gap, but it forces the author to choose between an external writer and a wrong encoding.
- **Proposed patch (not applied):** none (already a known not-shipped feature) — recorded for the utility slice as a friction point.

## F8 — No regex/substring operator in policy expressions: `is`/`==` are exact-equality only; grep's default unanchored regex cannot be expressed
- **Signal:** modeling-friction 🟠 (forces a silent/wrong encoding)
- **Severity:** 🟠
- **Slice:** grep (match patterns as policies) — also sed `s///` and find `-name`/`-regex`
- **Repro:** `probes/agent-ut-c/grep.poly` — `Evaluate: action { assign Matched to Body is "grep" }`.
  Export: `this.Matched = this.Body == "grep";` — exact equality. grep's `grep 'grep'` matches
  *any line containing* "grep"; `simulate_policy` with a line `"say grep me"` returns false.
  The guide's expression grammar (§8) lists comparison (`==`,`is`,…) but no `contains`, no
  `matches`, no regex — the only regex surface is the `pattern()` constraint (write-time, F4).
- **Expected:** grep's core is a regex match over each line's content; the DSL needs a
  `Body contains "grep"` or `matches` operator (or regex literals) for the match predicate.
- **Actual:** the natural `is "pattern"` is exact equality (silent wrong result); regex exists
  only as a write-time constraint. grep exit-status and sed address-by-regex (`/re/d`) are
  unexpressible as predicates.
- **Proposed patch (not applied):** add a shipped `matches`/substring operator to the expression
  grammar (the runtime VM already evaluates regex elsewhere); or document that match predicates
  require precomputed booleans.

## Verified-clean (no finding) on the utility slice
- **`is` string-literal comparison for enums** (`Kind is "Directory"`) compiles 0/0 and evaluates
  correctly (string form is the shipped enum-compare surface; bare member names in policy
  position are correctly rejected as "property does not exist").
- **Size range / mtime-threshold predicates** (`SizeBytes > 1024`, `SizeBytes < 100`,
  `ModifiedDaysAgo <= 7`) export and run correctly on both paths (find-encoded policies — but
  see F1: that file's self-rel factory breaks compilation, so policy correctness is via
  `simulate_policy`).
- **Address-range invoke at runtime** (`invoke all lines.SubstituteText where LineNo >= 1 and LineNo <= 3`)
  fans out correctly: line 1 edited, line 4 untouched (MCP `SubstituteRange`).
- **Quantifier exit-status policies** (`count/any/none`) evaluate correctly against linked
  instances at runtime (MCP `evaluate_policy`: `IsMatch`=false, `IsNoMatch`=true, `NoMatch`=true
  with zero matching lines) — matches guide §8 empty-set table.
- **`awk.poly` field/accumulate model** compiles 0/0 and `invoke all records.Accumulate(delta: 1)`
  exports (as a throw — see F3), actions on `Record` itself export fine.

---

## Final report (ranked)

1. `[🔴] compile-fail: Self-referencing `many`/`one` nav (find's recursive tree) accepted by analysis, export emits CS1503 `Create(name, this)` into an `IEnumerable<>` param — `Directory: entity { subdirs: many Directory }` alone fails (find-encoded.poly); expected a clean export or an analysis-time rejection`
2. `[🟠] guide-drift/trap: Entity-level policies silently gate every action (guide documents `require` only); grep's mutually-exclusive IsMatch/IsNoMatch make `Scan` un-runnable on both paths — `invoke_action Scan` → "blocked by guards: IsMatch, AnyMatch"; export inlines all four policy guards`
3. `[🟠] guide-drift: Shipped cross-entity invoke `invoke [any|all] Rel.Action [where …]` ALWAYS dead-ends in export as `throw new NotSupportedException`; trailing `transition` becomes unreachable → CS0162 0-warning-gate failure (sed ApplyEdit); runtime executes it correctly`
4. `[🟠] modeling-friction: `pattern(regex)` is write-time-only (create rejects non-matching lines), but grep's match is a read-time filter — no faithful encoding; grep-pattern.poly create on a non-matching line fails`
5. `[🟠] modeling-friction: No glob/regex in expressions — `Name is "*.c"` is exact equality (simulate_policy false for main.c); `not Status is "active"` is a parse error (parens required)`
6. `[🟠] modeling-friction: No regex/substring operator — grep `assign Matched to Body is "grep"` compiles to `Body == "grep"` (exact), unanchored grep match unexpressible (F4/F8 family)`
7. `[🟡] modeling-friction: sed no-op-on-empty (/pat/d) has no encoding — `invoke all/any … where` both fail loud on zero matches (runtime), while sed treats no-match as success exit 0`
8. `[🟡] modeling-friction: No date operations — find `-mtime`/`-newer` only as precomputed Number days (documented not-shipped; forces external writer)`

Paths:
- Findings: `probes/findings/round4/agent-c.md`
- Probes: `probes/agent-ut-c/{find,find-encoded,grep,grep-pattern,sed,awk}.poly`
