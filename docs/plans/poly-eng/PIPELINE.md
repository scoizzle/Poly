<!-- Canonical pipeline. Box paths (/home/box/..., /workspace/...) are the shared Grok Bot box where mills run; they are the one allowed exception to the repo-relative-links rule. -->
# Poly engineering pipeline (rebuilt 2026-10-07)

**One idea:** bots are dispatchers. All heavy work runs in Grok Build or OpenCode on the box, from a prompt file, with a turn cap and a timeout. Its output goes to the repo (commits, task file) and to PR comments. A bot turn only does three things: launch a run, read a one-line result, and pass a one-line baton.

## Stages

| # | Stage | Owner (launches) | Mill / model | Cap | Output | Next baton |
|---|-------|------------------|--------------|-----|--------|------------|
| 0 | Select | Foreman (no mill) | none | 1 turn | picks the next slice from `board.md` "Next" + open PRs | launches 1 |
| 1 | Plan | Foreman | OpenCode `deepseek-v4.1-flash` (else Grok) | 30 turns / 20 min | branch `slice/<id>`, commit 1 = `tasks/<id>.md` + board catch-up, **draft PR opened** | Foreman → 100x: `PR N \| tasks/<id>.md \| implement` |
| 2 | Implement | 100x | Grok Build `grok-4.6` (OpenCode only if Grok is down) | 80 turns / 60 min | commits, self-review sweep in PR comment, task-file log row, PR marked ready | 100x → Razor (Review 2) or Final Boss (Review 1) |
| 3 | Review | Razor | **opposite mill**: Grok-built → OpenCode `deepseek-v4.1-flash` (alt `mimo-v2.6-flash`/`mimo-v2.5`); OpenCode-built → Grok | 40 turns / 45 min | one PR comment: verdict + ONE findings table | Razor → 100x (findings) or → Final Boss (clean) |
| 4 | Fix | 100x | same mill as stage 2 | 50 turns / 45 min | commits, PR comment mapping each finding id → fixed / disputed, task log rows | 100x → Final Boss |
| 5 | Verify | Final Boss | opposite mill to the implementer | 30 turns / 30 min | PR comment `SHIP <sha>` or `NOT SHIP <sha>` + table; checks CI green on that SHA | FB → Chieftan (SHIP) or → 100x (NOT SHIP) |
| 6 | Merge | Chieftan (no mill) | none | 1-2 turns | `git ls-remote` tip == SHIP SHA, Scot's OK, squash merge with `sha` pin | Chieftan → Foreman: `merged N <sha>` (wakes stage 0) |

Ontologist runs `research.md` only when Foreman or Scot names a design question. Its output is a file on the slice branch or a PR comment.

## Budget and WIP rules

- **WIP:** 1 slice while the throttle is on, and 2 (one per plan lane, A and B) once Scot lifts it. No more than **2 mill runs at once** on the box.
- **Passes per slice:** Review-1 slices go implement → Final Boss. Review-2 slices go implement → Razor → one fix round → Final Boss. A second Final Boss NOT SHIP goes to Scot as a decision (waive, narrow or split). There is never a third fix round without that decision.
- **Re-review only on a new SHA,** and only of the listed findings plus real regressions.
- **No re-fires by bots.** `mill.sh` re-fires once by itself (a hang on OpenCode falls back to a free model). After that the run is failed, and the bot reports a one-line blocker. Nobody checks in with "how's it going".
- **No docs-only bookkeeping PRs.** Task-file and board catch-up ride on the next commit of a real slice branch.
- Implementer never reviews its own SHA. Chieftan never mills, reviews or clones.

## How a run is launched

Every run gets its own git worktree and run directory. Nothing runs in the shared checkout.

```bash
RUN=pr${PR}-${STAGE}-$(date +%m%d%H%M); D=/workspace/mill-runs/$RUN; mkdir -p $D
git -C /home/box/Poly fetch -q origin
git -C /home/box/Poly worktree add -q --detach $D/wt ${SHA:-origin/master}   # implement/fix: -B slice/<id> origin/slice/<id>
awk 'f;/^=== PROMPT ===$/{f=1}' /home/box/agent-data/workflows/poly-mills/<template>.md \
  | sed -e "s|{PR}|$PR|g; s|{SHA}|$SHA|g; ..." > $D/prompt.md     # full placeholder list: mills/README.md
/home/box/agent-data/projects/poly/bin/mill.sh --mill grok|opencode --role implement|review [--impl-mill grok|opencode|hand] \
    --cwd $D/wt --task-file $D/prompt.md --run-id $RUN --timeout 3600 --stall 900 [--max-turns 80] > $D/launch.out 2>&1 &
```

