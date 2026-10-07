# Poly eng plans (lanes, board, task files)

This folder is where the Poly engineering bots keep their working plan. It moved here from a private box folder on 2026-10-06 (Scot: all Poly plans live in the repo).

| File | What it is | Owner |
|------|------------|-------|
| [`board.md`](board.md) | The living lane board: open slices, who has them, tips, merges, parked cards | Foreman |
| [`tasks/<slice>.md`](tasks/) | One file per slice: scope, files, done-when, SHIP / NOT SHIP, then a running log | The slice's implementer, reviewers and Foreman |

The plan the slices come from is [`docs/domain-modeling/pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md) (decisions in [`pipeline-convergence-plan.decisions.md`](../../domain-modeling/pipeline-convergence-plan.decisions.md)). The pick is in [`PIPELINE-STATUS.md`](../simple-agent-tasks/PIPELINE-STATUS.md).

## Rule: update the task file with every change

Every bot updates its slice's task file here with each change it makes: each push, each review verdict, each merge. Do it in the same PR, or in a short docs-only follow-up PR right after.

- **Implementer:** add the pushed SHA and the mill it ran on (hand, Grok, OpenCode).
- **Reviewer:** add one verdict row: reviewer, mill, SHA reviewed, verdict (SHIP / NOT SHIP), link to the findings.
- **Foreman:** record the merge (PR number, merge SHA, date) and update `board.md`.

Verdict rows use this table:

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|

## New slices

Foreman adds `tasks/<slice>.md` when the slice is assigned. Keep it short: smallest readable change, shape matrix, self-review sweep, hand-edit gate ("could Scot change this by hand?").

Use repo-relative links only. No paths from any bot's machine.
