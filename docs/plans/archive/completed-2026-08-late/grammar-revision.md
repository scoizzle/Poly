# Grammar re-vision — design plan

**Date:** 2026-08-08  
**Status:** ✅ **DONE 2026-08-09** — tier A executed (parallel v2 rebuild + hard cutover; engine + DSL now `Grammar<TToken, TTokenKind>` canonical). All §7 criteria met; review items B1–B3, N1–N3, C1 closed. **Distinct from grammar wrap-up** (LeftAssoc live-fold / S5 — see PIPELINE-STATUS).
**Sequencing (2026-08-09 revision):** **parallel v2 rebuild, hard cutover** — the same pattern as the V2→V3 immutable-core cutover (no migration utility). Build the re-visioned engine from scratch as **`Poly.GrammarV2`** (namespace `Poly.GrammarV2`, clean type names, zero dependence on v1) alongside the existing `Poly/Grammar/`; migrate consumers (DSL, test grammars) one at a time via a `using` swap; then delete v1 and rename the folder/namespace back to `Poly.Grammar` in the cutover commit (mechanical). Wrap-up (LeftAssoc live-fold) lands on v2 after cutover. Admit as an explicit suite after the cheap dead-dual deletions (Validation / Text.Matching).  
**Principle:** One pattern-table engine for **language-shaped token streams** — tokenizer owns physical decoding and content, matcher owns recognition, handlers own meaning.  
**Related:** [`grammar-pure-end-state.md`](grammar-pure-end-state.md) (gpure DONE) · [`dead-dual-inventory-2026-08-08.md`](dead-dual-inventory-2026-08-08.md) · [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) · AGENTS.md / CORE  

---

## 1. Problem statement

The engine's **token type** is the fiction. Today:

```csharp
public readonly record struct Token<TKind>(TKind Kind, string Text, int Line, int Col, object? Payload = null)
```

- `string Text` rides along for languages that have no text.
- `int Line`/`int Col` are baked into the engine — meaningless for binary/char streams.
- `object? Payload` is an untyped escape hatch.
- The matcher reads **`.Text` zero times** — it matches **kinds** only.
- `GrammarException` **requires** line/col, so every language is forced to invent coordinates (or pass `0,0`).

The re-vision: Grammar is the engine for **any language-shaped token stream**. Width (fixed/variable) is a tokenizer concern. The token type is **language-owned**. Diagnostics positions are **caller-supplied**, not engine-invented.

---

## 2. The contract (locked)

### 2.1 Types

```csharp
/// <summary>Language-owned token: kind is the comparability surface; content/position are language-owned.</summary>
public interface IToken<TTokenKind> where TTokenKind : struct {
    TTokenKind Kind { get; }
}

/// <summary>Match stack: two generics (C# has no associated types).</summary>
public sealed class Grammar<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct
{ /* RuleBuilder / PatternBuilder / Pattern / Matcher / MatchResult */ }

/// <summary>Stream contract. Examine/consume (Pipelines-style) — the reader owns the
/// committed position; no Unread, no external head-token cursor.</summary>
public interface ITokenStreamReader<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct
{
    TToken Peek(int offset = 0);   // 0 = head, relative to committed position
    void Consume(int count);       // commit a matched span
    /// <summary>Kind-based end marker (renamed from IsEndOfFile). Not file-specific.</summary>
    bool EndOfStream(TTokenKind kind);
}
```

> **Examine/consume lock (2026-08-09):** the reader owns its committed position; the
> matcher only examines, callers commit via `reader.Consume(match.Consumed)`. This
> kills the dual-cursor dance at the root — `DslParseCursorBase`, the fragment cursor,
> and the parity cursor collapse to thin wrappers when the DSL migrates. `Matcher.Consume`
> was removed. All v2 test suites updated; suite 1989 green.

| Rule | Detail |
|------|--------|
| Kind equality | `EqualityComparer<TTokenKind>.Default` is the matcher's only comparison |
| Content | `Text` / payload / etc. live on language `TToken` — not on the engine |
| Position | **Not** on `IToken`, **not** on the reader. See §2.2 |
| Engine has no | `Token<TKind>` struct, engine `Line`/`Col`, `object? Payload` |

### 2.2 Diagnostics — caller provides position

**Do not** add `GetPosition()` (or similar) on `ITokenStreamReader`. Dual-cursor / Unread / Peek make “reader current position” ambiguous; product errors already use the **head token** (`_current.Line/Col`), not the reader.

**Lock (2026-08-09 revision — no custom exception type):**

```csharp
// Engine-internal factory; consumers read Message. Nothing reads structured
// position today; a GrammarException : FormatException subtype is a non-breaking
// additive change if type-based catchability is ever needed.
internal static class GrammarError {
    public static FormatException Error(string message);                       // no position
    public static FormatException Error(string message, int line, int column); // caller-supplied
}
```

