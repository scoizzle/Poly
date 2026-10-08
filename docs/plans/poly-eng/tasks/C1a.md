# TASK C1a — Interpreter: root program with declared parameters after `this`

Status: open PR #110 @ `d7b6b9a1`, CI red, base `11287134` behind master. Rebase + CI fix first, then Razor → Final Boss.

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
