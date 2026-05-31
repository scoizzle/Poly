# Agent Task Summaries

This directory is the primary mechanism for agents (especially smaller ones) to report completed or in-progress work back to the orchestrator(s) **without directly editing** the shared planning documents.

## Why This Exists

Directly editing `master-roadmap.md` and the workstream files creates high risk of:
- Conflicting edits when multiple agents work in parallel
- Inconsistent or low-quality updates from smaller models
- Accidental violation of planning discipline

Instead, every agent produces a structured summary here. Orchestrator agents then review these summaries and maintain the correctness of the master plan.

## Workflow

1. **Executor agent** (large or small) completes (or attempts) a task.
2. They create a new file in this directory using the template (`TEMPLATE-task-summary.md`).
3. They fill it out honestly and mark the relevant micro-task or workstream item as "In Progress" / "Done (summary submitted)" if the task template instructs them to.
4. **Orchestrator agent(s)** periodically scan this directory, review summaries, integrate the real work, update the master roadmap + workstream files, and (if needed) create decision records.
5. Once a summary has been fully processed by an orchestrator, it can be moved to an `archive/` subfolder or clearly marked as "Integrated".

## File Naming Convention

`YYYY-MM-DD_<agent-id>_<task-or-microtask-id>.md`

Examples:
- `2026-06-03_small-claude-2_ws3-add-property-operation.md`
- `2026-06-03_orchestrator-gpt4o_ws1-core-skeleton-review.md`

This makes it easy to batch-process summaries by date or agent.

## What Goes in a Summary

Use the template. The most important sections for the orchestrator are:
- **Impact on the Overall Plan**
- **Decision Impact**
- **Blockers / Open Questions**

These sections are how small agents "talk back" to the plan without breaking it.

See `EXAMPLE-filled-summary.md` in this directory for a realistic example of what a completed summary from a smaller agent looks like (including a sample orchestrator review note).

## For Orchestrator Agents

- Treat these summaries as the raw material for plan maintenance.
- Do **not** expect perfect updates from smaller agents — your job is to synthesize them into coherent plan state.
- When a summary indicates a significant design choice was made, you are responsible for creating or updating the corresponding decision record.
- Periodically archive processed summaries so the directory stays clean.

## Benefits of This Approach

- Much safer for parallel work by agents of mixed capability.
- Creates a durable record of what each agent actually contributed.
- Reduces context load on small agents (they don't have to understand the entire planning system to report progress).
- Makes the orchestrator's job more explicit and manageable.

This pattern is especially powerful when we want the *majority* of implementation work to be done by smaller/cheaper models while still keeping the overall plan coherent and high-quality.