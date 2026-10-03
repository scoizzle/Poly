# PR #84 Final Boss follow-ups (head abc6181c): verdict SHIP

No must-fix items. Everything below can be a small follow-up edit in section 7 (or at sign-off), none touches the stage map.

## Suggestions (do before Scot signs off if cheap)
- **F65** `plan:439`: append "and they still enforce every invariant (cardinality from item 10, constraints on the created instance)".
- **F66** `plan:439`: replace "the only time an outside entity can influence another entity's state" with "the only time an outside entity can change another entity's state directly". Reason: cross-entity `invoke Rel.Action` (`poly-dsl-guide.md` ~518-528, runtime test `McpSmokeTests.cs:2813`) is also an outside influence, but it runs the target's own named action.
- **F71** `plan:295-296`, `:423`: extend the precedence note to "slices that depend on a HELD slice (A3 to A5, F1 to F2)" and add one line to section 0 pointing to section 7 for the override rules. Alternatively edit rows `:276`, `:295`, `:296`, `:304` to say "HELD, see section 7" when the plan is next touched.

## Nits
- **F67** `plan:439`: drop "effects" or say link and unlink need a tree form (C6); they are store and tool operations today (`DomainInstanceStore.cs:364,379`).
- **F68** `plan:439`: say whether MCP `create_instance` of a root entity (`RuntimeTool.cs:148,197`) is session storage under Decision 9; add C6 and C8c as the slices affected.
- **F69** `plan:5`: delete "next to this file". `stage-map:169` carries the same wording; fix it with F54 in slice K1 (the stage map must stay byte-identical until then).
- **F70** `plan:437`, `:439`: split Scot's recorded words from the plan's own note ("Plan note: ...") in the "Decided" paragraphs.

## Carried, unchanged and parked
F54, F55, F56, F58, F62, F64 (parked by Foreman), F57 and F60 (nits, not worsened).

## Closed this round
F50, F51, F52, F53, F59, F61 (plan pointers), F63.
