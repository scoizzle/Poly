# Poly.Grammar

A pattern-table grammar engine. Grammars are defined as named collections of
token sequences (patterns). A linear-scan matcher finds the longest matching
pattern at each position in a token stream. A printer walks the same pattern
table to produce formatted output.

## Core concept

Most parsers are recursive descent — a tree of method calls where grammar rules
are implicit in the control flow. This module flips that: the **pattern table**
is the canonical grammar. The engine is a single loop that tries all patterns
at each position, picks the longest match, and advances.

Walking the same pattern table in the opposite direction produces formatted
output via a pluggable writer that owns all formatting policy.

```
Grammar (pattern table)
    │                       │
    ▼                       ▼
TokenReader      TokenWriter
    │                       │
    ▼                       ▼
Matcher ──► MatchResult    Printer ──► text
(parse)                    (print)
```

## Architecture

| Component | Responsibility |
|-----------|---------------|
| `Token<TKind>` | A single token with kind, text, and source position |
| `TokenReader<TKind>` | Abstract token source with lookahead buffering |
| `StringTokenReader<TKind>` | Base for string-backed scanners (char nav, line/col) |
| `Pattern<TKind>` | A named sequence of pattern elements |
| `IPatternElement<TKind>` | One slot in a pattern (token, value, predicate, many, etc.) |
| `Grammar<TKind>` | Table of patterns grouped into named rules; patterns are sorted by first-token kind then length |
| `Matcher<TKind>` | The scan loop — longest match at each position |
| `MatchResult<TKind>` | Pattern name + consumed tokens |
| `TokenWriter<TKind>` | Abstract output formatter — canonical text, indentation, spacing |
| `StringTokenWriter<TKind>` | Concrete `StringBuilder`-backed writer |
| `Printer<TKind>` | Walks pattern table to produce formatted output via writer |
| `PrintContext<TKind>` | Callback context for supplying runtime values at print time |

## Pattern elements

| Builder method | Element | Parse behavior | Print behavior |
|---|---|---|---|
| `Token(kind)` | `MatchToken<TKind>` | Match a specific token kind | Auto-emit canonical text |
| `Value(kind)` | `MatchValue<TKind>` | Match a specific token kind | Call content callback for runtime value |
| `Predicate(fn, label)` | `MatchPredicate<TKind>` | Match when predicate returns true | Call content callback |
| `Optional(kind)` | `Optional<MatchToken>` | Zero or one of a token kind | Call content callback (may emit nothing) |
| `Optional(element)` | `Optional<TKind>` | Zero or one of any element | Call content callback |
| `Many(ruleName)` | `ManyOf<TKind>` | Zero+ matches from a named rule | Call content callback |
| `Balanced(open, close)` | `Balanced<TKind>` | Brace-balanced block (depth-tracked) | Emit open, indent, content callback, dedent, emit close |
| `Any()` | `AnyToken<TKind>` | Wildcard — matches any single token | Call content callback |

**`Token` vs `Value`**: Both match the same token kind during parsing. The
distinction matters only for printing — `Token` elements are fixed syntax
(`:`, `{`, `true`) and can be auto-emitted. `Value` elements need runtime
content (String, Number, Identifier).

## Parsing usage

```csharp
// 1. Define a token kind enum
enum MyKind { Identifier, Number, LParen, RParen, Plus, Star }

// 2. Build the grammar
var g = new Grammar<MyKind>();
g.Define("expr")
    .Pattern("add").Token(MyKind.Number).Token(MyKind.Plus).Token(MyKind.Number).Commit()
    .Pattern("group").Token(MyKind.LParen).Many("expr").Token(MyKind.RParen).Commit();

// 3. Create a token reader (scanner) and matcher
var reader = new MyStringTokenReader("1 + 2");
var matcher = new Matcher<MyKind>(g, reader);

// 4. Scan
while (matcher.TryMatch("expr") is { } result) {
    Console.WriteLine($"Matched {result.PatternName}: {result.Consumed} tokens");
    matcher.Consume(result);
}
```

## Printing usage

```csharp
// Same grammar — Token elements auto-emit, Value elements need content
var writer = new MyTokenWriter();
var printer = new Printer<MyKind>(g, writer);

printer.Print("add", ctx => {
    ctx.Emit(MyKind.Number, "1");
    ctx.Emit(MyKind.Plus);
    ctx.Emit(MyKind.Number, "2");
});
var output = writer.GetOutput(); // "1 + 2"
```

## Safety guarantees

| Scenario | Behavior |
|----------|----------|
| Empty input | `TryMatch` returns `null` |
| Unclosed block (no matching close) | `Balanced` returns `null` via `IsEndOfFile` guard |
| `Any()` at end of input | Returns `false` — wildcard does not match `EndOfFile` |
| `ManyOf` infinite loop | Guarded by 10_000 iteration cap |
| Pattern sorting | Same first-token patterns sorted longest-first; `ManyOf` trusts first match |

## Token writer extensibility

Override these methods on `TokenWriter<TKind>` to control formatting:

| Method | Purpose |
|--------|---------|
| `CanonicalText(kind)` | Maps a token kind to its output string |
| `WriteValue(kind, value)` | Emits a value-bearing token (add quotes, escapes, etc.) |
| `Write(kind)` | Emits canonical text for a token |
| `WriteRaw(text)` | Emits raw text as-is |
| `Space()` / `Newline()` | Whitespace management |
| `PushIndent()` / `PopIndent()` | Indentation stack |

## Design principles

- **The pattern table is the grammar.** Adding, removing, or inspecting
  patterns is a table operation, not a code change.
- **Longest match disambiguates.** When two patterns share a prefix, the one
  that consumes more tokens wins — no manual lookahead.
- **Patterns are sorted.** Within each rule, patterns are ordered by first-token
  kind then element count descending. This makes `ManyOf` safe — the first match
  for a given first token is always the longest potential match.
- **Reader is abstract.** TokenReader<TKind> works over any source. Concrete
  scanners only implement `ScanNextToken()`.
- **Writer is abstract.** TokenWriter<TKind> owns all formatting policy. The
  printer never makes formatting decisions — it delegates canonical text,
  spacing, and indentation to the writer.
- **Zero external dependencies.** Uses only System.Collections.Generic and
  System.Runtime primitives.
- **Pack extensibility.** Packs register additional patterns into grammar
  rules. The engine doesn't change.
