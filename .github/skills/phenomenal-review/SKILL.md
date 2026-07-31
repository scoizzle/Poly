---
name: phenomenal-review
description: >
  Adversarial correctness and contract review of a git diff via
  docs/agent/phenomenal-review.md (split-context reviewer stance; optional
  multi-pass). Produces structured findings and checkable follow-up tasks under
  docs/ (not chat-only). Use when the user asks for a phenomenal review, deep
  review, adversarial review, contract review, correctness review of
  local/branch/PR changes, or wants residual work filed into plan docs. Do not
  use for pre-ship fix loops, maintainability/code-judo audits, or implementing
  fixes unless the user explicitly asks to harden after the review.
---

# Phenomenal review (Copilot / Agent Skills wrapper)

This skill is a **thin adapter**. The full protocol is tool-agnostic and lives in the repository:

**`docs/agent/phenomenal-review.md`**

Also see: `docs/agent/README.md`.

## Instructions

1. Open and follow **`docs/agent/phenomenal-review.md`** completely. Do not substitute a lighter checklist.
2. Determine the review **target** (default: uncommitted local changes; or branch / PR / paths if specified) and **mode** (`standard` or `multi`).
3. **Adversarial stance:** assume the code is wrong; reviewer is not implementer; prefer diff-first. If this session wrote the code, use multi-pass with a fresh context and the **Pass B template** (`docs/agent/phenomenal-review.md` §3.7.1).
4. Collect the diff, trace producer/consumer call graphs, hunt fail-closed and contract bugs (sibling-path drift, reachability before severity, primary-evidence counts, invariant comments as checklists, same-shape-different-meaning, oracle weakening), audit tests and plan honesty — as the protocol specifies.
5. **Do not modify production or test code** unless the user explicitly requests a harden/pre-ship pass after the review.
6. **Required deliverables:**
   - A structured review markdown file under `docs/` (suite path or `docs/agent/reviews/YYYY-MM-DD-<slug>.md`).
   - Checkable follow-up tasks under `docs/` (including process fixes when bug classes recur). Chat alone is not enough.
7. End with a short user report: verdict, bug/suggestion/nit counts, paths to the two docs, top issues.

## Related (do not merge)

- Always-on repo rules: `AGENTS.md` / `Agents.md`, `.github/copilot-instructions.md`
- Pre-ship **fix** gate: `docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`
- Maintainability audits: separate skill if present; this skill is correctness/contracts first
