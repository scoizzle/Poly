# PR 84 @ f5ce4a95: follow-ups (all doc edits; none blocks ship)

Verdict SHIP, bugs 0. Fix these in the same PR if the author is already editing, otherwise at the named slice.

## Suggestions
1. **F95** Q1a (`plan:460`): drop "the H4 Constant check is present" from Q1a's done-when, or move the Constant check into Q1a's own test and let H4 reuse it. H4 comes after Q1a.
2. **F96** Wave 0 header (`plan:890`): replace "everything here is test-only, docs-only" with "test-only, docs-only or new code nothing calls yet" (A1, A2a, A2b are new unwired code).
3. **F97** K1 (`plan:664`): add done-when greps, expected empty, on `pipeline-stage-map.md`: "after PRs 82", "Suggested order after PRs 82", "findings-raw", "/workspace", "Not a PR", "no PR exists".

## Nits
F98 stage map `:3`: use squash SHAs `16a895dc` / `945a2164` (K1). F99 `plan:377`: write `DirectVmAbiEmitter.Invoke.cs:481`. F100 `plan:458`: definition is `:503`, calls `:475-488`, remark `:29-30`. F101 drop satisfied "PRs 82 and 83 merged" Depends text. F102 define "mill" once; make V11 "Scot can take them off" read "you can take them off"; note that "the five reviews" are not in the repo. F103 put the N1/K6 caveat in `plan:928` too; take Q1a out of the "risky" hand-merge list or leave it with a note. F104 `plan:593`: "C5a, C5b, C6a to C6c". F105 C6a: check every bare `create` in the guide (line 141 included), not just `73-75`.
