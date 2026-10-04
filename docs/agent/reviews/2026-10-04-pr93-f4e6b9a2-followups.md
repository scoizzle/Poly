# PR 93 @ f4e6b9a2: follow-ups (all nits; none blocks the merge)

Verdict SHIP, bugs 0, hand-editability gate YES. F220-F224 are closed (F223 skipped honestly: the guard could never fire). N3 is on the V11 standing-merge list: Foreman may merge. Branch is BEHIND master `dac9a6a8`; merge is clean (checked) and the merged tree passes 3054 of 3054.

1. **F230** `CascadingErrorReportTests.cs` n/a legend (and the body's `n/a:` bullet): add "or only ones whose name is declared more than once". `mcp-library` Number-to-Text is n/a although `Amount` (on `Loan` and `Fine`) is referenced by `assign Amount to 0`; it is the only such cell of 22.
2. **F231** `IsReferencedElsewhere` comment: "its mentions are uses of the type, not of the property" -> "its mentions are almost all uses of the type".
3. **F232** Foreman, at merge: plan line 3 add `C4a (dac9a6a8)` to "Done so far" (PR 91 merged) and `N3 (<sha>)`; wave 0 Lane A `N3 (done, <sha>)`; wave 1 line 896 drop "(in review as PR 91; ...)".
