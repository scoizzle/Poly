# Dogfood-loop findings — 2026-08-11

Follow-ups surfaced by the exporter/runtime dogfood loop (IssueTracker, EdgeProbe,
Probe2, Probe3 domains). Not blocking — filed per the pre-ship review gate so the
uncommitted-change review can ship with 🟡 items tracked.

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
