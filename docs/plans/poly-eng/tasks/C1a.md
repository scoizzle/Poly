# TASK C1a — Interpreter: root program with declared parameters after `this`

Status: merged #110 `b1fddf7e` 2026-10-08.

Branch from master `dd7d4204` (or later). Lane B. Review 2. Not V11. Plan ~line 374.

## Scope
First of three parts of old C1 (P3-D). Allow `IsCompiledFunctionBody` mode for the root program so parameters are real slots (not parameter 0 aliasing `this`). Removes the `BindForSimulate` arm for action parameters and `previousStage`. Allowed to touch `Poly/Interpretation`.

## Files
`Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs`, `Invoke.cs`, `Runtime/DomainEntityInstance.*`.

## Done when
Parity rows for an action with parameters and for `when all` previousStage (fires once). `BindForSimulate` no longer rewrites parameter references. Shape matrix + self-review. Smallest readable change.

## SHIP / NOT SHIP
SHIP if `when all` row passes and BindForSimulate no longer rewrites params. NOT SHIP if parameter 0 can still alias `this`.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-06 | Foreman | — | `d7b6b9a1` | Open, CI red, BLOCKED; rebase after PR 113 fixes | — |
| 2026-10-07 | restructure | - | `d7b6b9a1` | open #110 | still open, CI red, base `11287134` behind master |
| 2026-10-08 | reviewer | grok/grok-4.6 | `d7b6b9a1` | NOT SHIP | First merge origin/master (7943b5cc) into fix/c1a-root-program-params with a normal merge commit (no rebase, no force-push) and resolve conflicts keeping master's intent. Then fix the red CI on PR 110 (see gh pr checks 110 and the failing run logs) until the full suite is green locally. Findings: F1 merge master 7943b5cc; F2 red CI. |
| 2026-10-08 | implementer | grok/grok-4.6 | `9ac06fff` | fixes pushed | F1, F2; disputed: none; tests 3417/3417 |
| 2026-10-08 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | `6e3a05fd` | NOT SHIP | https://github.com/scoizzle/Poly/pull/110#issuecomment-6067249860 (Grug NOT SHIP at 6e3a05fd, 6 open findings, mode full) |
| 2026-10-08 | implementer | grok/grok-4.6 | `099659b5` | fixes pushed | R1, R2, R3, R4, R6; disputed: none; R5 skipped (nit >1 line); tests 3418/3418 |
| 2026-10-08 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | `98c1407c` | NOT SHIP | https://github.com/scoizzle/Poly/pull/110#issuecomment-6067693925 (Grug NOT SHIP at 98c1407c, 1 open finding, mode verify). Scot approved ONE extra fix round on PR 110, only for Grug finding R5. |
| 2026-10-08 | implementer | grok/grok-4.6 | `495ef41a` | fixes pushed | R5; disputed: none; tests 3418/3418 |
| 2026-10-08 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | `b22da6d3` | NOT SHIP | https://github.com/scoizzle/Poly/pull/110#issuecomment-6069811466 (Grug 0 open findings at b22da6d3, mode verify). CI never ran: PR 110 conflicts with master after PRs 124 and 125. Merge origin/master (99d97e46) with a normal merge commit. |
| 2026-10-08 | implementer | grok/grok-4.6 | `95ad8fb1` | fixes pushed | F1; disputed: none; tests 3418/3418 |
| 2026-10-08 | planner | grok/grok-4.6 | `b1fddf7e` | merged #110 | recorded from C1b plan |
