# TASK parse-first - Resolve: move parse-time meaning out of parser; retire PeekExtensions

Status: merged #326 `6501436d` 2026-10-25. Branch slice/parse-first. Lane A. Review 2. Implement mill: OpenCode (review on Grok).
Plan card: [#310](https://github.com/scoizzle/Poly/issues/310) under umbrella [#305](https://github.com/scoizzle/Poly/issues/305). Decisions #14 (parse-first), #15 (stage name Resolve) and #16 (DomainModeling assumes nothing about simulation) are accepted in #305; this slice implements #14/#15 and changes neither.

## Scope
- Today the `uses` ids are discovered by a pre-parse token scan, `DomainCompilation.PeekExtensions` (`Poly/DomainModeling/Compile/DomainCompilation.cs:12-34`), so `DomainSession.ForSource` (`Poly/DomainModeling/Compile/DomainSession.cs:151-162`), `ForAuthoringSource` (`:164-191`), `DslCompiler.OpenCompileSession` (`src/Poly.DslCompiler/DslCompiler.cs:256-306`) and MCP `ApplyDsl` (`Poly.Mcp/Tools/DomainTools.cs:1309-1317`) all scan the text, load, then parse.
- The parse itself is one call that already carries every library meaning: `PolyDslParser(text, session)` reads `session.Language`, `session.Annotations`, `session.ExpressionForms`/`Folds`, `session.PrimitiveSeeds` and `ExtensionCatalog.Core` (`Poly/DomainModeling/Language/PolyDslParser.cs:57-82`, `:136-159`, `:423-439`, `:1305-1345`), and `DslExpressionParser` folds durations, `Now`/`Today` and `±` from those tables (`Poly/DomainModeling/Language/DslExpressionParser.cs:13-24`, `:36-41`, `:115-122`).
- No shipped library adds a grammar production, so the core table already tokenizes every product document (`Poly/DomainModeling/Language/DslGrammar.cs:110-123`). The parse stage can therefore be a real, session-free stage: tokenize + header (name, `uses`) into `DslSyntaxTree`, then Load from `tree.Uses`, then `DomainResolver` applies all library meaning over the same token tree.
- Keep `PolyDslParser` as a 20-line facade (`ParseDocument` + `DomainResolver`) so the 216 existing `new PolyDslParser(...).Parse()` call sites compile unchanged; the producers (`DslCompiler`, MCP, `DomainSession.ForSource`) use the explicit tree → Load → Resolve path.

## Files
- `Poly/DomainModeling/Language/DslSyntaxTree.cs` (new: `record DslSyntaxTree(string Name, IReadOnlyList<string> Uses, IReadOnlyList<DslToken> Tokens)` + `static Parse(string)`)
- `Poly/DomainModeling/Language/DomainResolver.cs` (new: the `PolyDslParser` body moved here; `Resolve()` applies meaning)
- `Poly/DomainModeling/Language/PolyDslParser.cs` (slimmed to the facade; keep both public ctors and `Parse()`)
- `Poly/DomainModeling/Language/DslCursor.cs` (add a `Func<ITokenReader<...>, Matcher<...>>` ctor so the resolver can replay `tree.Tokens`; `:21-31`)
- `Poly/DomainModeling/Language/DslGrammar.cs` (doc comment `:9` names `DomainResolver`)
- `Poly/DomainModeling/Meaning/AnnotationRegistry.cs` (doc comment `:8` names `DomainResolver`)
- `Poly/DomainModeling/Compile/DomainCompilation.cs` (delete `PeekExtensions` `:12-34`; add `Resolve(DslSyntaxTree, DomainSession)`)
- `Poly/DomainModeling/Compile/DomainSession.cs` (add `ForDocument`/`ForAuthoringDocument`; `ForSource` `:151-162` and `ForAuthoringSource` `:164-191` delegate to them)
- `src/Poly.DslCompiler/DslCompiler.cs` (`CompileCore` parse/resolve `:122-129`; `OpenCompileSession` takes the tree `:256-306`)
- `Poly.Mcp/Tools/DomainTools.cs` (`ApplyDsl` `:1309-1317`)
- `Poly.Tests/DomainModeling/Parsing/ParseFirstTests.cs` (new)
- `Poly.Tests/DomainModeling/Packs/DomainExtensionTests.cs` (`Parse_DuplicateUses_FailsClosed` `:24-30`; `Parse_DateType_WithoutTemporal_UnknownType` `:134-143`)
- `Poly.Tests/DomainModeling/Packs/DomainSessionTests.cs` (add `ForDocument_LoadsUses` next to `ForSource_ParserAndPrinter_ShareGrammar` `:14-24`)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `uses temporal` + `Due: Date` | `PeekExtensions` scans the header, `ForSource` loads temporal, parser resolves `Date` from `IndexPrimitiveNames` in the same pass | `DslSyntaxTree.Parse` returns `Uses=["temporal"]` with no session; `DomainSession.ForDocument` loads; `DomainResolver.Resolve` resolves `Date` | `ParseFirstTests.DslSyntaxTree_Parse_UsesTemporal_NoSession`; `ParseFirstTests.Resolve_DueDate_WithTemporalSession_IsProperty` |
| `uses temporal` + `Now - 12 Days` in a policy | folded while parsing by `DslExpressionParser` from `session.Folds` (`:36-41`, `:115-122`), so the parser needs the temporal session | tree keeps the tokens; `DomainResolver` creates `DslExpressionParser` with `session.ExpressionForms`/`Folds` and folds to `DateOperation` | `ParseFirstTests.Resolve_Duration_WithTemporalSession_IsDateOperation` |
| `Now - 12 Days` without `uses temporal` | parser throws `FormatException` `"requires uses temporal"` at `DslExpressionParser.cs:127-128` | `DslSyntaxTree.Parse` succeeds; `Resolve` throws the same `FormatException` at the same message | `ParseFirstTests.Resolve_Duration_WithoutTemporalSession_Throws` |
| `column("CODE")` on a property | parser accepts only when `session.Annotations.CanAccept("column")` (`PolyDslParser.cs:423-439`); otherwise `FormatException` `"Unknown or unregistered annotation"` | parse accepts the annotation-shaped tokens; `DomainResolver` gates on the loaded registry and throws the same message | `ParseFirstTests.Resolve_ColumnAnnotation_WithoutPack_Throws`; `AnnotationRoundTripTests.ColumnAnnotation_ParsePrint_RoundTrips` |
| `many Order` / bare `Order` nav | `ResolvePendingNavs` runs inside `Parse` after all entities (`PolyDslParser.cs:1301-1345`) | same walk runs inside `Resolve`, over the same tree | `N1NavigationTests` (unchanged) |
| Duplicate `uses` | `PeekExtensions` throws `FormatException("Domain lists extension '{id}' more than once.")` before any parse | `DslSyntaxTree.Parse` throws the same message; no second scan exists | `ParseFirstTests.DslSyntaxTree_Parse_DuplicateUses_FailsClosed` |
| Any existing `new PolyDslParser(poly[, session]).Parse()` | one call does header scan, load-by-caller and resolve | facade = `DslSyntaxTree.Parse` + `DomainResolver`; for the no-session ctor it keeps `DomainSession.ForExtensions([])` so core-only behavior is byte-identical | `PolyDslRoundTripTests`; `DomainExtensionTests.Parse_DateType_WithUsesTemporal_SeedsTemporalPrimitives` |

## Done when
- New `Poly/DomainModeling/Language/DslSyntaxTree.cs`:
  `public sealed record DslSyntaxTree(string Name, IReadOnlyList<string> Uses, IReadOnlyList<DslToken> Tokens);`
  `public static DslSyntaxTree Parse(string text)` tokenizes once with `DslTokenReader` until `DslTokenKind.EndOfFile`, keeps every token in `Tokens`, reads `domain <Name>` and the leading `uses <id>*`, and throws `FormatException("Domain lists extension '{id}' more than once.")` on a duplicate. It reads no `DomainSession`, registry, fold table, meaning, primitive seed or `ExtensionCatalog`.
- New `Poly/DomainModeling/Language/DomainResolver.cs` holds the current `PolyDslParser` body: `public DomainResolver(DslSyntaxTree tree, DomainSession session)`, `public List<DomainChange> Resolve()`, and all expression folds, annotation acceptance, primitive indexing, type/property dispatch and navigation resolution now happen there. It replays `tree.Tokens` through `ListTokenReader<DslToken, DslTokenKind>`; `DslCursor` gains `public DslCursor(ITokenReader<DslToken, DslTokenKind> reader, Func<ITokenReader<DslToken, DslTokenKind>, Matcher<DslToken, DslTokenKind>> matcherFactory)`.
- `Poly/DomainModeling/Language/PolyDslParser.cs` is only the facade: `public PolyDslParser(string text)` forwards to `this(text, DomainSession.ForExtensions([]))`; `public PolyDslParser(string text, DomainSession session)` stores `DslSyntaxTree.Parse(text)` + the session; `public List<DomainChange> Parse()` returns `new DomainResolver(_tree, _session).Resolve()`. No grammar, fold, annotation, seed or navigation code remains in this file.
- `DomainCompilation.PeekExtensions` is deleted. `DomainCompilation.Resolve(DslSyntaxTree tree, DomainSession session)` returns `new DomainResolver(tree, session).Resolve()`.
- `DomainSession.ForDocument(DslSyntaxTree tree, IReadOnlyList<string> seed, ExtensionCatalog? catalog = null)` and `ForAuthoringDocument(...)` load from `tree.Uses` (falling back to `seed`, authoring also calls `StorageFacetLibrary.EnsureRegistered`). `ForSource` / `ForAuthoringSource` call `DslSyntaxTree.Parse` once and delegate; neither scans tokens itself.
- `src/Poly.DslCompiler/DslCompiler.cs`: `CompileCore` does `var tree = DslSyntaxTree.Parse(polyText);` once, `OpenCompileSession(tree, mode, dbms, extraLibraries)` reads `tree.Uses`, and changes come from `DomainCompilation.Resolve(tree, session)` before `WithSeed`. `Poly.Mcp/Tools/DomainTools.cs` does the same with `ForAuthoringDocument` + `Resolve`.
- `git grep -n "ExpressionForms\|ExpressionFoldTable\|AnnotationRegistry\|PrimitiveSeeds\|ExtensionCatalog\|DomainSession" -- Poly/DomainModeling/Language/DslSyntaxTree.cs` is empty; `git grep -n PeekExtensions -- Poly Poly.Tests src Poly.Mcp` is empty; the full suite is green.

## SHIP if / NOT SHIP if
SHIP if the new `ParseFirstTests` red run is recorded, `DslSyntaxTree.Parse` has no session parameter and no library table read, `PeekExtensions` is gone, `DslCompiler` and MCP parse once then load then resolve, every existing `new PolyDslParser(...).Parse()` call site is unchanged and green, and the full suite passes.
NOT SHIP if the parse stage still takes or reads a `DomainSession`, `PeekExtensions` survives as a wrapper, `DomainResolver` copies the parser or the pass list, a test is deleted or weakened, the facade still contains fold/annotation/nav logic, or any library primitive/annotation/fold is read before Load.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParseFirstTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainExtensionTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainSessionTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/PolyDslRoundTripTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/N1NavigationTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AnnotationRoundTripTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DurationExpressionFormTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NowExpressionFormTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TemporalPackPrintRoundTripTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/McpSmokeTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj` (full suite)

## Hand-edit
The parse stage is one static method over a token list, the resolver is the old parser body with its constructor changed, the facade is three lines, and the loaders read `tree.Uses`; no grammar, pass list or dispatch table is added.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-25 | planner | opencode/deepseek-v4.1-flash | 6f085887 | planned | S0/#325 merged; issues #310/#305 |
| 2026-10-25 | implementer | opencode/opencode-go/deepseek-v4.1-flash | 0ee50b29 | pushed | tests 4573/4575; sweep: header scan retired, 2 pre-existing InvariantCulture env failures (no ICU) |
| 2026-10-25 | planner | opencode/deepseek-v4.1-flash | 6501436d | merged #326 | squash-merged to integration/pipeline; S1 planned on top |
