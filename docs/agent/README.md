# Agent protocols (tool-agnostic)

These docs are **source of truth for agent workflows** in this repo. They are plain markdown: any AI tool, human, or CI step can open and follow them.

| Protocol | When to use |
|---|---|
| [`phenomenal-review.md`](./phenomenal-review.md) | **Adversarial** correctness / contract review of a diff (Bun-inspired split context, multi-pass option). Findings + follow-ups in docs. **Does not** fix code by default. |
| Pre-ship gate | Before marking a slice Done: review → harden → re-review. See [`../plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md) and workspace `Agents.md` § Pre-ship. |

## Design rules for this folder

1. **Portable** — no Grok/Cursor/Claude-only frontmatter or proprietary skill syntax. Optional tool wrappers elsewhere may *point here*; they must not be the only home for the bar.
2. **One protocol per concern** — keep skills composable (review vs fix vs ship).
3. **Follow-ups live in docs** — residual work is written into `docs/plans/` (or a protocol-specified path), not left only in chat.
4. **Link from always-on docs** when a protocol must be discoverable — prefer a single line in `Agents.md` over duplicating the procedure.

## Invoking from any agent

Paste or say:

```text
Read docs/agent/phenomenal-review.md and execute it against the current local changes.
Assume the code is wrong until evidence says otherwise.
Write findings and follow-up tasks into the docs as the protocol requires.
```

Multi-pass (independent second context, diff-only — parent uses Pass B template §3.7.1):

```text
Read docs/agent/phenomenal-review.md and execute it against local changes, mode: multi.
```

Or substitute the target (branch, PR, path list) as the protocol allows.

## Tool wrappers (optional)

These only point at the portable protocol; they are not a second source of truth.

| Tool | Location | How to run |
|---|---|---|
| **Grok Build** | [`.grok/skills/phenomenal-review/SKILL.md`](../../.grok/skills/phenomenal-review/SKILL.md) | `/phenomenal-review` or matching intent |
| **GitHub Copilot** | [`.github/skills/phenomenal-review/SKILL.md`](../../.github/skills/phenomenal-review/SKILL.md) | Agent Skills load from `.github/skills/`; ask for a phenomenal/deep/contract review |

Keep wrapper bodies thin. Change the bar only in `phenomenal-review.md`.
