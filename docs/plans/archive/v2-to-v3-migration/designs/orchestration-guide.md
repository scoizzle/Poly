# Orchestration Guide for the V2 → V3 Domain Modeling Port

**Purpose**: This document defines the operating model for running the port with a mix of agent capabilities (large orchestrators + many smaller executors) working in parallel.

It is the single source of truth for *how* the plan is executed and coordinated.

## Roles

### 1. Orchestrator Agents (Larger / More Capable)
- Own one or more Workstreams (e.g., WS1 owner).
- Responsible for **decomposition**: Breaking workstream goals into micro-tasks in `simple-agent-tasks/`.
- Perform high-level design, interface definition, and integration.
- Review and accept work from smaller agents.
- Create or heavily review decision records for significant choices.
- Maintain the health of their workstream file and the master roadmap.

### 2. Executor Agents (Smaller / Simpler Models)
- Primarily work on individual micro-tasks from `simple-agent-tasks/`.
- Follow extremely narrow, self-contained instructions.
- Do **not** edit shared high-level documents (master roadmap, workstream files, decision records) except for simple status updates when explicitly instructed in the micro-task.
- Escalate when blocked or when the task seems underspecified.

### 3. Hygiene / Support Agent(s) (WS6)
- Runs in parallel with technical work.
- Helps smaller agents with documentation impact.
- Assists with decision record drafting when technical agents make choices.
- Audits the master roadmap and workstream files for consistency.

## Core Operating Principles

1. **Shared State is Sacred**
   - The master roadmap (`master-roadmap.md`) and workstream files are the only shared mutable state for planning.
   - Only Orchestrator Agents (or the Hygiene agent) should make structural changes to these files.
   - Executor agents only update status fields when the micro-task template explicitly tells them to.

2. **Interface Contracts Come First**
   - Any cross-workstream dependency must have a clear, documented interface (even if informal).
   - Changes to interfaces must be coordinated via the affected Workstream Owners and recorded (at minimum in the workstream file, ideally in a decision record).

3. **Small Agents Stay in Their Lane**
   - Micro-tasks must be written so the executor only needs to read a very small, explicitly listed set of files + the task itself.
   - Micro-tasks should almost never require editing shared planning documents.

4. **Verification Before "Done"**
   - A micro-task is not complete until the verification checklist passes **and** it has been reviewed/accepted by the owning Orchestrator (or automated checks where possible).

5. **Decisions Belong to Capable Agents**
   - Only Orchestrator-level agents (or the Hygiene agent) should create new decision records in `docs/decisions/`.

## Daily / Per-Session Workflow (Updated with Task Summaries)

**Core Principle**: Executor agents (especially smaller ones) should **not** directly edit shared planning documents. They report via structured task summaries. Orchestrators synthesize those summaries into the official plan.

**For an Orchestrator Agent:**
1. Review current status in master roadmap + your workstream(s).
2. Identify next valuable work.
3. Decompose into (or refine existing) micro-tasks in `simple-agent-tasks/`.
4. Assign micro-tasks to available Executor agents (or queue them).
5. Periodically review new files in `agent-summaries/`.
6. Accept/reject work based on the summary + actual code changes.
7. Update the master roadmap, relevant workstream file(s), and create decision records as needed.
8. Move processed summaries into `agent-summaries/archive/` (or mark them clearly as "Integrated").

**For an Executor Agent (Small Model):**
1. Pick a micro-task from `simple-agent-tasks/` (or be assigned one).
2. Read **only** the files explicitly listed in the "Context You Need" section of that micro-task.
3. Complete the exact steps and verification.
4. Create a new summary file in `agent-summaries/` using the template (`TEMPLATE-task-summary.md`).
5. Fill it out honestly (especially the "Impact on the Overall Plan", "Decision Impact", and "Blockers" sections).
6. Update only the Status line in the micro-task file itself (e.g. "Done – summary submitted as `2026-06-03_@my-agent_ws3-xxx.md`").
7. Do **not** edit the master roadmap or workstream files.