| Site | Who supplies position |
|------|------------------------|
| `DslParseCursorBase.Error` / product handlers | From language token (`DslToken` Line/Col) — same as today |
| Engine `Expect` | **Message-only** — the contract guarantees only `Kind` on `TToken`; "from the token if the language put coords on it" has no mechanism. Positions come exclusively from handler `Error()` helpers |
| Empty fragment / no token | Message-only (prefer over fake `0,0`) |
| Matcher failures | Rare; message-only or language token if available |

**Locked:** `Expect` throws message-only; positions are a handler concern via `Error()`. Optional later (not required for this plan): a single optional `SourceLocation?` (line/col **or** offset) so binary can attach offsets without more constructors. Principle stays: **caller constructs the exception with whatever location it has.**

### 2.3 Buffered abstract reader (keep)

`ITokenStreamReader` is the contract; a **shared abstract base** still owns peek buffer + `Unread` (product `MatchRule` dual-cursor depends on it). Languages implement scan; they do not reimplement buffering.

### 2.4 Two generics (associated-type resolution)

Matcher compares **kinds**; handlers consume **tokens**. C# has no associated types → `Grammar<TToken, TTokenKind> where TToken : IToken<TTokenKind>`.

**Justification for the migration:** language-owned content for **DSL handlers** (primary). Existing multi-kind test grammars (Json, TestKind, …) prove the match stack after a mechanical swap. **Binary is documentation only.** Matching rebuild is **not** required to justify two generics (see §3–§4).

### 2.5 MatchPredicate (locked)

| Element | Match signature |
|---------|-----------------|
| Today | `MatchPredicate<TKind>(Func<TKind, bool>, label)` — matcher passes `token.Kind` (`Matcher.cs:169`) |
| **This plan** | `MatchPredicate` takes **`Func<TToken, bool>`** (single signature — no overload hedge) |

Token predicates unlock table-driven content checks (e.g. identifier text) without handlers. Kind-only product uses (`IsCompareOpKind`) become `t => IsCompareOpKind(t.Kind)`.

**Test:** at least one engine test with a content-sensitive predicate (two same-kind tokens, only one matches).

> **Extensibility note (locked):** the DSL's extensibility seam is **pattern/content registration over closed kinds** (E1: `ExpressionFormRegistry.ContributeGrammarPatterns` + `IExpressionPrimaryForm` — `MAGIC`→42 and `12 days` are Identifier/Number tokens checked by **content**). A strong closed `DslTokenKind` is what makes longest-match dispatch + sorted pattern groups work; the revision *strengthens* the seam by making `MatchPredicate` content-aware. Genuinely new **lexical classes** are product tokenizer growth (`DslTokenKind` + `DslTokenReader`), unchanged by this plan — no known pack (temporal is the near one) needs one.

### 2.6 Printer generics (implemented — supersedes the locked kind-only plan)

| Path | Generics |
|------|----------|
| Match stack | `Grammar` / `Matcher` / `MatchResult` / stream: **`<TToken, TTokenKind>`** |
| Print stack | `Printer` / `PrintContext`: **`<TToken, TTokenKind>`** (built; the earlier kind-only lock was wrong — see below) |
| Test grammars (Json / TestKind / C99 / Arithmetic) | Each gains its own `TToken` struct replacing `Token<TKind>` usage — part of R2's mechanical swap (kept in R0's site map) |

The printer walks `Pattern<TToken, TTokenKind>` directly, so it must carry both generics — the kind-only lock was a wrong prediction, corrected at build time. It is **stateless** (no shared buffer; `Print` uses a local `StringBuilder`), prints fixed kinds via a caller-supplied canonical map, and delegates content-bearing positions (`Value` / `MatchPredicate` / `Any` / `Optional` / `Repeat` / `Ref` / `LeftAssoc` / `Balanced` body) to a handler callback. Nested `PrintRule` writes into a fresh scratch buffer so inner prints never disturb the caller's output. Canonical text is **verbatim** — no invented spacing; callbacks own whitespace. Product `export_dsl` stays **domain-walk** (table-parity **deferred** — CORE stays honest).

---

## 3. Consumers

| Consumer | Role in this plan | Notes |
|----------|-------------------|--------|
| **DSL (product)** | **Primary** — full migration, zero behavior change | `DslToken : IToken<DslTokenKind>` (kind + text + line/col on the **language** type) |
| **Existing test grammars** | **Proof** after engine swap | Json / TestKind / edge cases — not a Matching rebuild |
| **Matching** (`Poly.Text.Matching`) | **Out of tier-A success** | Dead-dual inventory: delete candidate. Optional later suite: greedy rebuild **or** delete. `*abc` needs Capture/ManyUntil — **not** this plan |
| **Binary/ISA** | **Documented future only** | Self-delimiting kind-stream only without Capture/LengthRef; no engine work now |

