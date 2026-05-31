# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: This directory contains very small, self-contained tasks that can be reliably completed by smaller, lower-context, or cheaper model-based agents.

## Philosophy

Not all work requires a large frontier model. The majority of the V2→V3 port implementation can (and should) be done by simpler agents if we break the work down properly.

**Guiding rules for tasks in this directory**:
- One task = one small, verifiable change.
- Minimal context needed (ideally < 4k–8k tokens total).
- Clear, explicit instructions.
- Self-contained verification steps.
- References to AGENTS.md + specific decision files for principles.
- Designed so a small model can succeed without deep architectural reasoning.

## How to Use These Tasks

1. **Larger agents / orchestrators**: Scan the workstream files, identify suitable micro-tasks, create new ones here, and assign them to smaller agents.
2. **Smaller agents**: Pick a task from this directory. Read only the referenced files + the task itself. Complete it. Update status.
3. **All agents**: After finishing, mark the task complete and update the parent workstream/master roadmap.

## Task Format

Every micro-task should follow this template (see examples in this directory):

```markdown
# Micro-Task: [Short Descriptive Name]

**Parent Workstream**: WSx
**Difficulty**: Small Model Friendly
**Estimated Tokens**: < 6k

## Objective
One clear sentence.

## Context You Need
- Link to specific small sections of AGENTS.md
- Link to 1-2 specific decision files (never the whole plan)
- 1-2 source files at most

## Exact Steps
1. ...
2. ...

## Verification
- Build succeeds
- Specific test passes
- [ ] Checklist item

## Output
What the agent should produce (e.g., "a new file at X with Y", "a passing test", "updated comment").

## Status
[ ] Not Started / In Progress (claimed by @agent-xyz) / Done
```

## Principles for Small-Model Tasks

- Prefer **implementation** over design.
- Prefer **copy-paste + adapt** patterns over inventing new abstractions.
- One file changed is ideal. Two files max for most tasks.
- Always include explicit "check AGENTS.md Core Principles" reminder.
- Make the success condition objective and testable.

## Current Micro-Tasks

(See files in this directory)

When creating new ones, name them clearly, e.g.:
- `ws3-add-property-operation.md`
- `ws1-implement-evolution-trace-record.md`

## Coordination & Reporting

See the **[Orchestration Guide](../orchestration-guide.md)** for the full model.

**Important**: After completing (or attempting) a micro-task, **do not edit the master roadmap or workstream files directly**. Instead:

1. Create a new file in `../agent-summaries/` using the template (`TEMPLATE-task-summary.md`).
2. Fill it out (especially the "Impact on the Overall Plan" and "Decision Impact" sections).
3. Update only the Status line in *this* micro-task file to indicate that a summary was submitted.

The orchestrator(s) will review your summary and fold the results into the official plan. This is the designed way for smaller agents to contribute without breaking shared state.