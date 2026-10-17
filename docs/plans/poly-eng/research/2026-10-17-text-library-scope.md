# What a text/string library must provide

**Date:** 2026-10-17
**Status:** proposal / research. Not on the board `CURRENT` — nothing here is admitted work.
**Evidence:** [`docs/probes/findings/grep-2026-10-17.md`](../../../probes/findings/grep-2026-10-17.md)
(a probe that modeled `grep` end to end). Findings cited below as G*, plus the probe's
`docs/probes/grep-2026-10-17/{grep.poly,LOG.md}`.

## 0. Why we now know the scope

The grep probe built the whole shell of a text tool in the domain language — records, lifecycle,
counts, per-file and whole-query results, an option surface, fan-out, subscriptions — and ran it on
the interpreter and through the emitter (`0 errors / 0 warnings`, host build `0/0`, 1029 emitted
lines). Every part of that model worked **except the match itself** (G1): the shipped model decides
`Content is needle`, whole-value equality, so `grep alpha` misses `alpha beta`.

So the library is small in surface and load-bearing in position: a text predicate over an existing
primitive. Nothing else in the probe needed it.

## 1. The seam (what "a library" is here)

`IDomainLibrary` (`Poly/DomainModeling/Libraries/*/`): `Id`, `PrimitiveSeeds` (catalog types), and
`Register(SessionBuilder)` which registers binary expression folds, print binders, meaning, and type
maps. `uses <id>` opts in; the session loads it. `temporal` is the worked example — it owns the
`Date`/`Time`/`DateTime`/`Duration` catalog types, the `Now`/`Today` clock nodes, the `12 Days`
postfix spelling, its print binders, and its lowering, while core keeps only the
`DomainExpression`/fold/binder seam.

Two facts decide this library's shape:

1. **`Text` is already a core primitive**, so unlike temporal this library adds **no types**. It
   adds *operations over an existing type*. There is no `TextTypeCatalog` to seed.