**For the Hygiene Agent (WS6):**
- Monitor `agent-summaries/` for documentation and decision debt surfaced by other agents.
- Help draft decision records when summaries indicate the need.
- Assist orchestrators with plan hygiene when volume is high.

**Important**: The existence of `agent-summaries/` is the primary mechanism that allows many smaller agents to contribute safely while keeping the overall plan coherent.

## Claiming and Status Rules

- Workstreams are claimed by writing your agent identifier in the "Owner" column of the master roadmap.
- Individual micro-tasks are claimed by setting Status to "In Progress (claimed by @your-agent)" in the micro-task file itself.
- Only one agent should be actively working on a specific micro-task at a time.

## Escalation Paths

If you encounter any of the following, stop and escalate (update the task file + notify relevant owners):

- The task description is ambiguous or seems to require significant design.
- You need to change an interface or shared file not explicitly listed in the task.
- The task depends on unfinished work that isn't documented.
- You're unsure whether a choice requires a decision record.

## Anti-Patterns to Avoid

- Small agents editing the master roadmap or workstream files directly.
- Orchestrators creating huge monolithic tasks instead of decomposing.
- Parallel agents making conflicting changes to the same interface without coordination.
- Treating micro-task status updates as optional.

## Tooling & Process Recommendations

- Use clear, unique agent identifiers when claiming work (e.g., `@small-claude-1`, `@orchestrator-gpt4`).
- When a micro-task is complete, the Executor should leave a short "Done" note with any observations.
- Orchestrators should periodically do a "sweep" to accept completed micro-tasks and update higher-level status.
- Consider using a simple convention for comments in the tracker files (e.g., `<!-- @agent-xyz: ... -->`).

## Archiving & Review Process

This section defines how work from Executor agents (especially smaller models) is reviewed and folded into the official plan.

### Review Flow

1. **Executor** completes a micro-task and creates a new file in `agent-summaries/` using the template.
2. **Orchestrator** periodically scans the `agent-summaries/` directory for new/unreviewed summaries.
3. Orchestrator performs a lightweight review:
   - Does the summary + linked code changes actually solve the stated objective?
   - Did the agent follow the Core Engineering Principles?
   - Are there any red flags (over-engineering, missed edge cases, new decision debt)?
   - Does the "Impact on the Overall Plan" or "Decision Impact" section require action?
4. If the work is accepted:
   - Orchestrator integrates the real code changes (if not already done via normal review process).
   - Orchestrator updates the relevant workstream file and/or master roadmap with accurate status.
   - Orchestrator creates or updates any decision records flagged in the summary.
   - Orchestrator adds a short review note to the summary file itself (see template).
5. Once fully processed, the summary is moved to `agent-summaries/archive/YYYY-MM/` for historical record.

### Quality Bar for Acceptance

- The micro-task verification checklist must be complete and believable.
- The work must not violate the Core Engineering Principles.
- Any new architectural choices must be captured (at minimum as a stub decision).
- The summary must be honest about limitations or open questions.

### Handling Problematic Summaries

- **Incomplete or low-quality work**: Reject politely in the review note and leave the micro-task status as "Needs rework". The original agent (or another) can pick it up again.
- **Significant new decisions needed**: The orchestrator (or WS6 Hygiene agent) should create the decision record and link it from the summary.
- **Pattern that should become a reusable micro-task**: Orchestrator can extract the pattern into a new template or example in `simple-agent-tasks/`.

### Archiving Rules

- Move accepted summaries into `agent-summaries/archive/2026-06/` (month-based subfolders) after they have been integrated.
- Keep rejected or "needs rework" summaries in the main `agent-summaries/` directory with clear status in the review section.
- Do not delete summaries — they form part of the project's execution history.

### Example

See `agent-summaries/EXAMPLE-filled-summary.md` for a realistic example of a completed summary from a smaller agent, including what an orchestrator review note might look like.

---

This guide exists because coordination complexity grows non-linearly with the number of agents (especially when mixing capability levels). Following these rules should keep the port moving efficiently while protecting architectural integrity and documentation quality. 

Update this guide itself whenever the team discovers better coordination patterns.