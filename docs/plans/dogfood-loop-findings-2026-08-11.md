# Dogfood-loop findings — 2026-08-11

Follow-ups surfaced by the exporter/runtime dogfood loop (IssueTracker, EdgeProbe,
Probe2, Probe3 domains). Not blocking — filed per the pre-ship review gate so the
uncommitted-change review can ship with 🟡 items tracked.

## Pilot round 1 (agent orchestration) — 2026-08-11

First orchestrated discovery run: 3 parallel agents (`general` subagents) on disjoint
slices (A: cross-entity/quantifiers, B: dates/defaults, C: constraints/create/enums),
probes in `probes/discovery-*/`, findings in `probes/findings/discovery-*.md`.
Protocol: [`docs/agent/poly-discovery-loop.md`](../../docs/agent/poly-discovery-loop.md).
Harness: `scripts/new-probe.sh`, `scripts/run-probe.sh` (parse → export → Roslyn
compile-check).

**Fixed in round 1 (all with regression tests, committed):**
- A-F1 multi-hop path-prefix nested nav PascalCase (was CS1061) + A-F4 null-forgiving
  path-prefix hops (CS8602) — `DomainExpressionLoweringPass.RelationshipNavigation`.
- A-F2 `Rel exists` on a `many` nav lowers to `Lines.Count != 0` (runtime parity; was
  always-true `!= null`).
- A-F3 quantified-invoke actions no longer emit unreachable `return Success()` (CS0162).
- C-F1 `create Type` defaulted-prop override flows through `Create(...)` (was CS0272
  private-setter assign; also fixes silent drop). Shared `AppendDefaultedPropArgs`.
- C-F2 string-literal enum members in create/create-in initializers qualify
  (`Kind: "Keyword"` → `TokenKind.Keyword`; was CS1503).
- B-F6 enum-member default on a non-enum property fails loud at export (was silently
  dropped / changed the Create signature).

**Deferred (filed below / `probes/findings/discovery-b.md`):**
- B-F1/B-F2 date arithmetic (`+`/`-`) not hoisted beyond the `assign` path (policies and
  create-in initializers emit raw `DateOnly ± long` CS0019); date subtraction never
  lowered. Fix = hoist AddDays rewrite into `DomainExpressionLoweringPass` + Subtract arm.
- B-F3/B-F4 runtime-default (`default(now/today/guid)`) and date-literal defaults lack
  property-type adaptation / analysis rejection (CS0019/CS1750).
- B-F5 policies cannot compare dates to `now`/`today` (analysis rejects) — guide-drift.
- C-F3 constraints validated only in the export's Create factory, never at runtime.
- C-F4 create/create-in inside a conditional silently dropped at runtime / `-> T` export
  always throws.
- C-F5 `unique` enforced nowhere (both paths).
- C-F6 negative/fractional range bounds unparseable.
- C-F7 multi-initializer create with a non-final bare-identifier value misparses.

## Pilot round 2 — 2026-08-11

Second orchestrated round (round2) via `scripts/discovery-round.sh round2` + 3 parallel
agents (findings in `probes/findings/round2/`, probes in `probes/agent-*/`).

**Fixed in round 2 (all with regression tests):**
- C-F4 wrong-stage stage-scoped action now reports "only available in stage 'X'" instead
  of the misleading "not found on entity" (runtime message parity with the export guard).
- C-F3 runtime `Create` applies the first stage's entry effects (export ctor parity) —
  transitions in initial entry effects are skipped, matching the export ctor.
