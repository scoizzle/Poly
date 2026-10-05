---
name: Plan suite until done
description: "Execute one Poly suite README that PIPELINE-STATUS has not archived. Orient, implement, verify, record. One suite per run."
tools: ["execute", "read", "edit", "search", "todo"]
user-invocable: true
argument-hint: "Suite README path. Mode: until-done (default) or next. max_tasks defaults to 8."
---

Execute **one** suite the user names. Do not invent product work outside its task files.

## Admit

1. Read [`docs/plans/simple-agent-tasks/PIPELINE-STATUS.md`](../../docs/plans/simple-agent-tasks/PIPELINE-STATUS.md).
2. The argument is a `*-README.md` path. There is no key table.
3. If that path is under `docs/plans/archive/`, or the status table marks the suite archived, stop and report that.
4. Also read [`AGENTS.md`](../../AGENTS.md), [`docs/CORE.md`](../../docs/CORE.md) before platform edits, and the suite gate linked from the README.

Mode: `until-done` (default) or `next`. Stop after `max_tasks` (default 8) even if the suite is open.

## Loop

1. **Orient.** First `[ ]` task, respecting prereqs. If every task is done, run the suite gate. If the suite and gate are done, stop.
2. **Implement.** Read only the task's required reading. Edit only files it owns. No `#region`. Tests with feature changes, TUnit style from `AGENTS.md`.
3. **Verify.** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` then `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false`. Filter when the task says so. Do not mark Done on a red build.
4. **Record.** Mark the task done in the suite README. Follow-ups go in a `docs/plans/` note, not only chat.
5. **Continue** until the suite is done, `max_tasks` hits, or a blocker. `next` stops after one task.

Stop when build or tests stay red after two attempts, the task needs a design the file does not contain, or the tree conflicts with file ownership.

On gate close, run [`docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../../docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md). Update PIPELINE-STATUS in the same change only when the suite's own instructions say to. Do not invent a second status schema.

## Report

Suite path, complete or blocked, task ids finished, remaining `[ ]` items, build and test result, files changed. Do not commit unless the user asked. Do not start a second suite.
