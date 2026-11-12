# Guide wrappers

Templates for the ```` ```poly ```` fences in [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../../Poly.Mcp/Docs/poly-dsl-guide.md)
that are fragments rather than whole documents. `GuideExamplesTests.EveryBlock_MatchesItsMarker`
splices one guide block at the `// <<guide-block:<ctx>>` line and compiles the result through the
same `PolyCompiler` path `poly_define` runs.

Rules (`GuideExamplesTests.Wrappers_AreHonest` enforces all of them):

- Exactly one placeholder line per file, and its context (`top`, `entity`, `expr`) must equal the
  marker's `fragment:<ctx>`. A wrapper is context, not a document: no `uses` line.
- A wrapper with a blank placeholder must analyse clean, and splicing `!!! this line is not poly !!!`
  must fail: a wrapper that cannot fail proves nothing about the blocks it hosts.
- Every wrapper is used by at least one block, and the count is pinned
  (`GuideBlocks.WrapperCount`).
- The file name ends in `.poly.tmpl`, never `.poly`. `StageEntryFactsTests` globs every `*.poly` in
  the repository and skips files that do not parse, so a wrapper named `.poly` would silently
  change the corpus sweep's skip budget.

Ids avoid `auth`, `schema`, `secret`, `credential`, `permission`, `migration` and `env` so the
review classifier does not treat a test fixture as a sensitive path.

## Placeholder contexts

Only `top` and `entity` exist. A `expr` context was designed and dropped: nothing used it
(tenet 7 — no seam without a second consumer), because the guide's expression-list blocks
are several expressions on separate lines and cannot be spliced into one policy body. If a
later slice wants those blocks verified one expression at a time, an `expr` wrapper is the
way in: add the context to `GuideMarker`, to the placeholder regex in `GuideBlocks`, and to
`Wrappers_AreHonest` in the same change.

## Re-measuring the short guide (A6)

`McpSmokeTests.GetDslGuide_ReturnsProductSurface` caps the short guide at 240 lines, and the
short guide is section 0 + section 10 + the golden workflow + the index. Section 0 carries
eight marked fences, so every marker line in section 0 costs one line of that budget.

Measured after the Q-10 markers landed: **239 lines, one line of margin.** To re-measure:

1. Run `/*/*/McpSmokeTests/GetDslGuide_ReturnsProductSurface*`. It fails when the guide passes
   240 lines, and it is the only cap.
2. For the exact number, run `/*/Poly.Tests.Docs/*/*` with a temporary
   `Assert.That(lines).IsEqualTo(<guess>)` in `Markers_DoNotBlowTheShortGuide`; the failure
   message prints the measured value. Remove the temporary pin afterwards: the count is not
   pinned, because a guide writer needs to be told by the cap, not by a test that pins a
   number nobody planned to keep.
3. A marker adds exactly one line. A blank line between the marker and the fence would add
   two, so markers sit directly above their fence.
