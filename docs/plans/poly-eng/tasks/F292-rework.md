# TASK F292 rework (PR 101) — unconditional cycle check + runtime loop guard

Status: merged #101 `11287134` 2026-10-05 (F293 ctor fix landed in the same PR).

Scot rulings 2026-10-05 (final for this slice):
1. Exit blocks MAY contain transitions (no blanket ban).
2. Stages may gate later transitions and fire them automatically once constraints are satisfied; guarded automatic transitions are valid.

Take over PR 101 https://github.com/scoizzle/Poly/pull/101. Lane B. Review 2 (Razor then Final Boss) after rework. Hand/Grok implement → OpenCode review. Not V11.

## Remove
Blanket DMEFF012 rejecting any StageTransitionEffect in an exit block.

## Analyze cycle check
Over the automatic transition graph (entry + exit, including nested in if):
- Reject ONLY unconditional cycles: every edge fires with no guard, or with a guard Analyze can prove is always true.
- A cycle that passes through a guard is ALLOWED (do not reject).
- Clear diagnostic (reuse/renumber DMEFF012; clash with PR 103 M1 — second merge renumbers).

## Runtime / lowering / print
- Allowed exit transition: inline exit once, no self-re-entry; simulate == printed.
- Interpreter AND printed code both need a loop guard as a backstop (Analyze cannot prove every guard ever becomes false). When the guard trips: fail loudly with a clear error, never overflow the stack.
- At runtime a guarded chain runs each step at most once per trigger (no recurse).

## Tests (required)
1. Allowed exit transition: Analyze clean, compiles, simulate == printed.
2. Unconditional loop: Analyze rejects.
3. Gated automatic chain: advances when its constraint holds and stops when it doesn't (simulate == printed).
4. Keep nested-if coverage where relevant.

## F293
Keep the duplicate parameterless ctor fix. If cycle-check rework runs long, split F293 into its own PR and tell Foreman.

## Done when
Shape matrix up front; smallest readable change; self-review sweep in PR comment; hand-edit gate.
