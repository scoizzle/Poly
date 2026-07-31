# Phenomenal review protocol

**Audience:** any agent or human.  
**Mode:** review only by default — **do not modify production or test code** unless the user explicitly asks to harden afterward.  
**Output:** structured findings **and** checkable follow-ups **in the repo docs** (not chat-only).

This protocol optimizes for **correctness, contracts, and evidence**. It is not a maintainability / “code judo” pass (use a separate quality audit for that) and not the pre-ship **fix** loop (see pre-ship gate).

**Influences:** process lessons from [Rewriting Bun in Rust](https://bun.com/blog/bun-in-rust) (adversarial multi-reviewer loops, split context, objective work queues, fix-the-process, semantic-port traps), plus field feedback from repeated use of this protocol (sibling paths, reachability, primary evidence, baseline re-verify, Pass B template). Adapted here as a **portable review bar**, not a rewrite recipe.

---

## 0. Invocation

The caller names a **target** and optionally a **mode**. Defaults if omitted:

| Target | Default |
|---|---|
| **Local** | Uncommitted changes: staged + unstaged + untracked (non-ignored) |
| **Branch** | `git diff $(git merge-base origin/main HEAD)..HEAD` (or `origin/master` if no `main`) |
| **PR** | Diff of the named PR if `gh` is available; else stop and say so |
| **Paths** | Only the listed paths within the chosen target |

| Mode | Default | Meaning |
|---|---|---|
| **standard** | yes | One thorough adversarial pass (this agent is **reviewer only**) |
| **multi** | if user asks for multi / 2× / adversarial pair | Independent second pass (or subagent) with **diff-only** context; merge findings (see §1.1) |
| **re-verify** | if prior review docs passed | Re-check open items against **current source**; do not rubber-stamp |

User may pass prior review docs to **re-verify**, not rubber-stamp.

**One-liner for any tool:**

```text
Read docs/agent/phenomenal-review.md and execute it against <local | branch | pr N | paths…>
[mode: standard | multi].
Write findings and follow-up tasks into the docs as the protocol requires.
Assume the code is wrong until evidence says otherwise.
```

---

## 1. Role and bar

You are an **adversarial reviewer**, not a co-author polishing a PR.

### 1.1 Split context (non-negotiable)

From the Bun rewrite: the implementer wants the change accepted; the reviewer wants to find why it fails. Those roles must not share a self-justifying narrative.

| Rule | Practice |
|---|---|
| **Reviewer is not implementer** | Do not defend the change. Do not invent reasons it “probably works.” |
| **Prefer diff-first** | Start from the unified diff. Load surrounding source only to prove or disprove a failure mode — not to re-derive the author’s design story. |
| **If you also wrote the code** | Prefer **mode: multi**: spawn (or ask for) a second review context that receives **only** the diff + this protocol + required project maps — **not** the session’s implementation chat. Merge both issue lists. |
| **Multi-reviewer merge** | Deduplicate by (file, failure mode). Keep the stronger severity. Never drop a bug because another reviewer “liked” the approach. |
| **Primary evidence only** | Do not chain-trust prior review quotes, chat summaries, or “~N” counts. Re-read current source, re-run greps, re-`git show` baselines yourself (§3.8). |

Default success metric is unchanged: a second engineer can implement fixes from the follow-ups doc alone.

### 1.2 Stance

- **Assume the code is wrong** until a concrete path is shown correct (happy path + fail path + key invariants).
- Prefer real behavioral / contract bugs over style nits.
- Do not invent issues to fill space.
- Empty `## Issues` is allowed only after an honest adversarial pass.
- Every issue needs `path:line` in the **post-change** tree.
- Severity: **bug** | **suggestion** | **nit** (see §6).
- Tone: direct, no emojis, no vague “consider improving” without a failure mode.
- **Paragraph-long workaround comments** (or plan notes that justify residual danger as “OK for now” without a tracked follow-up) are a smell: prefer flagging the underlying wrongness (Bun’s review rule: if you need a long comment to justify a workaround, the code is wrong).

---

## 2. Required context (load before judging)

Always:

1. Workspace agent instructions (`Agents.md` / `AGENTS.md` if present).
2. Platform map if the change touches core machinery (`docs/CORE.md` in this repo).
3. Active plan / suite README for the work under review (if any).
4. Any **prior review or follow-up files** — re-check each open item against **current source**.
5. **Objective oracles** when present: compiler/typecheck output, failing tests, ASAN/leak notes, CI logs the user points at. Treat these as a **work queue** of claims to prove or file — not as noise.

Repo-local principles that usually apply (adapt names if the project differs):

| Principle | Review implication |
|---|---|
| Fail closed | Missing config, empty match sets, absent metadata → loud error, not silent empty success |
| Ownership / seams | Logic lives in the module that owns the concern; no silent side paths |
| Tests prove contracts | “Fail-closed” tests must withhold the dependency and assert the throw/error |
| Plan honesty | `[x] Complete` / gate checks must match residual markers and real AC |
| Follow-ups in docs | Residuals become tasks under `docs/plans/` (or suite-specific follow-up file) |
| Fix the process | Recurring bug classes → improve this protocol / suite gate / tests, not only one-line patches (file as follow-up) |

### 2.1 Feedback-loop ranking

Earlier detection beats later. When reviewing, prefer findings that move detection **up** this ladder:

1. **Type / compile / analyze-time** — impossible states unrepresentable or rejected before run  
2. **Unit / contract tests** — illegal state constructed and asserted  
3. **Integration / suite** — end-to-end path fails loud  
4. **Fuzz / ASAN / stress** (if relevant) — classes of corruption or leak  
5. **“Looks fine in chat”** — weakest; never the only assurance for contract claims  

Flag changes that **delete, skip, or weaken** tests without replacing the oracle (Bun bar: large ports kept 0 tests skipped or deleted).

---

## 3. Method (strict order)

### 3.1 Collect the diff

```bash
git status --porcelain
git diff --stat HEAD          # local tracked
git diff HEAD
# untracked source: inspect or diff --no-index as needed; skip binaries / local DBs unless ship risk
```

For branch/PR targets, use the merge-base or `gh pr diff` as appropriate.  
**Size gate:** if the unified diff is multi‑MB of junk (e.g. untracked `node_modules`, datasets), stop and ask to ignore or clean — do not pretend to review it.

Note **scope drift**: files that do not belong to the claimed task (scratch demos, generated one-offs, local DBs).

If the change is a large mechanical port/refactor, also look for **porting artifacts** (mapping docs, lifetime tables, phase notes). Review those for internal contradiction the same way as code.

### 3.2 Build the call graph (not only the hunk)

For each meaningful change, answer:

1. **Who produces** the data/metadata/config this path needs?
2. **Who consumes** it — and do keys/IDs/types **match** the producer?
3. What happens when the dependency is **null**, **partial**, or **element not found**?
4. Are “not found” and “required contract missing” **distinct** outcomes?
5. **Lifetime / cleanup / once-only** (when relevant): who frees or completes the resource; error paths; double-run; early return before cleanup?

Trace **end-to-end paths** the change claims to improve (examples — use what the diff actually touches):

- Lookup / resolve → execute / dispatch  
- Analyze / compile → lower / export  
- API / tool surface → domain / store  
- Mutation / transition → subscribers / side effects  
- Async / callback / re-entrancy into shared structures (if present)

Read surrounding source with `read_file` (or equivalent); the hunk alone is not enough — but do **not** replace adversarial reading with the author’s README of intent.

### 3.2a Sibling-path check (mandatory for dual-path / fallback work)

When a change fixes or claims a semantic (policy resolve, stage lookup, SA fall-through, fail-closed throw, etc.), **list every code path that implements that same semantic** — e.g. metadata-primary **and** residual scan, analyze-present **and** analyze-absent, entity-level **and** stage-level.

For each sibling path:

1. Does the fix / invariant hold there too, or only on the branch the author edited?
2. Is there a regression test that **forces that sibling path** (not only the happy metadata path)?
3. If an **invariant-stating comment** documents the rule, treat the comment as a **checklist**: every sibling path must satisfy the invariant or the comment is a lie — file a bug.

Classic miss: metadata branch fixed; scan/`DM-META-REMOVE-FALLBACK` branch keeps the old buggy shape; tests only exercise the metadata branch.

### 3.2b Reachability before severity (fail-loud / throw changes)

Before labeling a new throw, hard fail, or “fail closed” change:

| Question | If answer is… |
|---|---|
| On **valid** domains / configs after normal analyze, can this throw still fire? | **Yes** → usually **bug** (breaks good inputs) or must be intentional product change with tests |
| Only on **corrupt / stripped / missing** required metadata? | Document that; severity depends on whether callers can hit it |
| Is the throw **dead** because an earlier check always returns, or the state is unrepresentable? | Do not claim a high-severity contract win; demote to **nit/suggestion** or “unreachable on valid trees” with evidence |
| Does severity depend on “unreachable on valid domains”? | **Prove** reachability (or unreachability) from call graph + how analysis is produced — do not guess |

Fail-loud is not automatically a **bug** or automatically a **win**. Severity follows **who can reach it** and **what inputs are legal**.

### 3.3 Hunt these bug classes (priority order)

| Class | What to look for |
|---|---|
| **Key / identity mismatch** | Written under one key (node, id, map), read under another; always-null lookups |
| **Vacuous success** | Empty list, `continue`, soft `return` when required state is missing |
| **Semantics drift** | Helper name promises “effective X” but aggregates wrong set; primary path and fallback disagree |
| **Sibling-path drift** | Fix applied on one branch of a dual path; sibling (scan, fallback, other scope) still wrong or untested (§3.2a) |
| **Present-but-soft** | Analysis/config present, required piece missing → falls through to legacy scan instead of error |
| **Test theater** | Test name claims fail-closed / missing-X; body never strips X or never reaches the SUT (e.g. early-return no-op) |
| **Plan / docs lies** | Phase/gate marked complete while AC unchecked, residual TODO markers remain, ghost test names |
| **Contract regression** | Prior throw → silent skip; counts or describe output wrong only when the “new” path is active |
| **Same shape, different meaning** | Port/refactor traps: side effects only in debug asserts; eager vs lazy args; bounds checks added/removed; `trunc` vs `floor`; macro/comptime vs runtime evaluation; “looks identical” across languages or layers (Bun post-port regressions) |
| **Cleanup / once-only** | Leak on error path; double-free; async close while stack still owns the object; refcount underflow pinning GC roots |
| **Oracle weakening** | Tests skipped, deleted, or made always-pass; stubs that compile but do nothing; “smoke” without assertions |
| **Unproven reachability** | Severity for a throw/fail-loud change asserted without a reachability argument (§3.2b) |
| **Derived-count fiction** | Aggregates (“~36 markers”, “all paths covered”) not recomputed from primary greps (§3.8) |
| **Ship noise** | Unrelated WIP, binaries, generated dumps mixed into the change set |

### 3.4 Audit tests

- Happy path ≠ contract test.
- Prefer tests that **construct the illegal state** (remove metadata, null analysis, empty map) and assert the **exact** failure mode.
- Flag tests that pass for the wrong reason (same-state transition, never calling the method under test).
- For large diffs: **no silent test deletion/skip** without an explicit, reviewed replacement oracle.
- If the suite is the product’s confidence story (language-independent or not), treat green as evidence only when tests **actually run** the changed path.
- **Sibling-path coverage:** for each dual path (§3.2a), ask whether any test forces that path. A single metadata-happy test does not cover the scan branch.

### 3.5 Audit plan / status docs in the same change

If the diff updates task/gate docs:

- Does status match residual work (`TODO`, fallback markers, unchecked AC)?
- Do progress notes cite **real** test and type names from the tree?
- Are reopenings explicit (`[~]`) instead of false `[x]`?
- Is residual danger tracked as a **follow-up task**, not only a long comment?

### 3.6 Optional verification (read-only)

If the environment allows, run the project’s standard build/test command and note failures **related to the change**. Do not “fix” them in review-only mode; file them as findings/follow-ups.

Treat compiler/analyzer errors and failing tests as a **queue of adversarial claims**: each item is either a confirmed bug, an environmental flake (say so), or out of scope (say so).

### 3.7 Multi-mode procedure (when requested)

1. **Pass A** (this context): full protocol; write draft issues.  
2. **Pass B** (fresh context/subagent): use the **Pass B prompt template** in §3.7.1 verbatim (fill placeholders only).  
3. **Merge** into one review file + one follow-ups file. Attribute multi-pass only if useful (`found by pass B`).  
4. Prefer **two reviewers** for high-risk surfaces (runtime memory/safety, metadata contracts, ports, large mechanical diffs).

#### 3.7.1 Pass B prompt template (copy into the subagent / second window)

Fill `<…>` only. Do **not** paste implementer chat, rationales, or prior review prose (paths to prior review files are OK for re-verify mode only).

```text
You are Pass B of an adversarial code review. You are NOT the implementer.

Read and obey in full:
  docs/agent/phenomenal-review.md

Target:
  <local | branch <name> | pr <n> | paths: …>

Diff (primary evidence — start here):
  <path to unified diff file, or: run git diff HEAD / gh pr diff N yourself>

Required maps (if present in repo):
  Agents.md (or AGENTS.md), docs/CORE.md if core machinery touched,
  active suite README / follow-ups paths if any: <list or "none">

Hard rules:
  - Assume the code is wrong until a concrete path is shown correct.
  - Prefer the diff first; open source files only to prove/disprove a failure mode.
  - Do NOT implement, refactor, or “fix while reviewing.”
  - Do NOT defend the change or invent why it “probably works.”
  - Sibling-path check: for every dual path (metadata vs scan, etc.), verify the
    invariant on ALL siblings and whether tests force each sibling.
  - Reachability before severity: for any new throw/fail-loud, argue who can hit it
    on valid vs corrupt inputs before choosing bug vs suggestion vs nit.
  - Primary evidence: recompute counts with your own greps; re-read current files;
    if comparing to HEAD, run git show / git diff yourself — do not trust quoted
    baselines from other reviews.
  - Invariant-stating comments are checklists: every sibling path must satisfy them.

Deliverables for Pass B (return in your reply; parent will merge into docs/):
  ## Pass B Summary
  (2–4 sentences)
  ## Pass B Issues
  ### Issue … -- Severity: bug|suggestion|nit
  - File: path:LINE
  - Description: …
  - Suggestion: …
  - Status: open

Empty Issues only after an honest adversarial pass.
```

### 3.8 Primary evidence (no chain-trust)

| Claim type | Required evidence |
|---|---|
| **“Still broken at line N”** | Your `read_file` / grep of **current** tree, not a quote from r1/r2/r3 |
| **“HEAD used to throw X”** | `git show HEAD:path` or `git diff HEAD -- path` run **by you** this session |
| **“~N markers / N call sites”** | Recompute: per-file greps, then **sum**; write the exact total (not “~36” from memory) |
| **“Prior review said fixed”** | Re-verify against current source; disposition fixed/open/invalid yourself |
| **Derived aggregates in plans** | Same — re-grep before repeating a number into the new review note |

Chain-trusting a previous review’s quote of a baseline is how wrong evidence propagates (e.g. “B-2 same class as B-1” only if **you** still see the sibling path wrong).

### 3.9 Invariant-stating comments

When the diff adds or keeps comments of the form “always X”, “must Y”, “effective = entity+stage only”, “fail closed when Z”:

1. Extract the **invariant** as a bullet list.
2. Run the **sibling-path check** (§3.2a) against that list.
3. If any path violates the comment, file a **bug** (comment is false advertising) or require the comment be narrowed — do not leave a lying invariant.

---

## 4. What not to do

- Do not implement fixes unless the user switches you to harden/pre-ship mode.
- Do not rubber-stamp a prior review: re-verify or mark fixed/invalid with evidence **you** collected.
- Do not flood with nits when bugs exist — lead with bugs.
- Do not expand scope into a full maintainability redesign unless the user asked for that skill.
- Do not accept “it compiles” or “tests pass” as proof without checking **which** paths the suite exercises.
- Do not let the implementer’s rationale substitute for a failure-mode analysis.
- Do not “stub to green” thinking — flag stubs, `todo!`, empty catches, and deleted assertions as bugs when they weaken the oracle.
- Do not assign severity to fail-loud changes without a **reachability** argument (§3.2b).
- Do not declare a dual-path fix done after checking only one branch (§3.2a).
- Do not paste approximate counts; recompute from primary greps (§3.8).

---

## 5. Deliverables (both required)

### 5.1 Review note in docs

Write under **`docs/`** (prefer a stable convention):

| Situation | Path |
|---|---|
| Work has an active suite (e.g. `docs/plans/simple-agent-tasks/`) | `<suite-prefix>-local-review-YYYY-MM-DD.md` or append `rN` to an existing review file |
| No suite | `docs/agent/reviews/YYYY-MM-DD-<short-slug>.md` (create `reviews/` if needed) |

**Required structure:**

```markdown
# <Title> — YYYY-MM-DD

- **Target**: local | branch <name> | PR <n> | paths …
- **Mode**: standard | multi
- **Issue counts**: N bugs, M suggestions, K nits
- **Verdict**: one line (adversarial: ship / not ship / ship only with F… closed)
- **Process notes** (optional): process gaps that caused or would re-cause the bugs

## Summary

2–4 sentences: what the change does, correctness posture, dominant risks.
Call out oracle strength (tests/types) and any multi-pass merge.

## Issues

### Issue 1 -- Severity: bug
- File: path/to/file.ext:LINE
- Description: what is wrong and why it matters (evidence / failure mode)
- Suggestion: concrete fix (prefer moving detection earlier on the feedback ladder)
- Status: open
```

### 5.2 Follow-up tasks in docs

**Chat is not the system of record.**

1. Create or update a follow-ups file next to the suite (e.g. `<prefix>-followups-YYYY-MM-DD.md`) or under `docs/agent/reviews/`.
2. Every open **bug** and material **suggestion** becomes a checkable task (`- [ ] **F#** — …`) with file, do-statement, and owning plan phase if any.
3. Disposition prior open items: **fixed** / **still open** / **invalid** with one-line evidence.
4. Update the suite README or plan status table if reopenings change phase/gate state — **do not** mark complete unless true.
5. If the suite states “all follow-ups go into the docs,” obey that rule literally.
6. **Process follow-ups:** if the same bug class keeps recurring (e.g. metadata key mismatch, test theater), add an explicit task to tighten gates, tests, or this protocol — not only a one-off code fix.

### 5.3 User-facing report

In the conversation, give a short report: verdict, counts, path to review file, path to follow-ups, top 3–5 issues. Point the next agent at the follow-ups file. If multi-mode, note both passes.

---

## 6. Severity definitions

| Severity | Use when |
|---|---|
| **bug** | Wrong behavior, silent no-op, false-green test, contract regression, incorrect API/tool output under the intended path, UAF/leak/double-free class issues, same-shape-different-meaning regressions, **sibling path still wrong** after a claimed fix, invariant comment violated by another branch |
| **suggestion** | Incomplete fail-closed, incomplete coverage of a metadata/API surface, overstated plan status, missing test for a real risk, detection only at a late feedback rung when an earlier one is feasible, fail-loud that is **only** reachable on deliberately corrupt inputs (state that explicitly after reachability analysis) |
| **nit** | Dead code, redundant work, naming drift, comment wrong — only if true; never as padding; unreachable-on-valid-tree throws with **proven** unreachability may land here or as suggestion, not as a triumphant contract bug |

When unsure between **bug** and **suggestion** for a throw: complete §3.2b first; default to **bug** only if valid product inputs can hit a wrong outcome (including silent wrong success on a sibling path).

---

## 7. Relationship to other processes

| Process | Difference |
|---|---|
| **This protocol** | Deep **adversarial** correctness review; docs artifacts; no fix by default; optional multi-pass split context |
| **Pre-ship gate** | After (or instead of) review when shipping: categorize → **fix** 🔴/🟠 → re-review → green build/tests |
| **Implementer loop** | Write code; must not self-approve under this bar without a separate reviewer context |
| **Maintainability / code-judo audit** | Structure, file size, abstraction quality — separate pass |
| **Tool-specific “/review” UIs** | May collect diffs or post PR comments; they should still **execute this bar**, prefer **split-context** reviewers, and **land follow-ups in docs** |

Engineering work as a loop (Bun’s simplification): `task → implement → review(s) → apply feedback`. This document is the **`review`** step. When reviews repeatedly miss the same class, **edit the loop** (tests, analyzers, this protocol), not only the last patch.

---

## 8. Poly-specific hooks (this repository)

When reviewing Poly DomainModeling / MCP / lowering:

- Prefer metadata-backed semantic resolution when analysis is present; residual tree scans should be explicit, tagged, and not pretend to be “fail-closed complete.”
- `GetMetadata` / `SetMetadata` **keys must agree** (domain node vs default vs entity).
- Runtime notify/dispatch: missing required runtime metadata should throw, not skip all subscribers.
- MCP/oracle describe routes: “analysis present + metadata missing” ≠ “element not found.”
- Suite example for follow-up discipline: `docs/plans/simple-agent-tasks/dacr-README.md` hard rule on docs follow-ups; active residuals in `dacr-followups-*.md`.
- Analysis pipeline is an early feedback rung — prefer analyze-time catch over runtime soft-scan.

Build/test (when verifying):

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

---

## 9. Checklist (copy into the review note if useful)

- [ ] Diff collected; scope drift noted
- [ ] Stance: adversarial / assume wrong; split-context rules applied (multi if required)
- [ ] Producer/consumer keys traced for new lookups
- [ ] Null / partial / not-found / missing-contract outcomes distinct
- [ ] Sibling-path check done for dual paths; tests force each sibling (§3.2a)
- [ ] Fail-loud / throw changes have reachability → severity (§3.2b)
- [ ] Invariant-stating comments checked against all siblings (§3.9)
- [ ] Counts and HEAD baselines recomputed by you (§3.8) — no chain-trust of prior reviews
- [ ] Same-shape-different-meaning and cleanup/once-only considered where relevant
- [ ] Fail-closed tests actually strip dependencies
- [ ] Oracles not weakened (no silent skip/delete/stub-to-green)
- [ ] Plan/gate status matches residual work
- [ ] Review file written under `docs/`
- [ ] Follow-up tasks written under `docs/` with checkboxes (including process fixes if recurring)
- [ ] Prior follow-ups dispositioned from **current** source
- [ ] Pass B used template §3.7.1 when mode is multi
- [ ] User given paths + top issues

---

## 10. Source lessons (summary)

### From [bun.com/blog/bun-in-rust](https://bun.com/blog/bun-in-rust)

| Lesson | Protocol section |
|---|---|
| Separate implementer vs reviewer context; reviewer told to find bugs | §1.1, §3.7 |
| Multiple adversarial reviewers on risky diffs | §0 multi mode, §3.7 |
| Objective queues (compiler errors, failing tests) | §2.1, §3.6 |
| Large change confidence needs strong, real test oracles | §2.1, §3.4 |
| Fix the generating/review process when bugs recur | §2 table, §5.2 item 6 |
| Same syntax, different semantics across layers/languages | §3.3 same-shape class |
| Long workaround comments hide wrong code | §1.2 |
| Earlier safety (types/tests) beats post-merge firefighting | §2.1 feedback ladder |

### From field use of this protocol (agent feedback)

| Lesson | Root-cause pattern | Protocol section |
|---|---|---|
| **Sibling-path check** | Fix lands on metadata branch; scan/fallback keeps buggy shape; untested | §3.2a, §3.3, §3.4 |
| **Reachability before severity** | Fail-loud severity depends on whether throws are reachable on valid domains | §3.2b, §6 |
| **Recompute derived aggregates** | “~36” written without summing per-file greps | §3.8 |
| **Re-verify HEAD baselines yourself** | Chain-trusted a prior review’s quote of old code | §3.8, §1.1 |
| **Invariant comments = checklist** | Comment states invariant; sibling path violates it | §3.9, §3.2a |
| **Reusable Pass B prompt** | Diff-only “assume wrong / don’t implement” works when templated | §3.7.1 |
