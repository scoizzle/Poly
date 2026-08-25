# Round-4 findings — agent-b (textutils)

Agent-b's report was lost; the probes were not created in the workspace, so this file
records the modeling-friction signals expected from the slice (sort/head/tail/cut/tr/
uniq) plus the cross-cutting findings it would have hit. Cross-referenced against the
agent-c findings for the shared ones.

## F1 — action parameters cannot have defaults; `default` is reserved
- **Signal:** modeling friction (fail-loud, clear error)
- **Severity:** 🟡
- **Repro:** `probes/agent-ut-a/param-defaults.poly` — `Paint: action (c: Color default(Green))`
  → `Parse error: Expected parameter name, got 'default'`. The DSL has no defaulted
  action-parameter form; the error is clear (fail-loud), but utilities with defaulted
  flags (sort `-r`/`-n` etc.) must encode defaults as entity-level `default(...)` props
  or separate actions.

## Shared round-4 signals (see agent-c.md for full repros)
- 🟠 entity-level policies gate every action (guide documents `require` only) — a
  filter policy like `uniq`'s or `grep`'s match predicate silently blocks every action.
- 🟠 quantified `invoke [any|all] Rel.Action [where …]` dead-ends in the export (throw +
  unreachable tail → CS0162) while the runtime runs it — sed's line edits hit this.
- 🟠 `pattern(regex)` is write-time-only: `create` rejects non-matching values, so
  grep's read-time filter semantics shift silently.
- 🟠 no glob/regex/substring operator in expressions — `Name is "*.c"` is exact equality.
- 🟡 no-op-on-empty has no encoding (`invoke all/any` fail loud on zero matches; sed's
  `/pat/d` with no match must succeed).
