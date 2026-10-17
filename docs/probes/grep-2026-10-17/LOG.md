# grep domain probe — LOG

Master `b40e694b`. One tool, modeled honestly, through every shipped stage:
author → `apply_dsl` → `get_domain_analysis` → VM walk → `export_dsl` round-trip →
`export_dsl` re-apply → `scripts/run-probe.sh` (http path: export + host build).
Findings: [`../findings/grep-2026-10-17.md`](../findings/grep-2026-10-17.md).

| Time | Step | Reached | Findings | Next |
|------|------|---------|----------|------|
| 09:05 | expression surface, spelling by spelling | ~35 single-spelling probes on scratch sessions | G1 no text predicate (🔴); G9 an unknown infix word is reported as a missing navigation property (🟡); G10 `Text + Text` concatenates on both paths, undocumented (🟡); G4 `-> Boolean`/`-> Number` rejected (🟠) | model the domain |
| 09:35 | first full draft | parse reached `Source.Scan`, then `'l' is not a property, parameter, or enum member` | G6 a property named `Number` is unreadable; the error names the **binder**, not the property (🔴) | rename to `LineNo` |
| 10:10 | domain applies | 4 entities, 3 relationships, 0 errors / 0 warnings / 3 hints | G8 aggregates all-roots: a plain `many` collection declares no composition (🟠) | add `owned` |
| 10:40 | VM walk, exact match (draft 1: pattern denormalized onto `Line`, predicate-filtered `for`) | query `alpha` over `alpha` / `alpha beta` / `gamma` → 1 hit; `HitCount=1`; `TotalHits=1` | — (the substring line correctly did *not* match) | zero-match walk |
| 11:00 | VM walk, no match (draft 1) | `for lines as line where line Matches invoke line.Mark` over a source with no matching line → `for lines.Mark matched zero targets.` source stays `Unscanned`, query stays `Prepared` | G3's trap, as originally written (🔴) — later re-scoped, see 14:10 | inverted walk |
| 11:15 | VM walk, inverted (draft 1) | a second source (`gamma`, `delta`), `ScanInverted` → `HitCount=2` | ✅ `-v` works | aggregate control |
| 11:40 | aggregate controls | `Root { kids: many Kid }` → `roots=[Root, Kid]`; `kids: many owned Kid` → `roots=[Root]`, `Root:[Kid]`; roastery re-applied → `roots=[Bean, Order]`, `Bean:[Batch] Order:[Line]` | G8 confirmed with a two-line control; `owned` is a prefix (`many Kid owned` is a parse error) | fan-out predicate |
| 12:00 | fan-out predicate with a path-prefix hop (draft 1) | `for lines as line where line Matches` with `Matches: policy { query PatternText is Content }` → rejected | G2, as originally written (🔴) — later re-scoped, see 14:10 | contract escape hatch |
| 12:20 | contract + bind | emitted `RegexAdapters` is static, private-ctor, no registration seam; the bound body sees only `IsSuccess` | G11 the escape hatch cannot host the matcher (🟡) | text comparison |
| 12:35 | `Haystack > Needle` | analysis accepts, VM evaluates, printed C# is `this.Haystack > this.Needle` → `CS0019` | G7 type-check gap + VM/print divergence (🔴) | gates |
| 12:50 | `scripts/run-probe.sh` (draft 1) | 8 files, 955 lines, host build 0/0; 15 route templates | — | canaries |
| 13:05 | test contracts | probe has VM Errors (7, all G13 `DomainResult.Success` arity) → row added to `VmErrorSamples`; not a `DomainErrorSamples` row; printed C# compiles | G13 (note) | suite |
| 13:20 | full suite (draft 1) | `Passed! 3944 total, 0 failed` | — | review |
| 14:10 | **shape review: the parameterized alternative** | draft 1 (denormalized pattern + predicate-filtered `for`) vs `for sources as src invoke src.Scan(needle: PatternText)` → `for lines as line invoke line.Check(needle: needle)` → `Line.Check: if (Content is needle) { transition to Marked }` | the alternative applies clean and **removes G2 and the no-match trap as language gaps**: no `PatternText` on `Line`, and a no-match source with a line **succeeds**. Both re-scoped | rewrite the probe |
| 14:40 | probe rebuilt on the working shape | `Line.Check` / `Line.Skew` guarded by `if (Content is … needle)`; 0 errors / 0 warnings / 2 hints; `roots=[Query]`, `Query:[Hit,Line,Source]` | — | re-walk |
| 14:55 | VM walk, all queries (rebuilt) | exact match → 1 hit, `HitCount=1`, `TotalHits=1`, `ScannedSources=1`; no match (1 line) → `Scan` and `Report` succeed, `TotalHits=0`; inverted → `HitCount=2` | ✅ no-match is an ordinary result in this shape | gates |
| 15:25 | canary + full suite (rebuilt) | probe VM Errors (7) / 11 types; suite `Passed! 3944 total, 0 failed` | — | second review |
| 16:05 | **shape review: empty sets and the dead flag** | (a) a source with **0 lines** → `for lines.Check matched zero targets.`; a query with **0 sources** → `for sources.Scan matched zero targets.` (b) `Invert` was declared and unread | (a) G3 keeps a real gap: the zero-target check is unconditional, so an **empty** fan-out fails (🟠) even though a non-empty no-match set does not. (b) `Invert` is honourable in-model — `Query.Scan` now branches on `Invert is true` and dispatches to `Source.ScanInverted` (verified: `Invert=true` over `gamma`/`alpha` → `HitCount=1`) | final gates |
| 16:30 | `scripts/run-probe.sh` (final) | 8 files, **1029 lines**, host build **0/0**; **16 route templates** | — | report |
| 17:40 | **follow-ups (Scot)**: empty-set `for` fix, scalar-return diagnosis, library specs | `for` over zero records is a no-op (`fix/for-empty-set`, `a965d287`); a `-> Number` domain prints compiling C# once the analyzer's entity-only check is relaxed, but the interpreter projects the result as a failure (G4); specs written for the text and cli/host libraries | G3 **fixed**; G4 diagnosed; new defect: a failure return in a `when` body (both `for` **and** plain `invoke`) emits into a `void` handler → CS0127 (`master`, pre-existing) | [follow-up notes](../../plans/poly-eng/research/2026-10-17-text-library-scope.md) |