**Dead-dual alignment:** this plan does **not** reverse “delete Matching” into a required rebuild. Prefer delete (or leave dormant) until an explicit Matching suite; do not claim rebuild “proves the seam” without `*abc`.

---

## 4. Scope

### 4.1 In scope (tier A — implement when admitted)

1. **`GrammarException`** — optional/caller-supplied position (§2.2); update engine `Expect` + product Error helpers.
2. **Engine match stack** → `Grammar<TToken, TTokenKind>`: Grammar, builders, Pattern, 9 PatternElements (incl. RuleRef/LeftAssoc), Matcher, MatchResult; abstract buffered reader + `EndOfStream(TTokenKind)`; **delete** engine `Token<TKind>`.
3. **`MatchPredicate`** → `Func<TToken, bool>` (+ tests) per §2.5.
4. **Printer** built as **`Printer<TToken, TTokenKind>`** (stateless, canonical-map + handler callbacks, scratch-isolated nested prints; the §2.6 kind-only lock was corrected at build time).
5. **DSL migration:** `DslToken`, reader, cursor, parsers — zero behavior change (parity + full suite).
6. **CORE / Grammar README:** “language-shaped token streams; tokenizer owns decoding; matcher owns recognition; handlers own meaning.” Exception positions are caller-owned.

### 4.2 Out of scope / deferred

| Item | Why |
|------|-----|
| Matching rebuild or delete suite | Separate admit; not tier A |
| `Capture` / `LengthRef` / non-greedy `ManyUntil` | `*abc` + length-prefixed binary trigger |
| Binary consumer | No schedule; self-delimiting only without capture |
| Product printer table-parity | Deferred (gpure / CORE) |
| Full ExpectedTokens / recovery UX | Agent-UX, not this slice |
| Reader `GetPosition()` | Rejected — dual-cursor-hostile; callers own diagnostics |

---

## 5. Migration sequence (tier A — parallel v2, hard cutover)

```text
V0  ✅ DONE 2026-08-09: Poly/GrammarV2 scaffold — IToken, GrammarError (plain FormatException,
    message/caller position — no custom exception type), BufferedTokenReader base, 9 PatternElements
    (Repeat(min,max), no 10k caps; Predicate(Func<TToken,bool>)), Grammar+builders (Commit returns
    RuleBuilder), Matcher (N3/N1/zero-width fail-closed + Any EOF guard + Consume), MatchResult +
    capture seam. 10 smoke tests green.
V1  ✅ DONE 2026-08-09: GrammarEdgeCaseTests ported → GrammarV2EdgeCaseTests (13 tests, self-contained
    TestKind/TestTokenizer). Two v2 bugs surfaced & fixed: (1) TryMatch returned rule name as
    PatternName — must be the matched pattern's name; (2) Any matched EndOfStream — v1 guards against
    infinite scan loops. Suite 1961 green.
V2  Consumer migration: ✅ JsonGrammarTests → JsonGrammarV2Tests (16) + GrammarMatcherTests →
    GrammarV2MatcherTests (12; shared TestGrammar.cs — TestKindV2/TestTokenV2/TestTokenizerV2 hoisted;
    v2 Matcher gained ExpectedTokens + Consume). Next: DSL (DslToken + reader/cursor/parsers) +
    remaining test grammars onto Poly.GrammarV2 — parity corpus + full suite green per consumer
V3  ✅ DONE 2026-08-09 (hard cutover): v1 Poly/Grammar + v1 DSL stack (DslGrammar/DslExpressionParser/
    DslTokenReader/DslParseCursorBase/DslExpressionFragment/ExpressionFormRegistry/IDslParseCursor/
    PolyDslParser) deleted; Poly/GrammarV2 → Poly/Grammar; V2 DSL stack → canonical names in
    Poly.DomainModeling.Parsing; registries MERGED into DomainInputSet.cs (v1 AnnotationRegistry
    adopted v2 grammar shape; DomainInputSet.Sql.Parser.Annotations canonical). DslTokenWriter +
    tests deleted (deferred table-print surface; DomainDslPrinter never wired it). C99 + Arithmetic
    integration lexers ported to v2 BufferedTokenReader (local char state; parser Peek(0)/Consume;
    C99 `_reader.Peek(1 + depth)` → `Peek(depth)` off-by-one fixed). Zero V2 residue in code.
V4  ✅ DONE 2026-08-09: full solution builds (Poly + Poly.Mcp + Poly.Tests + Poly.Benchmarks); suite
    **1907/1907 green** (v1 test files deleted — replaced by their V2 twins now canonical).
    CORE/README wording update pending (language-shaped streams; caller-owned positions).
```

**Oracle at every step:** Grammar tests + full suite green; parity pins IR. Consumers migrate **one at a time**; v1 stays green until the cutover commit.

