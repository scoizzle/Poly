---
name: phenomenal-review
description: >
  Adversarial correctness/contract review of a diff using docs/agent/phenomenal-review.md
  (split-context stance; optional mode: multi for a second independent pass). Writes
  findings and follow-up tasks into docs. Use when the user asks for a phenomenal
  review, deep review, adversarial review, contract review, correctness review of
  local/branch/PR changes, or runs /phenomenal-review. Not the pre-ship fix loop
  and not a maintainability/code-judo pass.
metadata:
  short-description: "Adversarial correctness review → docs follow-ups"
---

# Phenomenal review (Grok wrapper)

This skill is a **thin adapter**. The full protocol is tool-agnostic:

**[`docs/agent/phenomenal-review.md`](../../../docs/agent/phenomenal-review.md)**

## Instructions

1. **Read** `docs/agent/phenomenal-review.md` in full (do not improvise a lighter bar).
2. **Execute** that protocol against the user’s target:
   - Default: uncommitted local changes (staged + unstaged + untracked sources).
   - Or: branch, PR, or path list if the user named one.
   - Mode: `standard` (default) or `multi` if the user asks for multi/2×/adversarial pair.
3. **Adversarial stance** — assume the code is wrong; you are reviewer, not implementer. Prefer diff-first. If this session also implemented the change, prefer `mode: multi` (fresh subagent using the **Pass B template** in §3.7.1 of the protocol).
4. Enforce **sibling-path**, **reachability→severity**, and **primary evidence** rules from the protocol (do not chain-trust prior review quotes or approximate counts).
5. **Review only** — do not modify production or test code unless the user explicitly asks to harden afterward.
6. **Deliverables** (both required by the protocol):
   - Structured review note under `docs/` (suite-local or `docs/agent/reviews/`).
   - Checkable follow-up tasks under `docs/` (not chat-only), including process fixes when bug classes recur.
7. Report verdict, issue counts, file paths, and top issues in the conversation.

## Do not confuse with

| Skill / process | Difference |
|---|---|
| Bundled `/review` | Orchestrator + scratch review files / PR pending comments; still apply **this bar** if used for correctness, and land follow-ups in **docs** |
| `code-review` | Maintainability / code judo / file size |
| Pre-ship gate | Review → **fix** → re-review before Done (`Agents.md`, `pr1-uncommitted-review-gate.md`) |

## One-liner (if protocol path is forgotten)

```text
Read docs/agent/phenomenal-review.md and execute it against local changes.
Assume the code is wrong until evidence says otherwise.
Write findings and follow-up tasks into the docs as the protocol requires.
```