Raw commands (flags checked against `--help` and smoke-tested on 2026-10-07; mill.sh wraps these):
- Grok Build 1.0.41: `grok --prompt-file $D/prompt.md --cwd $D/wt -m grok-4.6 --reasoning-effort high --always-approve --max-turns N --output-format json </dev/null > $D/result.json`. The JSON gives `text`, `num_turns` and `usage`; mill.sh writes it to `result<N>.json` and copies turns, tokens and cost into `status`.
- OpenCode 1.18.33: `OPENCODE_DB=$HOME/.local/share/opencode/mill-$RUN.db opencode run --dir $D/wt -m opencode-go/deepseek-v4.1-flash --auto --title $RUN "$(cat $D/prompt.md)" </dev/null > $D/out.log 2>&1`. **`</dev/null` is required:** with stdin left open, `opencode run` hangs forever with no output (reproduced today with three models; mill.sh already redirects it). OpenCode has no turn-cap flag, so only the timeout and stall limits apply.

**How the bot learns a run finished:** the background shell exits, and that completion wakes the bot that launched it. The bot reads only the last line of `launch.out` (the `MILL done|failed|blocked ...` line) plus the PR's latest comment header. It does not read diffs or logs. PR comments do **not** wake anyone, because the PR events routine only fires on pr-opened, pr-merged, pr-closed and ci-failed. That makes the bot's one-line baton the hand-off.

**If a run hangs:** mill.sh kills it after `--stall` seconds with no log, file or commit activity, or at `--timeout`, then re-fires once. If it still fails, the launching bot sends Foreman `PR N | stage | failed: <reason> | log path`. Foreman picks one move: switch mill (implement or fix only; reviews must stay on the opposite mill, so they wait or go to Scot by hand), shrink the slice, or Needs-Scot. A down mill never blocks the other: keep implementing on the healthy one.

## Records (repo is the plan; GitHub is the live state)

- `docs/plans/poly-eng/board.md` holds the rules digest, the ordered **Next** queue, Needs-Scot and the Merged log. In-flight state is the open PR list, so the board is not edited per event.
- `tasks/<id>.md` (one per slice) holds scope, files, done-when, SHIP-if, shape matrix and a Log table. Mill runs append their own rows. Review verdicts live in PR comments, and the next commit on the branch copies them into the Log. The final SHIP/merge row is written by the next slice's plan commit.
- Review comments use one header line, `ROLE | SHA | mill/model | VERDICT | n findings`, then the table `id | severity | file:line | finding | fix`.

## What changed and why

| Old waste | Fix |
|-----------|-----|
| Bots read code, diffs and logs in their own chat turns | Bots read one result line; mills do every read in their own context |
| Many parallel lanes (WIP 2+, power hours) | WIP 1 during the throttle and 2 max after it; at most 2 mill runs at once |
| Anti-idle and anti-stall re-fires, plus re-fired reviews | mill.sh does one automatic re-fire; bots never re-fire; re-review only on a new SHA |
| Multi-hop routing (every step through Foreman) | Direct baton to the next owner; Foreman only selects and plans |
| FYI chatter (starting, acks, "merged" re-announces) | One line per stage change, nothing else (coordinator-reporting) |
| Reviews stalling for hours waiting on a ping | The reviewer is the next baton holder and launches right away; CI can run alongside review, and only SHIP needs green |
| Docs-only bookkeeping PRs | Bookkeeping rides on slice commits |
| Long prompts written in chat each time | Fixed prompt templates (`mills/*.md`) with placeholders |
| Shared checkout and /tmp state (worktrees in /tmp went stale when /tmp was wiped) | One worktree and run dir per run under `/workspace/mill-runs/` |

## Tooling status (mill.sh patched 2026-10-07; backup `mill.sh.bak-20261007`)

Done: (1) Grok uses `--prompt-file` + `--output-format json`, and turns, tokens, cost and stop reason go to `status` and the `MILL` line. (2) Run state goes to `$MILL_RUNS` (default `/workspace/mill-runs`), with nothing in `/tmp`. (3) `</dev/null` is on every CLI call. (4) `opencode-go/deepseek-v4-flash` is on the allowlist. Also fixed: mill.sh's own `git status` polling could take `index.lock` and make the mill's commit fail; it now uses `GIT_OPTIONAL_LOCKS=0`. `test-mill.sh` passes 17/17 (4 runs in a row). Real smoke runs of both mills through mill.sh returned `MILL done`.

Still open: `--push` checks every file changed since `origin/master` against `--allow-path`, which blocks any reviewer commit on a slice branch. That is why reviewers post PR comments only and never commit. Housekeeping: 66 stale `/workspace/Poly-*` worktrees are listed for later removal. After each merge, Foreman's select step runs `git -C /home/box/Poly worktree prune` and deletes `/workspace/mill-runs/pr<N>-*`.