## The VM walk (the exact call sequence, as shipped)

```
query 1  create_instance(Query, {"PatternText":"alpha"})                     → Prepared
         invoke_action(query, AddSource, {"path":"a.txt"})                   → Source (Unscanned)
         invoke_action(source, LoadLine, {"number":1,"content":"alpha"})     → Line (Unmarked)
         invoke_action(source, LoadLine, {"number":2,"content":"alpha beta"})
         invoke_action(source, LoadLine, {"number":3,"content":"gamma"})
         invoke_action(query, Scan)                                          → query stays Prepared
         invoke_action(query, Report)                                        → Reported
         get_instance(query)  → TotalHits=1, ScannedSources=1
         get_instance(source) → HitCount=1
         list_instances(Hit)  → 1          # LineNumber=1, Content="alpha"

query 2  one line "nothing"                        → Scan ok, Scanned; Report ok; TotalHits=0
query 3  {"PatternText":"alpha","Invert":true}, lines "gamma","alpha"
                                                   → Scan dispatches to ScanInverted; HitCount=1
query 4  source "empty.txt" with no lines          → Scan FAILS: for lines.Check matched zero targets.
query 5  no sources at all                         → Scan FAILS: for sources.Scan matched zero targets.
```

`alpha beta` is what real `grep alpha` matches and this model does not: the only predicate the
language can express over a line is whole-value equality. That is the whole of the distance from
`-F -x` to grep.

## Shape notes (what the drafts taught)

- **Draft 1** put `PatternText: Text required` on `Line` and filtered the fan-out with
  `where line Matches`. Both are forced *by that design*: a `for` predicate must be a local policy
  over the record's own properties, so the pattern has to be copied onto every line. It compiles,
  runs, and counts correctly — it is just a join the analyser has to guess at, and it makes a
  no-match run a hard failure.
- **Final shape** passes the pattern as an action parameter and moves the decision into the callee
  (`if (Content is needle)`). Nothing is denormalized, the fan-out targets every line, and a
  no-match (non-empty) run completes. An **empty** set still fails — that is the language's
  zero-target check, not the shape.
- **Options:** `Invert` is honoured (`Query.Scan` branches on it); `IgnoreCase`, `MaxCount` and
  `MatchMode` are declared and read by nothing, because no operator in the language can honour them
  (G1, G5).