- C-F1 VM string-concat arm for `"S" + Status` (runtime silently stored null; the
  LinqExpressions path already concat'd).
- B-F2 action parameters now resolve in create/create-in initializers at runtime (were
  garbage values compiled without the action-scoped analyzer).
- B-F9 `length(3, )` open upper bound no longer collapses to `length(3, 3)`; `length(, N)`
  open lower supported (mirrors range open bounds).
- B-F10 `pattern` on a non-Text property is now rejected at analysis (was a silent no-op).

**Deferred (filed in `probes/findings/round2/`):** date arithmetic hoist + subtraction
(🔴, high value), runtime-default type adaptation (🔴), date-literal defaults (🔴),
cross-type date ops analysis (🔴), `assign DateProp to now` (🔴), policy `now`/`today`
(🟠), create-Type runtime-keyword default crash (🟡), `now`/`today`/`guid` as non-final
initializer values (🟡), non-member enum initializer analysis (🔴), unbound no-default
prop divergence (🟠), conditional create (🟠), `unique` (🟠), signed/fractional ranges
(🟡), multi-initializer parse (🟡), nested `transition to` in entry effects (🟠).

## Pilot round 3 (adversarial "silly things") — 2026-08-11

Third round via `scripts/discovery-round.sh round3`: agents instructed to DELIBERATELY
try degenerate/contradictory models (A), cross-type abuse (B), and control-flow
pathologies (C). Findings in `probes/findings/round3/`, probes in `probes/agent-*/`.

**Fixed in round 3:**
- C4/agent-a F8: a `transition to` nested inside an entry effect no longer gets
  overwritten by the outer stage-set in the export — `CurrentStage` is set before the
  target's entry effects run (matching the runtime TransitionStage). Final stage now
  agrees between export and runtime.

**Headline finding (filed, not fixed — systemic):** the DSL has **no type-compatibility
check in parse or analysis**. Wrong-typed comparisons/assigns/arithmetic/defaults pass
analysis; the export then compile-fails (CSxxxx) while the runtime silently accepts and
coerces garbage (agent-b F1–F3, F5–F12). This is the highest-value architectural gap:
a type-compatibility analyzer pass would reject the whole class at authoring time.
Sub-cases include a VM coercion that maps any string/Date → the constant `2` when read
as a Number, and the `Name == null` → always-false behavior caused by the deliberate
null→"" Text coercion (G-S6-1 `Name exists` contract) — a documented design tension.

**Other filed round-3 findings (repros in `probes/findings/round3/`):**
- A: same-name actions across stages silently merge bodies at runtime + CS0111 in the
  export (needs the entity-action fallthrough nuance — empty stage-copy fallthrough is
  supported); action/policy name collision passes analysis; self-`many`/to-one navs
  break the export (CS1503 / nonexistent CreateParent); default violating its own
  range accepted; always-false entity-level policy silently gates every action;
  duplicate stage names crash opaquely.
- B: `Name + 5` compiles 0/0 but runtime drops the numeric operand; non-member enum
  strings accepted at runtime; date-arithmetic opaque casts; negative literals
  unparseable; `assign Name to true` stores the whole bag.
- C: `if` blocks silently drop transition/invoke/create-in at runtime (only assign
  runs) — the runtime Conditional turns non-assign sub-effects into no-ops; same-stage
  and chained transitions diverge in the export; recursive invoke has no export depth
  guard (StackOverflow, guide says "cycles fail loud"); invoke with missing args
  silently stores `System.Reflection.Missing`; self-invoke passing an action param
  stores a garbage `1`; duplicate subscriptions emit CS0111.

## Filed-items round (2026-08-11) — fixed from the backlog

Worked through the filed findings; four fixed with regression tests, two investigated
and re-filed with the reason they were not changed:

**Fixed:**
- **Date arithmetic hoist + subtraction** (B-F1/B-F2, A-F1/F2): the AddDays rewrite
  moved from the assign-only path into `DomainExpressionLoweringPass.Add/Subtract`, so
  policies, if conditions, entry/exit, and create-in initializers lower `DueDate + 14`
  / `DueDate - 14` → `AddDays(14)` / `AddDays(-14)` (int-cast for DateOnly). Probes
  `loans.poly`/`bookings.poly` now compile 0/0.
- **Runtime constraint validation** (C-F3, B-F3): `DomainEntityInstance.Create` now
  enforces required/range/length/pattern (mirroring the export's Create-factory
  guards) and fails loud; the runtime no longer silently accepts invalid instances.
- **Negative/fractional range bounds** (C-F6, B-F7): `range(-500, )` and
  `range(0.01, 1.0)` now parse (tokenizer scans `.`; the range grammar accepts a
  signed bound).
- **Conditional/if runtime effect drop** (C-F4, B-F5, round-3 C1): composites and
  conditionals containing transition/invoke/create/create-in sub-effects now run each
  sub-effect through the dispatcher instead of the VM path that silently lowered them
  to no-op Comments. `if (rush) { create in ... }` and `if (Flag) { transition }` now
  execute correctly at runtime.

**Investigated, re-filed (not changed — relied-on lenient/valid behavior):**
- Invoke with missing args: the runtime intentionally allows unbound action params
  (fallthrough tests depend on it); the real bug is a *referenced* missing param being
  read as `System.Reflection.Missing` and stored — needs a referenced-param check, not
  an unconditional missing-arg rejection.
- Duplicate subscriptions: the stage-scoped + entity-level combination for the same
  Rel+Stage is a documented supported placement; a blanket duplicate-handler error
  broke it. Needs export-side handler dedup or a narrower same-scope check.

**Still open (filed with repros):** the systemic type-compatibility gap (agent-b
headline), same-name actions across stages (needs the fallthrough nuance), self-navs in
the export, default-vs-range contradictions, same-stage/chained transition export
ordering, recursive-invoke export depth guard, `Name == null` (G-S6-1 null→"" tension),
`Name + 5` string-number concat divergence, non-member enum strings at runtime,
`now`/`today`/`guid` in policy bodies and as non-final initializer values, `unique`,
date-literal defaults, cross-type date ops.

## Round 4 (uutils-style utilities) — 2026-08-11

Fourth round via `scripts/discovery-round.sh round4`: agents modeled real-world common
utilities (coreutils / textutils / findutils). Findings in `probes/findings/round4/`,
probes in `probes/agent-ut-*/`. Note: agents A and B returned empty reports (context
exhaustion) — their findings were recovered from the probes they left.

**Fixed:**
- `create in { Prop: param … }` with multiple bare-identifier (action-param) values
  misparsed as a path-prefix (`newName.Content` → "Expected property name, got ':'") —
  the common shape for copying args into a created child. Fixed via an
  `InPropertyInitializerValue` cursor mode that stops path continuation at an
  `Identifier :` boundary. `minicopy.poly` now compiles 0/0.

**New findings filed:**
- 🟠 property names that camelCase to a C# reserved keyword (`Protected` → `protected`)
  break the export with a CS1001 cascade and no analysis rejection — needs escape-or-
  reject decision.
- 🟡 action parameters cannot have defaults (`default` reserved) — modeling friction.
- 🟠 self-relationship `invoke` (find's recursive traversal) is rejected — surface gap
  for recursion (fail-loud, but find can't express `-exec` traversal naturally).

**Re-confirmed (already filed):** self-many nav CS1503, entity-policy gating (guide
documents `require` only), quantified-invoke export dead-end + CS0162 tail, `pattern`
write-time-only (grep read-filter shift), no glob/regex operator (find `-name '*.c'`),
no-op-on-empty semantics.

**Verified-clean:** ls (sort options as enums), chmod (mode as Number range 0-511 with
default), touch/mkdir/cp encode cleanly — utilities with option enums + constrained
scalars + lifecycle stages map naturally.

## Fail-closed retrofit (moves 1–4) — 2026-08-11

Four strategic moves to satisfy the majority of open findings at once, per the
"fail-closed by default" analysis:

**Move 1 — type-compatibility analysis (`ExpressionTypeAnalyzer`).** The DSL previously
had no type check in parse or analysis; wrong-typed comparisons/assigns/arithmetic/
defaults passed analysis, the export compile-failed, and the runtime silently coerced.
The new pass rejects the agent-B class at authoring time: incompatible comparison
operands, `not`/`and`/`or` on non-Boolean, non-numeric arithmetic, assign RHS vs target
property type, wrong-typed `default(...)` (incl. `now`/`today`/`guid` on the wrong
property type), and enum-member validity for string literals. All three round-3
agent-b probes now fail at analysis.

**Move 1b — interpretation-layer type check (`SyntaxTypeCompatibilityAnalyzer`).** The
interpretation pipeline (the `AnalyzerBuilder` used by the VM) resolved types but never
validated compatibility — `Name >= 18` sailed through, the VM coerced, the C# compiler
was the first rejector. A new analyzer on the lowered Syntax AST reports the same class
at VM-compile time (Text vs Number comparison, non-numeric arithmetic, non-Boolean
`not`), and the DSL runtime paths (`EvaluatePolicy`, effect execution) compile via
`Interpreter.CompileChecked`, which fails loud on those errors. The raw `Compile` stays
lenient for direct VM/robustness callers. This closes the three-layer defense:
DSL authoring rejects → interpretation/VM compile rejects → runtime coercion fails loud.

**Move 2 — runtime fail-loud on coercion (`GuardCompatible` in `CoerceRead`).** The VM
member read no longer silently mangles wrong-typed raw values (`Convert.ToInt64(true)`
→ 1, number in a Text prop). Fundamentally wrong types now throw a clear
`InvalidOperationException`; null → default coercion (the G-S6-1 `Name exists` contract)
is preserved.

**Move 3 — structural-invariant checks.** Action/policy name collision is rejected;
a literal `default` violating its own `range`/`length` is rejected; duplicate stage names
fail loud at parse (was an opaque catalog `ArgumentException`). Same-name actions across
stages were investigated and found to be a *supported, tested* pattern (resolved by
current stage) — the round-3 "merge" finding did not hold up and is not rejected.

**Move 4 — shipped-surface narrowing (documented in the DSL guide).** `unique` is
declared storage-projection metadata (not a runtime invariant); `now`/`today`/`guid` are
excluded from policy bodies; `pattern` is declared write-time validation (not a read
filter); relative date ordering is unsupported; and expression type-checking is
documented as enforced.

**Open backlog after moves 1–4:** the residual needs genuine feature work or decisions —
date-ordering comparisons, `pattern` read filters (grep), `now`/`today` in policies,
action-param defaults, reserved-keyword escaping (escape vs reject), `Name == null`
(G-S6-1 null→"" tension), self-nav export codegen (trees are legitimate), quantified-
invoke `-> T` export tail, same-stage/chained-transition export ordering, recursive-
invoke export depth guard.

## Export / runtime parity notes

- **🟡 Stage-scoped action not-found message is misleading.** Invoking a stage-scoped
  action from a different stage reports `"Action 'X' not found on entity 'Y'"` even
  though the action exists on another stage. Prefer `"not available in stage 'S'"`.
  (RuntimeTool / DomainEntityInstance action resolution.)
- **🟡 Entry/exit effects stomp the action's own property assigns.** An action's
  `assign` is overwritten by the source stage's exit effects (and the target stage's
  entry effects run unconditionally). Faithful codegen — e.g. `Boot`'s conditional
  result is always overwritten by `Active.entry`. Consider a DSL-guide note.
- **🟡 Export fills unspecified create-in props with CLR defaults.** `create in` with
  an unset non-defaulted prop emits `""`/`0`/`null` in the export; the runtime leaves
  them `null`. Minor, both "unset", but a parity delta.
- **🟡 Cross-entity invoke: property vs store link.** The export calls the nav
  *property* (`this.Assignee!.Notify()`); the runtime resolves the *store link*
  (`ResolveRelationshipTarget`, throws if unlinked). An issue created with `assignee`
  set but never linked succeeds in the export but throws at runtime (and vice versa).
- **🟡 Store-dependent entity-level policies make exported actions throw.** Every
  entity policy gates every exported action; a store-dependent policy (any/all/count)
  throws `NotSupportedException` when evaluated, so actions on such entities cannot
  run in the export. Fail-loud and honest, but sharp — worth a guide note.

## Runtime VM gaps

- **🟡 `now` / `today` / `guid` inside runtime effect bodies** lower to phantom
  property reads (`Member(subject, "now")`); they only work as `default(...)` values
  and in the export. Runtime effects cannot author these today.
- **🟡 Date-typed ordered comparisons unsupported.** Non-numeric value types are now
  boxed into the VM heap (2026-08-11); `==`/`!=` go through object-Equals, but
  `>`/`<` on DateTime/DateOnly compare heap handles (garbage), same as strings.
  Date ordering is not a shipped surface.

## Follow-up candidates (not scoped)

- MCP restart still needs a manual `/mcp` reconnect after `scripts/restart-poly-mcp.sh`
  (opencode does not auto-restart a killed local MCP mid-session). A persistent stdio
  wrapper that re-execs the server in place would close the last manual step.
