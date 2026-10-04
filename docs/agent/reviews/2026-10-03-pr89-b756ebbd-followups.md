# PR 89 @ b756ebbd: follow-ups (small hand edits; F132 blocks the A2b merge, the rest do not)

Verdict NOT SHIP, bugs 1. Make the F132 change plus its test (about ten lines), push to the same PR, and ask for a verification review; the suggestions can go in the same push or a small PR before A3a, which is the first slice that registers real references.

## Bug (blocks ship)
1. **F132** In `ArtifactDescriptor.cs` copy the references when the descriptor is built, and reject null: `public IReadOnlyList<ArtifactId> References { get; } = References is null ? throw new ArgumentNullException(nameof(References)) : References.ToArray().AsReadOnly();` (verified in a scratch worktree: 18/18). Add `Register_KeepsItsOwnCopyOfTheReferences`: declare `method` (may point at `type`), register a `method` artifact built from a `List<ArtifactId>` with one `type` reference, add a second reference to the list afterwards, expect the registered descriptor to still have 1 reference. It fails on the head.

## Suggestions
2. **F133** Give `ArtifactDescriptor` value equality over `References` (override `Equals` / `GetHashCode` using `SequenceEqual`), or wrap the list in a small value type; test that two descriptors with equal references are equal and hash alike.
3. **F134** Validate declared type names (non-empty, no whitespace, `/` or `#`, as for `ArtifactId`; expose that rule from `ArtifactId`) in `DeclareType`, for the type and for each `mayPointAt` entry; null elements refused; either report a never-declared target type as its own kind in `FindDanglingOrWrongType`, or say in its summary that this case reads as `Dangling`. Tests: null, blank and malformed names, a null element, the `ghost` case, a case-only difference, the caller growing its `mayPointAt` after `DeclareType`.
4. **F135** Decide ownership: record the declaring producer in `DeclareType` and require it in `Register` (one dictionary, one comparison, one test), or reword the `DeclareType` summary and class summary to say it is by convention.
5. **F136** Sort the finder's report by `(From, Target)` ordinal and say so in the summary; de-duplicate or say duplicates repeat. Tests: the same catalog registered in both orders gives the same list; a target path that is a prefix of an existing path, and one that differs only by case, are `Dangling`.
6. **F137** Extend the surface allow-list test to `ArtifactDescriptor` and `ArtifactReferenceProblem` (members, no plain setters, no `List` properties), assert `References` is `IReadOnlyList<ArtifactId>`, pin the enum members.

## Nits
F138 test every `ThrowIfNull` (`DeclareType(null, ..)`, `DeclareType(.., null)`) and a null element in `References`. F139 update the A2b "Files" line in the plan (`DomainSession.cs` is also touched, one line) and add a test of what `Lower` declares. F140 drop the 2-argument `ArtifactDescriptor` constructor once real references arrive, or keep it on purpose; index paths if the finder ever runs over many artifacts.
