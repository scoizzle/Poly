# PR 86 @ ec0a5bbf: follow-ups (small hand edits; none blocks the A1 merge, except F117 if the gate clause "comments claim only what the code does" is read strictly)

Verdict SHIP, bugs 0. Do these in the same PR if the author is editing anyway, otherwise as a small PR before A2a, which is the first real consumer of `ArtifactId`.

## Suggestions
1. **F114** Decide the character set. Either reject control characters, format characters (`UnicodeCategory.Format`: ZWSP `U+200B`, BOM `U+FEFF`) and unpaired surrogates in `RequireValid` (`ArtifactId.cs:54-59`), or keep it permissive, say so in the `Create` summary, and add one test showing `Hotel\u200B` is accepted on purpose. Do it before A2a stores ids in a catalog.
2. **F115** Make the `Segments` assertion order-sensitive (`ArtifactIdTests.cs:12`: `IsEquivalentTo` ignores order; M16 reversed segments passes). Compare `string.Join("/", id.Segments)` or use `CollectionOrdering.Matching`. Add a one-name case (`Hotel#module`).
3. **F116** Add tests for `Parse(null)` and `Create(null, "m")` (`ArgumentNullException`) and for a null element and a null type (currently `FormatException`; pin the one you want). One sentence in the `Create` summary about null names.
4. **F117** Replace `ArtifactId.cs:6-7` with a statement of what the type is: holds only names and a type, no stage, body text or hash, so same-named stage actions are one artifact; the type is any valid string. Move "ids are derived on every compile" to the slice that derives them (A2a). This is the only item that can flip the hand-edit gate.

## Nits
F118 add tab and NBSP rows to `Parse_Malformed_Throws` (narrowing the check to space and `\n` still passes). F119 one assertion that two different ids have different hash codes. F120 cut `Segments` until a slice needs it, or store the segments at construction.
