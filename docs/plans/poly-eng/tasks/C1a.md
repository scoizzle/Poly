# TASK C1a — Interpreter: root program with declared parameters after `this`

Status: PR #110 (`d7b6b9a1`) carried forward on `cursor/c1a-root-program-params-4ad1`: master merged, CI-red cause fixed. Razor → Final Boss next.

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
| 2026-10-07 | implementer | Cursor agent | (this push) | Merged master `bfe66beb`; reproduced the 13 CI failures. Cause: PR 113's compiled AST calls (`InvokeAstFunction`) push the callee frame at the stack pointer, which a root program leaves at 0, so the call overwrote `this` and the C1a parameter slots. Fix: a root with SetArgs parameters raises the stack pointer past its frame on entry and drops it back at exit unless a return wrote a result. | full suite 3414/3414 (13 failed before the fix) |
