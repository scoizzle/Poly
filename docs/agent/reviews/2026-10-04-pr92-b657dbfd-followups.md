# PR 92 @ b657dbfd: follow-ups

Verdict NOT SHIP, bugs 0, hand-editability gate NO. The measurement and the product change are right; two comment claims are not (F190, F191). Everything below is a comment or PR-body edit except item 8. H1 is on the V11 standing-merge list: after a comment-only fixup and a short re-check, SHIP with gate YES means Foreman squash-merges.

Must fix (blocks ship):

1. **F190** `Poly.Tests/DomainModeling/Compile/VmAnalyzerReportTests.cs:10`: replace "Emit ignores the result today" with "Emit ignores the analysis's diagnostics today" (Emit passes the analysis to `new CSharpGenerator(interpAnalysis)`, `DomainSession.cs:173-176`). Optionally the same wording at `pipeline-convergence-plan.md:735`.
2. **F191** `VmAnalyzerReportTests.cs:21`: drop "most are old syntax or meant to be rejected". Say the archive is not maintained and 114 of its 256 files do not load or fail domain analysis. In the PR body fix the archive run (256 files, not 262; at the head: 142 VM Errors, 67 domain Errors, 47 failed to load, 0 clean, 0 null) and drop "Most of those probes were written to be rejected" before the body becomes the squash message.

Should fix in the same push (one line each):

3. **F192** line 24-25: "the domain analyzer rejects these"; the printed-C# rejection is `ParityTests.cs:254`, and the same list is at `ParityTests.cs:232`.
4. **F193** line 68-69: comment that this mirrors `DomainSession.Emit` lines 172-173.
5. **F194** class comment: add "Run with `--output Detailed` to see the table."
6. **F195** line 85: delete the dead `Poly.sln` check (repo has `Poly.slnx`). A later slice can move `FindRepoRoot` (now in nine test files) to one helper.
7. **F196** say in the PR body or comment that null means "no lowered types" and that analyzer exceptions show as `load failed`; later, fix the plan's "returns null on failure" (`pipeline-convergence-plan.md:727`).
8. **F197** put the invalid-by-design count in the summary, or rename "domain has Errors" to "domain rejected".

Not this PR's (plan housekeeping, for K6/H2 authors): the plan cites `DomainSession.cs:203-208` and `:169-174` (lines 196, 727); on master the method is at 205-210 and the Emit call and fallback at 172-176.

On merge (Foreman): plan line 3 "Done so far" gets `H1 (<merge sha>)` and the wave 0 Lane A line gets `H1 (done, <sha>)`; C4a (PR 91) stays "In review".

Re-check list: `git diff b657dbfd..<new tip>` is comments/PR body only; `git diff --check`; `dotnet test Poly.Tests -p:NuGetAudit=false --treenode-filter "/*/*/VmAnalyzerReportTests/*"` passes; table unchanged.