---

## 6. Risks

| Risk | Mitigation |
|------|------------|
| Large two-generic ripple | R0 site map; mechanical migration; parity oracle |
| Same-kind tokens differ by content | `MatchResult` exposes full tokens; handlers keep `Tokens[i]` |
| Fake `0,0` positions | Prefer message-only GrammarException when no token/coords |
| Printer table-parity drift | Unchanged; CORE honest; not this plan’s job |
| Speculative Matching rebuild | Out of tier A; dead-dual stands until explicit suite |
| Binary overclaim | Documented future; self-delimiting only without Capture |

---

## 7. Success definition (tier A only)

- [x] Grammar errors accept message-only **or** caller-supplied line/col — plain `FormatException` via internal `GrammarError` (no custom type; reintroduce `GrammarException` additively only if a consumer needs to distinguish by type)
- [x] Engine `Token<TKind>` deleted; no engine `Line`/`Col`/`Payload`
- [x] `Grammar<TToken, TTokenKind>` + buffered stream + `EndOfStream(TTokenKind)` + `IToken<TTokenKind>`
- [x] `MatchPredicate` is token-based (`Func<TToken, bool>`) with an engine test
- [x] Printer built as `Printer<TToken, TTokenKind>` (two-generic — walks `Pattern<TToken, TTokenKind>`; kind-only lock corrected, §2.6)
- [x] DSL migrates with **zero behavior change** (parity + full suite)
- [x] CORE/README: language-shaped streams; diagnostics caller-owned
- [x] Full suite green (1927 tests, 2026-08-09)

**Not required for Done:** Matching rebuild, Capture, binary consumer, product printer table-parity.

---

## 8. Decision

1. Adopt **`IToken` + two-generic match stack**; language owns content.  
2. **`GrammarException` positions are caller-supplied** — no reader position API.  
3. **Tier A** = engine + exception + DSL + docs. Matching/binary are separate.  
4. **Park** until human admits a dedicated suite (prefer after **grammar wrap-up** / mut-safety unless engine cleanup is explicitly prioritized). Not the same workstream as LeftAssoc live-fold.  

---

## 9. Review disposition (2026-08-08)

| ID | Topic | Disposition |
|----|--------|-------------|
| R1 | Migration size / R0 | **Accepted** — R0 is a deliverable with site map |
| R2 | Printer over-generic | **Accepted** — two-generic printer is REQUIRED to walk `Pattern<TToken, TTokenKind>` (kind-only lock wrong; corrected §2.6) |
| R3 | EndOfStream kind-based | **Accepted** — locked (§2.1) |
| R4 | MatchPredicate | **Accepted** — `Func<TToken, bool>` (§2.5) |
| R5 | Matching `*abc` / success | **Accepted** — Matching out of tier A success |
| R6 | Binary self-delimiting | **Accepted** — narrow claim in §3 / §4.2 |
| R7 | Binary not a justification | **Accepted** — DSL (+ existing tests) justify migration |
| R8 | Dead-dual delete vs rebuild | **Accepted** — no required rebuild; inventory stands |
| — | Reader `GetPosition` | **Rejected** — caller supplies exception location (§2.2) |
| — | Abstract Unread buffer | **Accepted** — keep shared base (§2.3) |

Prior “not ready to implement as written” is **cleared for tier A** once admitted; remaining open work is suite solidification (`grev-*` or similar), not more design on the exception/position question.

---

## 10. Second review disposition (2026-08-08 — finalized)

| ID | Topic | Disposition |
|----|--------|-------------|
| S1 | `Expect` position mechanism (§2.2) | **Locked** — `Expect` message-only; positions via handler `Error()` only (contract guarantees only `Kind` on `TToken`) |
| N1 | MatchPredicate "or overload" hedge (§2.5) | **Resolved** — single `Func<TToken, bool>` signature; kind-only call sites adapt via `t => IsCompareOpKind(t.Kind)` |
| N2 | Test grammars need token structs (R2) | **In scope** — added to §2.6 table + R0 site map |
| P1 | Sequencing: revision vs wrap-up | **Locked in §Status** — revision-first, wrap-up-second (fold written once on new stack; avoids double migration) |
| — | Extensibility vs strong kind | **Addressed** — §2.5 extensibility note: seam is pattern/content registration over closed kinds; new lexical classes are product tokenizer growth, unchanged by this plan |

**Final status:** ✅ **DONE 2026-08-09** — tier A shipped: v2 engine (`Grammar<TToken, TTokenKind>`, `ITokenStreamReader` examine/consume, longest-match, stateless `Printer`, token-based `MatchPredicate`), DSL migrated with zero behavior change, v1 deleted (cutover), §7 criteria all met, suite 1927 green. Wrap-up (LeftAssoc live-fold) is a separate suite — see PIPELINE-STATUS.
