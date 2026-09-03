# Fleet eval 2026-08-12 — agent findings (slice: Parser & grammar)

Slice: `Poly/DomainModeling/Parsing/` (PolyDslParser, DslGrammar, DslTokenReader,
DslTokenKind, DslExpressionParser, DomainDslPrinter, DslCursor).

Probes:
- `probes/fleet-eval/01-parser/booking.poly` — hotel booking, full surface (0 err / 1 warn)
- `probes/fleet-eval/01-parser/keyword-collisions.poly` — keyword collisions (0/0)
- `probes/fleet-eval/01-parser/malformed/*.poly` — 9 edge/reject probes
Round-trip verified with a throwaway harness (parse → `DomainDslPrinter` → re-parse)
built in the pre-approved temp dir against `Poly/Poly.csproj` (no repo edits).

## F1 — `not (comparison)` round-trips to unparseable text (printer drops parens)
- **Signal:** guide-drift / round-trip divergence
- **Severity:** 🔴
- **Slice:** Parser & grammar (DomainDslPrinter.PrintExpression / Not)
- **Repro:** `probes/fleet-eval/01-parser/malformed/not-compare.poly`
  (`NotSmall: policy { not (Total > 0) }`), harness: parse → print → re-parse.
  Same for `if (not (Total > 0))` in `effects-rt.poly` (action body).
- **Expected:** guide §13: a valid domain "round-trips through apply_dsl → export_dsl".
  `not (Total > 0)` parses fine (Not over Comparison via group).
- **Actual:** printer emits `not Total > 0` (parens dropped) → re-parse fails
  `Expected RBrace, got '>' (Gt) (line 5, col 33)`. The parser's `ParseNot`
  operand is `ParseAdd` (no comparison), so the printer's own output is rejected.
  Affects every print context: policy bodies, `if` conditions, assign RHS.
- **Proposed patch:** in `ExpressionPrinter.Not`, parenthesize the operand when it is
  not a Primary-shaped node (`not (…)`), or make `ParseNot` accept a comparison
  operand per the guide's precedence table (comparison binds tighter than `not`).

## F2 — mixed `require A` + `require not B` round-trips to unparseable text
- **Signal:** guide-drift / round-trip divergence
- **Severity:** 🔴
- **Slice:** Parser & grammar (DomainDslPrinter.PrintAction / PolyDslParser require list)
- **Repro:** `probes/fleet-eval/01-parser/malformed/mixed-require.poly` (also
  `probes/fleet-eval/01-parser/booking.poly` action `Confirm`):
  parse → print → re-parse fails.
- **Expected:** export_dsl output re-parses (guide §13).
- **Actual:** printer comma-joins positive and negated gates as
  `require Positive, not Blocked`. `ParseActionBody` reads a require list via
  `ParseIdentifierList`, which can't consume the `not` keyword mid-list
  (`Expected identifier, got 'not' (line 9, col 26)`). A freshly-sourced action
  with both `require X` and `require not Y` (valid authoring) dead-ends the
  apply→export→apply loop.
- **Proposed patch:** printer emits `require not Blocked` on its own require line
  (parser already supports repeated `require` lines), or `ParseIdentifierList`
  handles a `not` prefix per element.

## F3 — DSL identifiers that are C# keywords (and entity named `Create`) break the export
- **Signal:** compile-fail on valid surface (fails at the latest rung, not parse)
- **Severity:** 🔴
- **Slice:** Parser & grammar (identifier admission; first rung should fail closed)
- **Repro:** `probes/fleet-eval/01-parser/malformed/kw-export.poly`
  (`Create: entity { For: Text If: Text }`) → run-probe:
  CS1525 `Invalid expression term 'if'`, CS0542 `'Create': member names cannot be
  the same as their enclosing type`, 22 errors.
- **Expected:** the parser is the earliest rung; identifiers that cannot survive
  the C# export must be rejected at parse/analysis with a rename hint, or the
  exporter must sanitize (`@if`). The guide §3 has no identifier rule.
- **Actual:** `For:`/`If:` lex as Identifiers (case-sensitive keywords), parse,
  analyze, and print (`export_dsl` emits them), but the export emits `string For` /
  `public static DomainResult<Create> Create(…)` → uncompilable C#.
- **Proposed patch:** parse-time reserved-identifier check for the C# keyword set
  (fail closed with "rename" hint), or export-side escaping.

