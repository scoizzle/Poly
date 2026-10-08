# Poly eng: how the plan is kept

[`board.md`](board.md) is the one live queue. It says what is current, what is next, and what Scot must decide. Each slice has one file in [`tasks/`](tasks/) ([template](tasks/_TEMPLATE.md)). The slices come from [`pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md) (decisions in [`pipeline-convergence-plan.decisions.md`](../../domain-modeling/pipeline-convergence-plan.decisions.md)). Open pull requests are the live in-flight state.

## Pipeline in brief

The full pipeline (stages, mills and models, budget rules, launch commands, hang handling) is [`PIPELINE.md`](PIPELINE.md). Summary:

| Stage | Owner | Heavy work runs on | Leaves behind |
|---|---|---|---|
| Plan | Foreman | OpenCode (or Grok) | `tasks/<id>.md`, a draft PR on `slice/<id>` |
| Implement | 100x | Grok Build (OpenCode if Grok is down) | commits, self-review sweep in a PR comment, a Log row |
| Review (Review-2 slices) | Razor | the other mill | one PR comment: verdict and one findings table |
| Fix | 100x | same mill as implement | commits, a PR comment mapping finding ids to fixes, Log rows |
| Verify / ship gate | Final Boss | the other mill | a PR comment `SHIP <sha>` or `NOT SHIP <sha>`; CI green on that SHA |
| Merge | Chieftan, with Scot's OK (or Scot) | none | squash merge pinned to the SHIP SHA |

Agents run the heavy steps headless from fixed prompt templates. Bots only launch runs and pass one-line hand-offs. Reviews always run on the mill the implementer did not use. Nobody reviews their own work.

## Record rules

- **Task file:** every implement or fix commit adds its own Log row (date, who, mill, SHA, event, findings). Review verdicts are PR comments, and the next commit on the branch copies them into the Log. The merge row is added by the next slice's plan commit.
- **Board:** the plan commit of each new slice moves that slice from Next to In flight and records the previous merge. There are no docs-only PRs just for bookkeeping.
- **Review comments** start with one line, `ROLE | SHA | mill/model | SHIP or NOT SHIP | n open findings`, followed by one table `id | severity | file:line | finding | fix`.
- **Hand-edit gate:** every review answers "could Scot change this by hand, opening the files cold, without an agent?" A "no" is a finding of severity suggestion or higher and blocks SHIP until fixed or waived by Scot.
- **Code is truth;** plans are background. Use repo-relative links only, and no paths from any machine (except the box launch paths in `PIPELINE.md`).
