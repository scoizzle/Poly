# PR 90 @ f3f0dd36: follow-ups (all nits; none blocks the merge)

Verdict SHIP, bugs 0, hand-editability gate YES. K1 is on the V11 standing-merge list: Foreman may merge. F160-F167 are all closed.

1. **F180** `pipeline-stage-map.md:150`: after "registering their output as artifacts (slices A5a and A5b)" add "and no longer calling `GetOrLower` (slice C9; `src/Poly.DslCompiler/MinimalApiGenerator.cs:1145`)".
2. **F181** (a) When K1 merges, edit `pipeline-convergence-plan.md` line 3: move K1 from "In review, not merged" to "Done so far" with its merge SHA (and drop "(PR 91)"-style numbers if they will go stale). (b) Wave 0 heading: replace "new code that nothing calls yet" with "new code not yet wired into Emit or the consumers" (`Lower` calls `DeclareType` and `Register` for the placeholder).
3. **F182** Name the same leftover helpers in difference 5 and in "What PRs 82 and 83 changed": `BindModuleMethodBody`, `BindForSimulate`, `AsVoidResultBody`.
4. **F183** Difference 8: add "(the MCP session holding the authored `Domain` is allowed, decision 6)".

Re-check: `git grep -n "exactly two" docs/domain-modeling/pipeline-stage-map.md` empty; `git diff --check`.
