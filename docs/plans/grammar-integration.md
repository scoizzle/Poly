# Grammar Framework Integration — Poly.Text.Grammar → DSL Pipeline

**Date:** 2026-07-26  
**Status:** Draft — core prototype proven, integration pending  
**Prototype:** [`Poly/Text/Grammar/`](../../Poly/Text/Grammar/) — 13 files, 54 tests, 0 warnings  
**Related:** [`docs/plans/domainmodeling-decomposition-proposal.md`](../domainmodeling-decomposition-proposal.md), [`docs/CORE.md`](../../docs/CORE.md), `Poly/DomainModeling/Parsing/`, `Poly/DomainModeling/IAnnotationSyntax.cs`

---

## 1. Why now

The current DSL pipeline (`PolyDslTokenizer` + `PolyDslParser` + `DomainDslPrinter`) is ~2,360 lines of hand-rolled recursive descent — closed, non-extensible, and a barrier to pack-contributed syntax. Every new pack keyword requires editing `PolyDslParser.ParseAnnotation()`.

The `Poly.Text.Grammar` prototype proves the alternative: a pattern-table engine where adding syntax is a registration operation, not a code change. 54 tests across three grammars (mini-DSL, JSON parse, JSON print) confirm the core works — longest-match, nesting, optionality, repetition, wildcards, EOF guards, and round-trip print→parse.

This plan closes the gap between "pattern-table engine works for toy grammars" and "the DSL pipeline is driven by it."

### Named consumers

| Consumer | Why they need it |
|----------|------------------|
| **SQL pack** | `column("NAME","TYPE")` annotation parse currently wired into `PolyDslParser.ParseAnnotation()` — cannot add new syntax without editing core code |
| **Future packs** | Same barrier — every pack keyword is a core edit today |
| **DSL authoring agents** | Grammar-driven `ExpectedTokens()` enables better error recovery and completion suggestions |
| **MCP `apply_dsl`** | Eventually: grammar-validated batch DSL with position-aware errors instead of runtime failures |

---

## 2. Current state inventory

### Already built (prototype — `Poly/Text/Grammar/`)

| Component | Status | Tests |
|-----------|--------|-------|
| `Token<TKind>` | Done | – |
| `TokenReader<TKind>` (abstract + lookahead buffer) | Done | – |
| `StringTokenReader<TKind>` (char nav, line/col) | Done | – |
| `IsEndOfFile(TKind)` guard | Done | tested (Balanced + AnyToken) |
| `Pattern<TKind>` + `IPatternElement<TKind>` | Done | – |
| `Grammar<TKind>` + `RuleBuilder`/`PatternBuilder` | Done | – |
| Pattern sorting (first-token kind, length desc) | Done | tested |
| `Matcher<TKind>` — longest-match scan loop | Done | 30+ parse tests |
| `MatchResult<TKind>` | Done | – |
| `GrammarException` (line/col) | Done | – |
| `TokenWriter<TKind>` (abstract formatter) | Done | – |
| `StringTokenWriter<TKind>` (StringBuilder) | Done | – |
| `Printer<TKind>` + `PrintContext<TKind>` | Done | 12 print tests, round-trip |
| `MatchValue<TKind>` element | Done | tested (parse + print) |

### Integration surface to replace

| Current file | Lines | Interface | Target |
|-------------|-------|-----------|--------|
| `PolyDslTokenizer.cs` | ~280 | `Next()`, `Peek()`, `Peek(int)` | Replace with `StringTokenReader<DslKind>` subclass |
| `PolyDslParser.cs` | ~1,380 | `Parse()` → `List<DomainChange>` | Replace with `Matcher<DslKind>` + handler dispatch |
| `DomainDslPrinter.cs` | ~700 | `Print(Domain)` → `string` | Replace with `Printer<DslKind>` + content callback |
| `IAnnotationSyntax.cs` | ~20 | `Keyword`, `TryPrint(Facet, out string)` | Port to grammar registration (new element type or predicate) |
| `AnnotationRegistry.cs` | ~50 | `Register(IAnnotationSyntax)`, `CanAccept(string)` | Port to grammar-level keyword registration |
| `DomainAuthoringContext.cs` | ~45 | Annotations, TypeMaps, Passes, StorageConventions | Add grammar extension point |
| `DomainExpressionJsonParser.cs` | ~200 | `ParseJson(string)` → `DomainExpression` | Phase 2 — port to `Grammar<JsonKind>` (separate track) |

## 2.1 Grammar Generalization Commitments

This section owns token-model evolution decisions that were previously mixed into
domain-analyzer planning.

Decision:

1. Non-text token payloads (including binary encodings): committed capability.
- Text-first remains the initial delivery shape for the current DSL pipeline.
- Binary/non-text streams are a planned follow-on capability through the same
    grammar seams (`TokenReader`, `Matcher`, `Printer`), not a second parser
    subsystem.

