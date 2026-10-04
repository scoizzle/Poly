# PR 89 @ 7372c542: follow-ups (small hand edits; F150 blocks the A2b merge through the hand-edit gate, the rest do not)

Verdict NOT SHIP, bugs 0, gate NO (one false comment). The F132 bug is fixed. Fix F150 (one sentence, one test assertion), push to the same PR, and ask for a short re-check of just that comment and test; F151-F157 are test gaps and nits that can go in the same push or before A3a.

## Blocks ship (hand-edit gate)
1. **F150** In `ArtifactCatalog.cs` (the `DeclareType` summary, line 21) replace "a reference to it can only show up as dangling" with "a reference to it can never resolve (nothing can be registered with an undeclared type), so the finder reports it as dangling, or as wrong type when the path exists with another type". In `ArtifactCatalogTests.cs:336` rename `DeclareType_TargetTypeNeverDeclared_IsAccepted_AndItsReferencesAreDangling` (for example `..._AndItsReferencesNeverResolve`) and add the second case: register `G#t` (declare `t`), point `A#method` at `G#ghost`, expect `A#method->G#ghost WrongType`.

## Suggestions
2. **F151** Add two rows to `Descriptor_WithEqualReferences_IsEqualAndHashesAlike`: same references with a different producer, and with a different id, are not equal.
3. **F152** In `Register_KeepsItsOwnCopyOfTheReferences` add `((IList<ArtifactId>)registered)[0] = ...` and `.Clear()` through the cast, both expected to throw `NotSupportedException` (the existing `Add` assertion cannot tell a bare array from a read-only wrapper).

## Nits
F153 test that `M` and `m` are distinct declared types. F154 test a bad entry in the second position of `mayPointAt` and `Register` after a refused declaration. F155 optionally extend the exact-members surface check to `Artifact`, `ArtifactDescriptor` and `ArtifactReferenceProblem` (methods, constructors, enum values). F156 null `Id` / `Producer` / `Descriptor` give `NullReferenceException` at `Register`; null elements give `ArgumentException` in references and `FormatException` in `mayPointAt`. F157 add one row each for a target path that starts with a registered path and for two ids with the same path and different types in the finder tests. Carried over and skipped on purpose: F139 (update the plan card's A2b Files line at the next plan edit; A3a rewrites what `Lower` declares), F140 (two constructors; finder scan).