2. **A library can bend existing expression *rules*, but not add a production.** The registry a
   library registers into (`ExpressionFormRegistry`) offers `RegisterBinaryFold` (specialize the
   operands of `+`/`-`), `RegisterFold(pattern, fold)`, `RegisterIdent` ("claims an identifier
   spelling (e.g. `Now`)"), and `RegisterGrammarContributor` ("patterns on existing expression
   rules, e.g. `ident` / `number+ident`") — which is how temporal gets `Now`, `Today` and `12 Days`.
   Its own contract says: *"Not a place to add new language productions."* There is also no call
   syntax (probed: `Length(x)` → `Parse error: Expected RBrace, got '('`). So text needs one of:
   - **(A) core-shipped spelling** — new expression node(s) with fixed keywords, like `is`/`and`,
     available to every domain (no `uses` door); or
   - **(B) widen the contract** to admit a session-registered **binary form word**
     (`contains`/`startsWith`/`endsWith`). The plumbing (`ContributeGrammarPatterns`, folds, print
     mappings, meaning) already exists for *rule patterns*; what is missing is the contract, the
     tokenizer/parser treatment of the word, and the analysis type rule.

   The frozen-core rules ("shipped ⊆ complete generic Syntax tree", "new meaning: lower, analyze, or
   replace nodes") are satisfied by both; the difference is whether text is always-on language or an
   opt-in door. The one *known* second use is a regex/pattern library, which the same hook would
   serve, so (B) is not a framework for an imaginary second use.

## 2. Required surface (minimum, derived from grep)

| Operation | Why (grep) | Result | Notes |
|---|---|---|---|
| `contains` | the default: `grep alpha` matches `alpha beta` | Boolean | **the kernel**; without it the model is `-F -x` at best |
| `startsWith` / `endsWith` | common in real use | Boolean | cheap once the node exists |
| `length` | `-o`/window logic, validation | Number | decide chars vs UTF-8 bytes (D4) |
| case fold (`lower`/`upper`, or a `-i`-style ignore-case flag on the operators) | `-i` | Text / flag | without it `IgnoreCase` is a declared-but-unimplementable option (probe §6) |
| equality | already shipped (`is`, `is not`) | Boolean | keep as the reference semantics |
| `+` on two `Text` operands | text *construction* | Text | **already shipped and undocumented** (G10): `this.Haystack + this.Needle`. Document it or drop it; do not add a second concat |
| ordinal comparison (`<`, `<=`, `>`, `>=`) | ordering | Boolean | **broken today**: analysis accepts `Haystack > Needle`, the VM evaluates it, and the print emits `this.Haystack > this.Needle` → `CS0019` (G7). The library must own the decision (ordinal comparison, or reject the operands) |

Explicitly **out** of this library: read-time regex over stored text (`pattern(...)` is a *write-time
constraint*, G1); culture/collation-sensitive comparison (pin **ordinal**, culture-free — the repo
already pins invariant culture elsewhere); capture groups and match spans (that is `-o`, and it needs
a *result* shape, not just a predicate — see the `-o` row of the probe's feature table); Unicode
normalization.

## 3. What each layer must do (the exit criterion per operation)

Per AGENTS.md a construct is only "shipped" when it is **represented, executed, and printed**:

- **author** — the spelling parses in a policy body, an `if` condition, and an assign RHS (the three
  places the probe's text reads live), and it round-trips through `export_dsl`;
- **analyze** — operand types are both `Text`; the result type is checked; the *absence* of the
  operator is never misreported as a missing navigation property (G9's message today tells the
  author to "declare a navigation property" for `Haystack contains Needle`);
- **execute** — the interpreter runs it (`DomainEntityInstance` executes the lowered body);
- **print** — the emitted C# compiles (`String.Contains(...)` etc.), the way `This.Haystack + This.Needle`
  does and `Text > Text` does not;
- **guide** — `Poly.Mcp/Docs/poly-dsl-guide.md` lists the operator in the shipped surface and in the
  effect/expression tables, in the same change.

## 4. Decisions

- **D1** core-shipped spelling (A) vs session-registered named form (B). *(Recommend B: the regex
  library is the known second consumer, and an opt-in door matches "product doors are opt-in uses";
  A is smaller but makes text always-on.)*
- **D2** where the meaning lives: a node in core (`Poly/DomainModeling/Ontology/`) vs a node in the
  library folder routed through the `DomainExpression` seam (the `DateOperation` precedent).
- **D3** is matching-with-a-pattern (`matches`, regex) the same library or a separate one over a
  `Pattern` type? It is the second use that decides D1.
- **D4** `length` units (chars vs UTF-8 bytes) and whether comparison is ordinal or ordinal-ignore-case.

## 5. Slices

| # | Slice | Exit criterion |
|---|---|---|
| S1 | `contains` end to end (spelling + node + analysis + Lower + VM + print binder + guide + tests) | the grep probe's corpus goes from **1 hit of 3** to the real answer: `alpha` matches both `alpha` and `alpha beta`. Nothing else in the probe changes |
| S2 | `startsWith` / `endsWith` | same harness, two more rows |
| S3 | `length` + case fold, and `-i` becomes real in the probe (`IgnoreCase` stops being a dead property) | probe's `IgnoreCase=true` walk matches case-insensitively |
| S4 | ordinal comparison decision (G7): implement ordinal `<`/`>` or reject `Text` operands at analysis | `Haystack > Needle` either prints compilable C# or is rejected with a type error; no `CS0019` |

Each slice is independently shippable and needs the interpreter and the printer to agree on the same
lowered body (a divergence is a Lower bug, per the frozen core).

## 6. What this library does *not* fix

- **Input** — reading files/stdin/argv is the cli/bare-host library
  ([`2026-10-17-cli-host-library-scope.md`](2026-10-17-cli-host-library-scope.md)).
- **Exit status** — a tool still cannot return 0/1/2; that is the scalar-result gap (probe G4).
- **`-o` spans** — surfaced by this library's `contains` only if D3 adds a match *result*; today the
  probe stores the whole line in the hit record.