2. Non-enum token identifiers: supported as an exception path.
- Keep enum-first ergonomics for the primary DSL authoring path.
- Maintain an incremental path for other token identifier forms when a consumer
    justifies it.

3. Facets/attributes-first policy for packs: default.
- Most pack scenarios should extend via facets/attributes plus custom nodes and
    analysis passes.
- Token identity/payload generalization is for cases where facets cannot express
    transport or encoding constraints.

Sequencing:

1. Keep DomainAuthoringContext removal and one-analyzer unification as the active critical path.
2. Deliver dedicated grammar slices for non-text streams:
     - token payload channel (typed/non-text value path)
     - stream-backed reader path (non-string input)
     - printer/writer compatibility contracts for non-text outputs
3. Validate with one concrete pack/transport consumer before broad API expansion.

Non-goals for this sequence:

- No alternate analyzer system definition.
- No pack-specific parser forks.
- No broad token API expansion without a first concrete consumer.

---

## 3. Tasks

Slice IDs: **GI-N** (Grammar Integration — numbered).

### GI-1: Migrate tokenizer — `DslTokenReader`

Replace `PolyDslTokenizer` with a `StringTokenReader<DslTokenKind>` subclass.

| Current | New |
|---------|-----|
| `sealed class PolyDslTokenizer` | `sealed class DslTokenReader : StringTokenReader<DslTokenKind>` |
| `Token Next()` | `protected override Token<DslTokenKind> ScanNextToken()` |
| `Token Peek()` / `Peek(int)` | Inherited from `TokenReader<DslTokenKind>` |
| `enum TokenKind` (55 values) | `enum DslTokenKind` (same values, rename) |
| `readonly record struct Token` | Replaced by `Token<DslTokenKind>` |
| `Position` / `Line` / `Col` field management | Inherited from `StringTokenReader` |

**Acceptance:**
- `DslTokenReader` produces identical token stream for 10 representative `.poly` inputs
- `IsEndOfFile(DslTokenKind)` returns true for `EndOfFile` kind
- All existing `PolyDslTokenizer` callers ported to new type

**Risk:** Low — straightforward port. Tokenizer is pure scan logic with no parser coupling.

---

### GI-2: Define DSL grammar table

Build the `Grammar<DslTokenKind>` that describes all Phase 1a constructs.

**Scope:** Cover the complete Phase 1a grammar (domain header, entity, property, stage, action, policy, constraints, navigation properties, annotations).

**Registration pattern:**
```csharp
var dsl = new Grammar<DslTokenKind>();
dsl.Define("entity-body")
    .Pattern("property").Value(DslTokenKind.Identifier).Token(DslTokenKind.Colon)
                        .Predicate(IsPrimitiveType, "type")
                        .Many("constraints").Commit()
    .Pattern("stage")   .Token(DslTokenKind.Stage).Value(DslTokenKind.Identifier)
                        .Balanced(DslTokenKind.LBrace, DslTokenKind.RBrace).Commit()
    .Pattern("action")  .Token(DslTokenKind.Action)
                        .Value(DslTokenKind.Identifier)
                        .Optional(DslTokenKind.LParen).Many("action-params")
                        .Optional(DslTokenKind.RParen).Commit()
    // ... etc
```

**Acceptance:**
- All Phase 1a constructs have at least one pattern
- Grammar compiles with zero warnings
- `ExpectedTokens()` returns sensible first-token sets for each rule

**Risk:** Medium — some constructs may not map cleanly to the pattern element set (e.g., `when StageName { enter on entry ActionName }` subscription blocks). May require extending the element set.

---

### GI-3: Port parser — handler dispatch

Replace `PolyDslParser.Parse()` with a matcher-driven loop that maps each matched pattern to a `DomainChange` producer.

**Architecture:**
```
DslTokenReader ──► Matcher<DslTokenKind> ──► handler map
                     (grammar + input)         (pattern name → Func<MatchResult, List<DomainChange>>)
                                                    │
                                                    ▼
                                              List<DomainChange> (same as today)
```

**Handler registration:**
```csharp
var handlers = new Dictionary<string, Func<MatchResult<DslTokenKind>, List<DomainChange>>>
{
    ["property"] = result => [new AddPropertyToEntityChange(...)],
    ["stage"]    = result => [new AddStageChange(...)],
    // ...
};
```

**Acceptance:**
- 31 existing DSL round-trip tests pass with new parser
- Same `DomainChange` records produced as today
- Position-aware errors (`GrammarException`) surface correct line/col

**Risk:** Medium-high — the current parser is 1,380 lines with intertwined expression parsing, annotation dispatch, and constraint handling. The expression sub-grammar (comparisons, string literals, numbers for `range`/`length`/`pattern`) may need its own grammar + nested matcher or fallback to recursive descent.

---

### GI-4: Pack annotation grammar — replace `IAnnotationSyntax`

