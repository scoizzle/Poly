# Final Boss follow-ups — PR 60 `2993b986` nested effects / DMEFF006 — 2026-09-08

- **Review**: `docs/agent/reviews/2026-09-08-pr60-2993b986-final-boss.md`
- **SHA**: `2993b9865cee5ae3152e1bd8e56628a81a4c7ec3`
- **Verdict**: ship
- **Open**: F1–F3 suggestions; F4 nit
- **Bugs**: none
- **Prior follow-ups**: Razor `docs/agent/reviews/2026-09-08-pr60-2993b986-razor-followups.md` F1–F4 re-verified from `git show 2993b986:`; none closed; none promoted to bugs

## Open bugs (must close before ship)

None.

## Tasks

- [ ] **F1** — Add PipelineTransformation oracle for nested **transition** (title/docs claim). File: `Poly.Tests/DomainModeling/Compile/PipelineTransformationTests.cs` (near `:242`). Do: DSL `if (…) { transition to X }`; assert no `NestedDirectEffectDropped`; assert `session.Lower` / C# print contains CurrentStage / target; assert false branch stays put and true branch transitions. Owning: lowering / effect-surface completeness. Severity: suggestion (runtime already covered at `DomainEntityInstanceTests.cs:3586`).

- [ ] **F2** — Strengthen `ConditionalCreateIn_DoesNotWarnDropped_AndRuns` print oracle. File: `PipelineTransformationTests.cs:283–313`. Do: after `session.Lower`, assert method body / exporter C# contains CreateIn (mirror invoke test’s `Inc()` assertions). Severity: suggestion.

- [ ] **F3** — Archive honesty (optional). Files: `docs/plans/archive/domainmodeling-completed-2026-08/v2-to-v3/domainmodeling-next-phase.md:35`; `docs/plans/archive/probes-2026-08/findings/fleet-eval/04-analysis-pipeline.md:63`. Do: footnote that DMEFF006 was retired / nested effects now lower, or leave historical. Live `docs/plans/v2-to-v3/effect-surface-completeness.md` already correct. Severity: suggestion.

- [ ] **F4** — Fix stale `TryLowerVmNode` XML. File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:192–197`. Do: stop saying null means “execute directly on DomainEntityInstance”; document fail-closed Composite/Conditional. Severity: nit.

## Disposition of Razor items (this SHA)

| Item | Razor | Final Boss |
|------|-------|------------|
| F1 nested transition print oracle | open suggestion | **still open** — no PipelineTransformation `if` + `transition to` print/execute pair; runtime still `DomainEntityInstanceTests.cs:3586` |
| F2 create-in print oracle | open suggestion | **still open** — `:299` `session.Lower` still unasserted for CreateIn in C# |
| F3 archive DMEFF006 | open suggestion | **still open** — archive `:35` / `:63` unchanged; live plan corrected in this PR |
| F4 TryLowerVmNode XML | open nit | **still open** — `EffectLoweringPass.cs:192-197` still DEI-direct-null wording |
| Silent drop after warning removal | not found | **not found** — Composite/Conditional throw (`:613+`); named invoke binds module |
| Remaining `IsDirectExecutionEffect` callers | none | **none** — method deleted; `Poly/` grep empty |
| Live plan still saying DMEFF006 drops | fixed | **fixed** — `effect-surface-completeness.md:47,78` |

## Process follow-up (if recurring)

- [ ] When retiring an analyzer warning as a “lie,” require an explicit pointer in the PR to the fail-closed replacement (here: lowering throw) and a print+execute oracle for **each** effect kind named in the title — avoids claim > evidence on transition.
- [ ] Re-verify call-graph line numbers from `git show SHA:PATH` (Razor cited `DomainProgramProjection.cs:443` for LowerActionBody; on this SHA it is `DomainToCSharpExporter.Actions.cs:444`).
