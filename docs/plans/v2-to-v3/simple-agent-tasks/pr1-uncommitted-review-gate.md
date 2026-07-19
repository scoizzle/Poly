# Process Task: Uncommitted-Change Review Gate (ry1)

**Suite:** Reusable — reference via `pr1-uncommitted-review-gate.md` in any suite pick order.  
**Difficulty:** Small Model Friendly  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started

## Objective

Before marking a suite or slice "Done" (shipped), run the **uncommitted-change review loop**: review dirty files, categorize findings, harden with three-layer defense and fail-closed posture, re-review, and only ship when the tree is clean and findings are resolved.

## Origin

The following loop was exercised live during E3b + Q3′ on branch `rewrite/domainmodeling-from-scratch`. This task codifies it as a repeatable pre-commit gate.

```
Review ──→ Findings ──→ Harden ──→ Re-review ──→ Ship
  ↑                                               │
  └────────────────── Loop ────────────────────────┘
```

## Required Reading

- `AGENTS.md` — principles 1–5 (especially §4 Go well to go fast, §5 Shipped capability)
- `docs/CORE.md` — module boundaries, pipeline
- `docs/plans/v2-to-v3/simple-agent-tasks/qe-README.md` — suite operating rules
- **Your suite's own task files and source changes** — review via `git diff --stat HEAD` then `git diff HEAD`

## Exact Steps

### Step 1 — Review uncommitted changes

Run:
```
git diff --stat HEAD        # quick overview: which files, how many lines
git diff HEAD               # full diff — read carefully
```

For each dirty file:
- Is it source code? Tests? Docs? Config?
- Does it belong to the **current task** or is it drift from a previous task?

### Step 2 — Categorize findings

For every structural or behavioral change, classify each finding by severity:

| Severity | Label | Example | Action |
|----------|-------|---------|--------|
| 🔴 **Structure** | Package/type placement wrong, module boundary violation | New type in wrong namespace | Fix — violates AGENTS.md / CORE.md |
| 🟠 **Contract** | Silent fallback, missing error, unexpected success | Empty `all` returns true; missing `where` drops filter | Fix — fail-closed |
| 🟡 **Edge case** | Off-by-one, empty set, null input, zero match | Remove-by-name with no match silently no-ops | Fix or note as known gap |
| ⚪ **Hygiene** | Dead code, stale comment, inconsistent naming | Dead `using`, parameter named wrong | Fix if small; else file as follow-up |

### Step 3 — Apply three-layer defense

When fixing a 🟠 **Contract** or 🔴 **Structure** finding, verify all three enforcement layers:

| Layer | Where | What to check |
|-------|-------|---------------|
| **Parse-time** | Parser (PolyDslParser) | Does the parser reject invalid syntax with a clear error? |
| **Analyze-time** | Analyzers (EffectAnalyzer, PolicyConstraintAnalyzer, etc.) | Does the static analysis pass catch the contract violation? |
| **Runtime** | Interpreter / VM / DomainEntityInstance | Does the runtime fail loud if the violation reaches it? |

**Fail-closed posture:** Empty sets, missing matches, and invalid configurations **fail loud** (throw / return error). Vacuous success (e.g. `all` on empty collection returning true) is **banned** unless explicitly documented as a design choice.

### Step 4 — Apply fixes

For each finding:
1. Write or tighten **one** test that demonstrates the failure
2. Change production code with the **smallest** fix that makes it pass (per §4)
3. Under green, remove duplication and special cases

### Step 5 — Re-review

After all fixes are applied:
```
git diff --stat HEAD        # verify the fix delta is proportional
git diff HEAD               # re-read — no new problems introduced
```

- Findings count should be 0 for 🔴🟠
- 🟡 findings should be resolved or explicitly filed in `docs/plans/`
- ⚪ hygiene should be clean unless deferred with a note

### Step 6 — Ship

Only mark the suite/slice "Done" when:
- [ ] `git diff --stat HEAD` is clean (0 dirty files) — or dirty files are **intentional pre-committed next-task work** (rare)
- [ ] All 🔴 and 🟠 findings resolved
- [ ] 🟡 findings filed or resolved
- [ ] ⚪ hygiene clean or deferred with a doc note
- [ ] `dotnet build` passes
- [ ] Full suite passes (`dotnet run --project Poly.Tests/Poly.Tests.csproj`)

## Verification (of this process)

- [ ] The checklist in Step 6 is complete before any "Done" marker
- [ ] Three-layer defense is verified for each contract finding
- [ ] No vacuous success paths are left unaddressed

## Output

- Findings list (written to agent summary or task notes)
- Fix commits (one or more)
- Re-review confirmation
- This task is complete when the parent suite passes the Step 6 gate

## Status tracking

**Claimed by:**
**Started:**
**Notes / Blockers:**
