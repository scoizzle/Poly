# Agent protocols

Plain markdown. Any tool can follow them. Always-on policy is [`../../AGENTS.md`](../../AGENTS.md). Machinery is [`../CORE.md`](../CORE.md). Admission is GitHub issues under umbrella #305 (or its successor); the slice plan is the draft PR body. [`../plans/poly-eng/board.md`](../plans/poly-eng/board.md) is superseded.

| Protocol | When |
|---|---|
| [`phenomenal-review.md`](./phenomenal-review.md) | Adversarial correctness review of a diff. Writes findings, a validated repro test per bug in the report (§3.10), and follow-ups (a shell-less reviewer returns them; the parent writes them). Read-only on the tree; does not fix production code. |
| Pre-ship gate | Before marking a slice Done. [`pr1-uncommitted-review-gate.md`](./pr1-uncommitted-review-gate.md). |
| [`poly-discovery-loop.md`](./poly-discovery-loop.md) | A discovery round the user started (`scripts/discovery-round.sh`). Not a default work mode. |
| Suite run | Not a live queue (Part 0, 2026-10-26). [`.github/agents/plan-suite-until-done.agent.md`](../../.github/agents/plan-suite-until-done.agent.md) and `.grok/workflows/plan-orchestrator.rhai` read `board.md` and suite task files. Do not start work from them. |

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
