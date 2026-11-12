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
