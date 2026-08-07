# Task checklist — grammar-integration (GI) execution

Source: `docs/plans/grammar-integration.md`  
**Note:** Preflight (C99 Matcher dual-run, §3.4 / GIP) was skipped by prior agent; product GI WIP continued. Preflight still owed as high-value engine stress — do not treat “engine proven” as complete until GIP green or written waiver.

## Pre-work

- [x] Silent trace: full API surface of `Poly.Grammar`, DSL pipeline, annotation system, JSON parser, test grammars
- [x] Repair pre-existing suite regressions (stage-action invoke resolution in `EffectAnalyzer` + test fixtures) — suite green before GI-1

## GI-preflight (C99) — still open

- [ ] GIP-0 inventory C99 subset surface
- [ ] GIP-1 `Grammar<C99TokenKind>` for supported subset
- [ ] GIP-2 Matcher handlers dual-run vs hand `C99Parser`
- [ ] GIP-3 gaps doc → feed expression strategy

## GI-1 — DslTokenReader (tokenizer port)

- [x] `CharSourceTokenReader` + `ICharSource` + `StringCharSource` extracted; `StringTokenReader` re-based; `Token.Payload` channel added
- [x] `DslTokenKind` enum (mirror of legacy `TokenKind`)
- [x] `DslTokenReader : StringTokenReader<DslTokenKind>` — token stream parity incl. `//` comments, two-char ops, string escapes, keyword map; `IsEndOfFile`; `GrammarException` errors
- [x] Parity tests: `DslTokenReaderTests`
- [x] Suite green

## GI-2 — DslGrammar table

- [x] `Grammar<DslTokenKind>` for Phase 1a structure (top enum/entity, entity-body, stage-body, annotation shapes)
- [x] Element-set gaps documented on `DslGrammar` (expr / effect bodies / action params stay RD handlers)
- [x] `DslGrammarTests` dispatch acceptance

## GI-3 — GrammarDslParser dispatch

- [x] Parser on `DslTokenReader` + matcher-driven dispatch (`top` / `entity-body` / `stage-body`)
- [x] Dual-cursor fix: `TokenReader.Unread` + `MatchRule` so Matcher sees head token held in `_current`
- [x] Concurrent-match fix: sort patterns on `AddPattern`, never mutate in `GetPatterns` (shared static table)
- [x] Regression: `GrammarDslParserDispatchTests`
- [x] Full suite green: **1881/1881**

## GI-4 — Pack annotation grammar

- [ ] Annotation rule + pack pattern registration API (extension point sketched in GI-2 tests)
- [ ] SQL pack (column/table) round-trips still on handler path today

## GI-5 — DslPrinter port

- [ ] DslTokenWriter + Printer skeleton
- [ ] Round-trip corpus

## GI-6 — JSON parser port (optional / pull)

- [ ] Product-side JsonKind port when pulled

## GI-7 — Cutover + docs

- [ ] Legacy tokenizer removed; CORE + guide if needed
- [ ] Dual path removed (today: single façade, matcher dispatch + RD handlers)

## GI-8 — Non-text streams (defer)

- [ ] Utf8CharSource etc. — foundation `ICharSource` already landed in GI-1

## Verify

- [x] Build + full suite green (after GI-3 dual-cursor + sort fixes)
- [ ] Pre-ship review gate before commit
