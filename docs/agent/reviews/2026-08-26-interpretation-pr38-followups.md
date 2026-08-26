# Follow-ups — Interpretation PR #38 — 2026-08-26

Review: [`2026-08-26-interpretation-pr38-local-review.md`](./2026-08-26-interpretation-pr38-local-review.md)

Owning stream: Interpretation (VM emit + analysis) and C# printer only where fusion must stay honest.

## Prior open items (disposition)

- F1–F9 capture/kinds siblings (capture-analysis + declare-init reviews) — **still closed**.
- Declare-init printer residual "never-assigned `default(object)`" — **tracked** in [`2026-08-26-interpretation-declare-init-followups.md`](./2026-08-26-interpretation-declare-init-followups.md) (not a merge blocker).

## Closed this change

- [x] **F1 (bug)** — Stored `Invoke(fn)` arity must match the lambda exactly, including 0 vs N. `SetArgs` 0-arg remains immediate `Invoke(Lambda)` only. Analysis (`CheckInvokeArity`) + emit (`EmitInvokeIndirect`). Tests: `StoredLambda_ZeroArgsIntoArity1_*`, `StoredLambda_TooFewArgs_*`, `StoredLambda_TooManyArgs_*`.
- [x] **F2 (suggestion)** — Removed dead `GetResolvedMember` allow-list on `Variable`/`Parameter`. Stored lambda or analysis error only. `ResolveMethod` still requires `Delegate is Member`.
- [x] **F3 (suggestion)** — Unknown first write installs a sentinel; later typed write fails closed. Void assign to a variable errors. Test: `Assignment_UntypedThenLong_*`.
- [x] **F4 (suggestion)** — Two stored closures sharing one capture: `Stored_TwoClosuresShareOneCapture_SeesLatest`. Non-lambda reassign already `Invoke_AfterReassignFromLambdaToInt_*`.
- [x] **F5 (nit)** — Declare-init residual tracked as an open checkbox (not "Open: None").
- [x] **F6 (nit)** — No change. `ulong.MaxValue` round-trip and `NumericTypePromotion_Add_ULongAndInt` already cover the ABI bitcast.

## Process follow-ups (fix the loop)

- [x] **P1** — Stored vs immediate arity siblings are in `InvalidProgramTests`. Protocol §3.2a now names immediate `Invoke(Lambda)` vs stored `Invoke(fn)` and analyze vs emit.