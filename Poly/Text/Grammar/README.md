# Poly.Text.Grammar

A pattern-table grammar engine. Grammars are defined as named collections of
token sequences (patterns). A linear-scan matcher finds the longest matching
pattern at each position in a token stream.

## Core concept

Most parsers are recursive descent — a tree of method calls where grammar rules
are implicit in the control flow. This module flips that: the **pattern table**
is the canonical grammar. The engine is a single loop that tries all patterns
at each position, picks the longest match, and advances.

```
Grammar (pattern table)
    │
    ▼
TokenReader ──► Matcher ──► MatchResult
(string,           (longest      (pattern name +
 UTF-8,            match         consumed tokens)
 stream)           loop)
```

## Architecture

| Component | Responsibility |
|-----------|---------------|
| `Token<TKind>` | A single token with kind, text, and source position |
| `TokenReader<TKind>` | Abstract token source with lookahead buffering |
| `StringTokenReader<TKind>` | Base for string-backed scanners (char nav, line/col) |
| `Pattern<TKind>` | A named sequence of pattern elements |
| `IPatternElement<TKind>` | One slot in a pattern (token, predicate, many, etc.) |
| `Grammar<TKind>` | Table of patterns grouped into named rules |
| `Matcher<TKind>` | The scan loop — longest match at each position |
| `MatchResult<TKind>` | Pattern name + consumed tokens |

## Built-in pattern elements

| Element | Description |
|---------|-------------|
| `Token(kind)` | Match a specific token kind |
| `Predicate(fn, label)` | Match a token whose kind satisfies a predicate |
| `Optional(kind)` | Optionally match a token kind |
| `Many(ruleName)` | Zero or more matches from a named rule |
| `Balanced(open, close)` | Brace-balanced block (tracks nesting) |
| `Any()` | Wildcard — matches any single token |

## Usage

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

## Design principles

- **The pattern table is the grammar.** Adding, removing, or inspecting
  patterns is a table operation, not a code change.
- **Longest match disambiguates.** When two patterns share a prefix, the one
  that consumes more tokens wins — no manual lookahead.
- **Reader is abstract.** TokenReader<TKind> works over any source. Concrete
  scanners only implement ScanNextToken().
- **Zero external dependencies.** Uses only System.Collections.Generic and
  System.Runtime primitives.
- **Pack extensibility.** Packs register additional patterns into grammar
  rules. The engine doesn't change.