Current: `PolyDslParser.ParseAnnotation()` checks `AnnotationRegistry.CanAccept(keyword)`, then calls `IAnnotationSyntax.TryPrint()` for printing.

**Target:** Packs register patterns into the DSL grammar directly. A new element type or registration API lets packs declare keyword+argument shapes. The main DSL grammar includes a catch-all or predicate for pack keywords.

**Design sketch:**
```csharp
// Pack registers (no core edit)
var packGrammar = new Grammar<DslTokenKind>();
packGrammar.Define("annotations")
    .Pattern("column").Token(DslTokenKind.Column)
                      .Token(DslTokenKind.LParen)
                      .Value(DslTokenKind.String).Optional(DslTokenKind.Comma)
                      .Optional(DslTokenKind.Identifier)
                      .Token(DslTokenKind.RParen).Commit();

// Core grammar includes a hook for pack patterns
dsl.Define("entity-body")
    .Pattern("annotation").Many("pack-annotations").Commit();
```

**Acceptance:**
- SQL pack `column("NAME")` round-trips through grammar parser + printer
- No edits to core parser code
- Existing `SqlAnnotationSyntax` either ports to grammar or continues as printer-only fallback

**Risk:** Medium — the annotation model is simple (keyword + positional string args), so the mapping should be direct. The printer side needs equivalent `PrintContext` support.

---

### GI-5: Port printer — `DslPrinter`

Replace `DomainDslPrinter.Print(Domain)` with `Printer<DslTokenKind>` + content callbacks that walk the `Domain` object model.

**Acceptance:**
- All 31 existing round-trip tests pass (print → tokenize → parse → compare)
- Output byte-for-byte identical for 10 representative domains
- Annotation facets print via same grammar-driven path

**Risk:** Medium — the printer is 700 lines with domain→structure logic interleaved with formatting. Porting requires separating "what to print" (domain traversal) from "how to format" (TokenWriter).

---

### GI-6: Migrate `DomainExpressionJsonParser`

Port the JSON expression parser from hand-written recursive descent to `Grammar<JsonKind>` (reuses `JsonTokenReader` from existing tests).

**Acceptance:**
- All expression parsing tests pass through grammar path
- No behavioral change in `DomainExpressionJsonParser.ParseJson()`

**Risk:** Low — the grammar already exists (proven in tests). The existing parser is only ~200 lines of straightforward comparison/composite/literal dispatch.

---

### GI-7: Deprecation cleanup

| Action | Files |
|--------|-------|
| Delete or `[Obsolete]` `PolyDslTokenizer` | `Poly/DomainModeling/Parsing/PolyDslTokenizer.cs` |
| Delete or `[Obsolete]` `PolyDslParser` | `Poly/DomainModeling/Parsing/PolyDslParser.cs` |
| Delete or `[Obsolete]` `DomainDslPrinter` | `Poly/DomainModeling/Parsing/DomainDslPrinter.cs` |
| Remove or redirect `IAnnotationSyntax.TryParse` | `Poly/DomainModeling/IAnnotationSyntax.cs` |
| Update `CORE.md` + `AGENTS.md` | Grammar placement rules, module map |
| Update `poly-dsl-guide.md` if syntax changed | `Poly.Mcp/Docs/poly-dsl-guide.md` |

**Acceptance:** No stale files. CORE.md references grammar engine. AGENTS.md has placement rules for new files.

---

### GI-8: Non-text token streams (binary-capable path)

Add a grammar-level non-text stream capability without replacing the existing
text DSL path.

Scope:
- Add token payload abstraction (typed value channel in addition to display text).
- Add stream-backed reader path for non-string inputs (binary-friendly).
- Add printer/writer compatibility contracts for non-text outputs.
- Keep current text DSL behavior unchanged.

Acceptance:
- Existing DSL round-trip suite remains green without behavior drift.
- One binary/non-text consumer demonstrates parse + match + print path.
- Errors remain position-aware or equivalently diagnosable for non-text streams.

Risk:
- Medium. Requires API shape care to avoid destabilizing text-first ergonomics.

---

## 4. Agent pick

```text
CURRENT: GI-1 (tokenizer migration)
THEN:    GI-2 (grammar definition)
THEN:    GI-3 (parser dispatch)
THEN:    GI-4 (pack annotations)
THEN:    GI-5 (printer port)
THEN:    GI-6 (expression JSON port)
THEN:    GI-7 (deprecation + docs)
THEN:    GI-8 (non-text token streams)
DEFER:   Expression sub-grammar improvements, ExpectedTokens completeness
```

---

## 5. Rules

- **One task per turn.** Complete GI-1 fully before starting GI-2.
- **Existing tests must pass.** All 31 round-trip tests are the regression bar.
- **No API breaks in `DomainChange`** — the grammar produces the same change list types.
- **Document gaps.** If a construct doesn't map to the element set, flag it in the task before implementing.
- **Keep old parser until GI-7.** GI-1 through GI-4 build alongside the old parser for comparison testing.
