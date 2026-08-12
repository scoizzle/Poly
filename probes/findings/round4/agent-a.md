# Round-4 findings — agent-a (coreutils)

Utilities modeled: ls, chmod, cp/mv/rm file-ops, minicopy. Probes in `probes/agent-ut-a/`.
Findings extracted from the probes (the agent's report was lost; the probes + run-probe.sh
are the evidence).

## F1 — `create in` with multiple bare-identifier (action-param) values misparses
- **Signal:** compile-fail (parse) — FIXED this round
- **Severity:** 🔴
- **Repro:** `probes/agent-ut-a/fileops.poly`, `minicopy.poly` —
  `create in files { Name: newName Content: srcContent Mode: srcMode }` →
  `Parse error: Expected property name, got ':'`. A bare-identifier value followed by
  the next initializer was consumed as a path-prefix (`newName.Content`).
- **Expected:** the common `create in { Prop: param ... }` shape must parse.
- **Actual:** blocked on any create-in with 2+ action-param initializers. **FIXED** —
  initializer-value parsing stops path continuation at an `Identifier :` boundary
  (`InPropertyInitializerValue` cursor mode). `minicopy.poly` now compiles 0/0.

## F2 — property names that camelCase to a C# reserved keyword break the export
- **Signal:** compile-fail (silent — no analysis rejection)
- **Severity:** 🟠
- **Repro:** `probes/agent-ut-a/fileops.poly` — `Protected: Boolean default(false)`
  camelCases to `protected` → export emits `bool protected = false` → CS1001 (162-error
  cascade).
- **Expected:** reject such names at analysis (fail-loud) or escape (`@protected`).
- **Actual:** the DSL accepts the name, the export emits invalid C#. Filed for a
  design decision (escape vs reject).

## F3 — modeling friction (verified, not bugs)
- `ls` (sort order options as enums), `chmod` (mode as octal Number range 0-511 with a
  default), and `touch`/`mkdir`/`cp` encode cleanly (0/0). Permissions as a `Number`
  range works; option flags as enums work.
