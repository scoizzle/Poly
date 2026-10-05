---
name: phenomenal-review
description: >
  Adversarial correctness review of a diff. Use when the user asks for a
  phenomenal, deep, adversarial, contract, or correctness review, or
  /phenomenal-review. Not the pre-ship fix loop and not a style audit.
metadata:
  short-description: Adversarial correctness review into docs
---

# Phenomenal review

Read and execute [`docs/agent/phenomenal-review.md`](../../../docs/agent/phenomenal-review.md). Do not substitute a shorter checklist.

Default target is uncommitted local changes. Use a branch, PR, or path list when the user names one. Mode is `standard` unless the user asks for multi. If this session wrote the change, use multi and the protocol's Pass B template.

Reviewer only. Do not edit production or tests unless the user asks to harden afterward.

Write the protocol's two docs: a review note under `docs/`, and checkable follow-ups under `docs/`. Then report verdict, counts, paths, and the top issues.
