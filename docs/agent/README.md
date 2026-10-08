# Agent protocols

Plain markdown. Any tool can follow them. Always-on policy is [`../../AGENTS.md`](../../AGENTS.md). Machinery is [`../CORE.md`](../CORE.md). Admission is [`../plans/poly-eng/board.md`](../plans/poly-eng/board.md).

| Protocol | When |
|---|---|
| [`phenomenal-review.md`](./phenomenal-review.md) | Adversarial correctness review of a diff. Writes findings and follow-ups. Does not fix code unless asked. |
| Pre-ship gate | Before marking a slice Done. [`../plans/archive/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../plans/archive/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md). |
| [`poly-discovery-loop.md`](./poly-discovery-loop.md) | A discovery round the user started (`scripts/discovery-round.sh`). Not a default work mode. |
| Suite run | One README path that is not under `docs/plans/archive/`. Copilot: [`.github/agents/plan-suite-until-done.agent.md`](../../.github/agents/plan-suite-until-done.agent.md). Grok: `.grok/workflows/plan-orchestrator.rhai`. |

`reviews/` is evidence. Open a review only when a task names it. Do not scan the folder for what to do next.

## Rules for this folder

1. Portable markdown. Tool wrappers point here. They are not a second copy of the procedure.
2. One protocol per concern.
3. Follow-ups go in `docs/`, not only chat.
4. Link a protocol from `AGENTS.md` with one line. Do not paste the procedure into the always-on file.

## Tool wrappers

| Tool | Wrapper |
|---|---|
| Grok | [`.grok/skills/phenomenal-review/SKILL.md`](../../.grok/skills/phenomenal-review/SKILL.md) |
| Copilot | [`.github/skills/phenomenal-review/SKILL.md`](../../.github/skills/phenomenal-review/SKILL.md) |

Change the review bar only in `phenomenal-review.md`.
