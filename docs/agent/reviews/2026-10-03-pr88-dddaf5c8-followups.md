# PR 88 @ dddaf5c8: follow-ups (none blocks the A2a merge)

Verdict SHIP, bugs 0, gate YES. F121-F125 are closed. Everything below is optional polish, small hand edits, best done in the same PR if the author is editing anyway.

## Nits
1. **F126** (partial) null `Descriptor` gives `NullReferenceException` at `ArtifactCatalog.cs:17`; decide whether `Producer` may contain `|` or a newline (it breaks one-line-per-artifact) and either validate it or say it is trusted; add a `Find(null)` expectation. Do this before A2b/A3a start registering real producers.
2. **F127** Update the A2a "Files" line in `docs/domain-modeling/pipeline-convergence-plan.md` (line 179): `EmitGoldenTests.cs` and `ArtifactCatalogTests.cs` also reference the catalog; `Artifact.cs` is a second new file.
3. **F129** Rename `ToText_SortsOrdinallyByPathThenType_NotByTypeProducerOrCulture` (`ArtifactCatalogTests.cs:80`) so it says "by the `Path#Type` string" (`!` and `"` sort below `#`, so path `a!` comes before path `a` with a type), or add that as a one-line comment.
4. **F130** Optional: keep one `ReadOnlyCollection<Artifact>` in a field instead of calling `AsReadOnly()` on each access (`ArtifactCatalog.cs:12`), if a caller ever compares or loops on `Artifacts`.
5. **F131** In `PublicSurface_IsExactlyTheAllowList`: also assert the constructor takes no parameters (`type.GetConstructors().Single().GetParameters()` empty) so a single `ArtifactCatalog(List<Artifact>? seed = null)` cannot pass; filter `IsSpecialName` so each property is listed once.
6. **F128** (informational) when A2b/A3a add test files that name `Artifact`, they need `using Artifact = Poly.DomainModeling.Compile.Artifact;` because of `TUnit.Core.Artifact`.
