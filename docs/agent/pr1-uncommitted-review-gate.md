# Uncommitted-Change Review Gate

**Reusable process:** reference it as `pr1-uncommitted-review-gate.md` from any suite pick order.

## Objective

Before marking a suite or slice "Done" (shipped), run the **uncommitted-change review loop**: review dirty files, categorize findings, **land each finding's reported failing test**, harden with three-layer defense and fail-closed posture, turn the test green with the smallest fix, re-review, and only ship when no dirty file of **this task** remains (other dirty files are named, not counted) and findings are resolved.

Review is read-only: the repro test arrives in the review report and enters this tree at fix time. Every finding enters the loop as a red test. A finding that is not red is not actionable, and a finding that cannot be made red is `open (unproven)` and blocks.

## Origin

The following loop was exercised live during E3b + Q3′ on branch `rewrite/domainmodeling-from-scratch`. It was codified here as a repeatable pre-commit gate.

```
Review ──→ Red test ──→ Fix ──→ Green ──→ Re-review ──→ Ship
  ↑                                                    │
  └──────────────────── Loop ───────────────────────────┘
```

## Required Reading

- `AGENTS.md` — principles 1–5 (especially §4 Go well to go fast, §5 Shipped capability)
- `docs/CORE.md` — module boundaries, pipeline
- `docs/plans/archive/domainmodeling-completed-2026-08/v2-to-v3/simple-agent-tasks/qe-README.md` — historical suite conventions (frozen product direction)
- **Your suite's own task files and source changes** — collect status, diff, and untracked files as Step 1 describes

## Exact Steps

### Step 1 — Review uncommitted changes

Collect the complete change. With a shell, run:
```
git status --short                          # staged, unstaged, and untracked
git diff --stat HEAD                        # quick overview: which files, how many lines
git diff HEAD                               # staged + unstaged vs HEAD — read carefully
git ls-files --others --exclude-standard    # untracked files — read each in full
```
`git diff HEAD` does not list untracked files. Read every untracked, non-ignored
file the last command names; an untracked source file is easy to miss and is a
dirty file of this task.

A reviewer without a shell does not collect evidence. The parent gives it the
same items — status, diff, and the contents of every untracked file — as a
`review-bundle` directory or pasted. The reviewer reads only what it was given,
and reports any of those items missing as uncertainty, not as a clean review.

For each dirty file:
- Is it source code? Tests? Docs? Config?
- Does it belong to the **current task** or is it drift from a previous task?

### Step 2 — Categorize findings

For every structural or behavioral change, classify each finding by **category**,
then assign its **severity** from phenomenal §6 (🔴 and 🟠 block; 🟡 is a
suggestion; 🟢 is a nit).

The category says what kind of defect it is; it does **not** set severity. A 🔴
or 🟠 is fixed in this gate even when its category is Edge case or Hygiene.
"Note as known gap" and "file as follow-up" apply only to 🟡 and 🟢.

| Category | Example | Action |
|----------|---------|--------|
| **Structure** | Package/type placement wrong, module boundary violation | Fix — violates AGENTS.md / CORE.md |
| **Contract** | Silent fallback, missing error, unexpected success | Fix — fail-closed |
| **Edge case** | Off-by-one, empty set, null input, zero match | Fix or note as known gap |
| **Hygiene** | Dead code, stale comment, inconsistent naming | Fix if small; else file as follow-up |

Each finding must already carry its repro from review: the test source verbatim in the review note, or a runnable check (`rg`, `git show`, probe) for docs/config/plan findings. A supplied but not-yet-executed test is `pending red` until this gate runs it. If neither a test nor a check exists, label it `open (unproven)` — it blocks ship and cannot be closed. The test enters this tree at fix time, not during review.

### Step 3 — Apply three-layer defense

When fixing a **Contract** or **Structure** finding, verify all three enforcement layers:

| Layer | Where | What to check |
|-------|-------|---------------|
| **Parse-time** | Parser (PolyDslParser) | Does the parser reject invalid syntax with a clear error? |
| **Analyze-time** | Analyzers (EffectAnalyzer, PolicyConstraintAnalyzer, etc.) | Does the static analysis pass catch the contract violation? |
| **Runtime** | Interpreter / VM / DomainEntityInstance | Does the runtime fail loud if the violation reaches it? |

**Fail-closed posture:** Empty sets, missing matches, and invalid configurations **fail loud** (throw / return error). Vacuous success (e.g. `all` on empty collection returning true) is **banned** unless explicitly documented as a design choice.

**Test-first is the layer underneath this table:** the red test must fail at the layer that should catch the defect. If analyze-time should reject it and the test only fails at runtime, the finding is not closed — move the oracle up.

### Step 4 — Fix red → green

For each finding:
1. Land the reviewer's test in the owning test project **verbatim** (test code only) and run it. It must fail the same way the review reported. If it passes or fails differently, the finding is invalid or unproven — do not rewrite the test to match current behavior.
2. Capture the exact command and failure in the finding list.
3. Change production code with the **smallest** fix that makes it pass (per §4).
4. Re-run the named test, then the full suite. Under green, remove duplication and special cases.
5. Keep the repro: never delete, skip, weaken, or retarget it to match current behavior. If its expectation was wrong, dispose the finding `invalid` explicitly.

### Step 5 — Re-review

After all fixes are applied, collect the fix delta with the same evidence as
Step 1 (status, diff, and untracked files) and re-read it:

- No 🔴 or 🟠 finding remains open.
- Each closed finding names its green repro; nothing is closed on a code read alone.
- During the loop, the only failing tests are the filed repros.
- 🟡 findings should be resolved or explicitly filed as follow-ups under `docs/` (phenomenal §5.2); they are evidence, not admitted work.
- Hygiene findings should be clean unless deferred with a note

Re-review only the fix delta. Steps 1–2 are the gate's one review pass; Step 5 is
not a second full review. A regression outside the edited lines still counts as a
finding.

### Step 6 — Ship

Only mark the suite/slice "Done" when:
- [ ] The Step 1 evidence (status, diff, and the contents of every untracked file) shows no dirty file belonging to **this task**, and every other dirty file is itemized in the finding list with its category — a preserved pre-existing edit, drift from an earlier task, or named next-task work; no dirty file is left unnamed. A review note or follow-ups written for this gate belong to this task and are **committed before this box is checked**, not left dirty and itemized as someone else's
- [ ] All 🔴 and 🟠 findings resolved, each with a named green repro test
- [ ] No finding is `open (unproven)` or `pending red`; no repro was deleted, skipped, weakened, or retargeted
- [ ] 🟡 findings filed or resolved — filed means under `docs/` (phenomenal §5.2), which is evidence, not admitted work
- [ ] Hygiene clean or deferred with a doc note
- [ ] `dotnet build` passes
- [ ] Full suite passes (`dotnet run --project Poly.Tests/Poly.Tests.csproj`)

## Verification (of this process)

- [ ] The checklist in Step 6 is complete before any "Done" marker
- [ ] Every finding has a captured red → green transcript for its named test/check
- [ ] Three-layer defense is verified for each contract finding
- [ ] No vacuous success paths are left unaddressed

## Output

- Findings list (written to agent summary or task notes), each with its red repro output
- Fix commits (one or more) that turn those repros green
- Re-review confirmation
- A suite or slice ships only after it passes the Step 6 gate

