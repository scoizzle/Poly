# Grammar integration full-send (GI-4…7)

**Mode:** C — full send approved  
**Date:** 2026-08-07

## Scope

| Slice | Action |
|-------|--------|
| GI-4 | Lock E2 hybrid (structure Matcher + RD expr); no E1 this pass |
| GI-5 | Annotation dispatch via MatchRule; pack pattern hook on `DslGrammar.Build` |
| GI-6 | `DslTokenWriter` + Printer smoke (DomainDslPrinter stays domain-walk; round-trips green) |
| GI-7 | Delete `PolyDslTokenizer`; golden token tests; CORE placement |

Park: GI-8 JSON, GI-9 binary, E1 expr grammar.

## Files

- [MODIFY] `PolyDslParser.cs` — instance grammar; annotation MatchRule
- [MODIFY] `DslGrammar.cs` — optional configure callback
- [MODIFY] `AnnotationRegistry.cs` — `ContributePatterns`
- [NEW] `DslTokenWriter.cs`
- [DELETE] `PolyDslTokenizer.cs`
- [MODIFY] `DslTokenReaderTests.cs` — no legacy
- [NEW] `DslTokenWriterTests.cs` / printer smoke
- [MODIFY] CORE, grammar-integration, task.md
