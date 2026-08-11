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