## F4 — wrong-typed binder path-prefix read in `assign` passes analysis → export CS0029
- **Signal:** compile-fail on accepted surface (analysis gap behind the parser's grammar)
- **Severity:** 🔴
- **Slice:** Parser & grammar (peer-binder path-prefix surface, DslExpressionParser)
- **Repro:** `probes/fleet-eval/01-parser/malformed/binder-wrongtype.poly`
  `when all bookings Completed as b { assign Status to b Total }` (Status: Text,
  b.Total: Number) → run-probe: `error CS0029: Cannot implicitly convert type 'long' to 'string'`.
- **Expected:** guide: "Expressions are type-checked at analysis" — a Text-target
  assign from a Number source must be rejected at analysis, not at the compiler.
- **Actual:** analysis accepts; the C# export emits `this.Status = b.Total;` and the
  Roslyn gate fails. Same class as round3 F1/round5 F7 — binder path-prefix reads
  are missing from the type-check.
- **Proposed patch:** type-check assign targets against binder path-prefix value
  types in the invariant/type analysis (the binder's entity type is known).

## F5 — guide §8 documents `invoke` quantifiers `any`/`all` + `where` that the parser rejects
- **Signal:** guide-drift (unparseable-but-documented surface)
- **Severity:** 🟠
- **Slice:** Parser & grammar (invoke grammar)
- **Repro:** `probes/fleet-eval/01-parser/malformed/invoke-any.poly`
  `invoke any items.Mark()` → `Parse error: Expected effect …, got 'items'` (`any`
  is parsed as the action name, the real action name becomes a stray token).
- **Expected:** `Poly.Mcp/Docs/poly-dsl-guide.md` line 730 ("Shipped in the current
  product surface") lists "Invoke effect … quantifiers `any`/`all`; filter `where`".
  Either parse it or remove the line.
- **Actual:** the surface was removed (coordinator: commit 004331da); line 730 was
  not updated. §6 line 461 correctly says "One fan-out mode, no any/all/each
  quantifier" — the guide contradicts itself and documents an unparseable form.
- **Proposed patch:** delete the `quantifiers any/all; filter where` fragment from
  line 730 (and any `where`-filter invoke mention) so the guide matches the parser.

## F6 — guide §11 documents inline `enum(v1, v2, …)` constraint that the parser rejects
- **Signal:** guide-drift (unparseable-but-documented surface)
- **Severity:** 🟠
- **Slice:** Parser & grammar (constraint grammar)
- **Repro:** `probes/fleet-eval/01-parser/malformed/inline-enum.poly`
  `Color: Text enum(Red, Green, Blue)` → `Parse error: Inline enum(...) constraints
  are no longer supported. Use a top-level enum type declaration…`.
- **Expected:** guide §11 line 892 documents `Enum | enum(v1, v2, ...) |
  Color: Text enum(Red, Green, Blue)` as a shipped constraint.
- **Actual:** the parser rejects it (top-level enum types only). §3's constraint
  table correctly omits it — §11's "Constraint Reference" table is stale.
- **Proposed patch:** remove the `enum(...)` row from the §11 table (or mark it
  removed, mirroring §3).

## F7 — unbounded expression/effect nesting → uncatchable StackOverflow kills the process
- **Signal:** security/robustness (hostile input)
- **Severity:** 🟠
- **Slice:** Parser & grammar (recursive descent, DslExpressionParser / ParseEffect)
- **Repro:** generated probes in temp: `deep-parens.poly` (≈2000 nested parens in a
  policy body, ~4 KB input) and `deep-if10k.poly` (10k nested `if`):
  `Stack overflow. at DslTokenReader.ScanNextToken() / … NodeId.NewId()` — the
  compiler process dies. Threshold measured between 1000 and 2000 parens.
- **Expected:** invoke depth is capped (max 16, guide §6); expression/effect nesting
  should fail loud with a depth diagnostic rather than terminating the process.
- **Actual:** `StackOverflowException` is uncatchable — an `apply_dsl` with such
  input would take down the shared MCP server (all agents' tools), not just the call.
- **Proposed patch:** depth-guard in `ParseExpression`/`ParseEffect` recursion
  (e.g. 256–512), mirroring the invoke cap; fail with a positioned parse error.

## F8 — `when <to-one-nav> <Stage>` export derefs the nav unguarded (CS8602 + runtime NRE)
- **Signal:** reliability (valid surface fails the 0/0 gate; runtime null-deref risk)
- **Severity:** 🟠
- **Slice:** Parser & grammar (subscription surface the parser accepts; export adjacent)
- **Repro:** `probes/fleet-eval/01-parser/booking.poly` `Confirmed: stage {
  when room Available { … } }` → run-probe prints `errors: 0, warnings: 1`;
  manual Roslyn: `warning CS8602: Dereference of a possibly null reference`
  on `this.Room.RegisterBookingAvailableSubscriber(this);`.
- **Expected:** the round gate requires 0 errors / 0 warnings; a to-one correlation
  edge may legitimately be unset, so the registration must null-guard (or skip).
- **Actual:** `InitializeSubscriptions` calls `this.Room.Register…` without a null
  check — CS8602 warning, and a NullReferenceException at construction whenever the
  nav is unset on a created instance.
- **Proposed patch:** exporter null-guards the subscription registration
  (`if (this.Room is not null)`), or analysis requires the nav on the create path.

## F9 — `delete` effect is dead grammar: internal "Unhandled effect pattern" error
- **Signal:** fail-loud-but-sharp (dead grammar / internal message leak)
- **Severity:** 🟡
- **Slice:** Parser & grammar (DslGrammar effect table / ParseEffect)
- **Repro:** `probes/fleet-eval/01-parser/malformed/delete-effect.poly`
  `delete Foo` in an action → `Parse error: Unhandled effect pattern 'delete'`.
- **Expected:** `delete` is neither documented (guide §9 effect table) nor handled;
  an author gets either a supported effect or a product-facing "not supported"
  message — not an internal pattern name.
- **Actual:** `DslGrammar` defines `.Pattern("delete")` (plus the `Delete` token and
  its canonical text) but `ParseEffect` has no `delete` case; the match reaches the
  `default:` throw. Dead grammar kept around for a removed/never-shipped effect.
- **Proposed patch:** remove the `delete` grammar pattern + `Delete` token, or wire
  it to a "not supported" FormatException matching the other unsupported keywords.

## F10 — unterminated string literals are silently scanned to EOF (misleading error)
- **Signal:** fail-loud-but-sharp (error-message quality)
- **Severity:** 🟡
- **Slice:** Parser & grammar (DslTokenReader.ScanString)
- **Repro:** `probes/fleet-eval/01-parser/malformed/unterminated-string.poly`
  `Note: Text default("oops` (no closing quote) →
  `Parse error: Expected RParen, got '' (EndOfFile) (line 5, col 1)`.
- **Expected:** the scanner should report "unterminated string literal" at the
  opening quote's position.
- **Actual:** `ScanString` returns a StringLiteral covering everything to EOF with
  no error; the failure surfaces later at a random expected-token site. A stray
  quote also silently swallows newlines (multi-line raw strings with no escape
  semantics), so malformed input's cause is invisible.
- **Proposed patch:** after the scan loop, if the closing quote was not found, throw
  `GrammarError` ("unterminated string literal", start line/col).

## F11 — relationships named `any`/`all`/`none`/`count` (any case) are unusable in expression reads
- **Signal:** fail-loud-but-sharp (reserved-word behavior beyond documentation)
- **Severity:** 🟡
- **Slice:** Parser & grammar (DslExpressionParser quantifier detection)
- **Repro:** `probes/fleet-eval/01-parser/malformed/nav-any.poly` (`Any: many Item`,
  policy `Any exists`) → `Parse error: Expected 'where' after 'Any exists', got '}'`.
- **Expected:** the guide documents the quantifier collision only for `when`
  subscriptions (§7 "Reserved keywords: any and all are parsed as quantifier
  keywords when they immediately follow when"). The guide's sibling rule would
  apply the same reservation to expression path-prefix reads.
- **Actual:** `IsQuantifierKeyword` is `OrdinalIgnoreCase` and fires in
  `ParsePrimary` before `ParseRelatedAccess`, so `Any exists`, `count Rel`, `all
  Rel where …` all fail (or silently reinterpret — `count exists` parses as
  `Count("exists")`). The failure message ("Expected 'where' after…") doesn't
  mention the reserved-word collision.
- **Proposed patch:** extend the guide's reserved-keyword note to expression reads
  (and the rename hint), and/or emit a collision-specific diagnostic.

## Verified-OK (not findings)
- booking.poly / keyword-collisions.poly parse, analyze, export; keyword-collisions
  passes 0/0; booking.poly fails only the 0-warning gate (F8).
- Round-trips verified clean: enum defaults + escaped strings + pattern + column/
  table annotations + open ranges (`range(0, )`, `range(-500, )`), `length(3, )`,
  else-if chains, entry/exit, `for … where policy|in stage`, self/cross/`for` invoke,
  `-> EntityType` create-in returns, `when any|all … as binder`, Q3′ quantifiers
  (`any/all/none/count`, bare and `where`-filtered), to-one path-prefix and multi-hop,
  `Rel exists` / `not Rel exists`, subject-first `where` reads.
- Fail-loud behaviors verified: `when` inside an action body (F7 message), empty
  input ("DSL text is empty."), `prev` stage keyword, unknown annotations, unknown
  enum members, unclosed braces, `not a > b` / `a > b > c` (rejected).
- `delete`/`for`/`when`/`in` etc. as lowercase identifiers are reserved tokens with
  positioned errors; capitalized keyword forms (`Create`, `Stage`, `In`) are valid
  identifiers (pinned by EnumKeywordCollisionTests).
