# Grammar Framework Integration — Poly.Grammar → product DSL

**Date:** 2026-07-26  
**Revised:** 2026-08-07  
**Status:** **GI-1…7 + E1 landed (2026-08-07).** Structure/annotations Matcher; **E1** `DslExpressionParser` + `ExpressionFormRegistry` (open primaries for temporal); effects still structure RD. Park: GI-8 JSON expr, GI-9 binary. Temporal pack may admit on open-form seam.  
**Engine:** [`Poly/Grammar/`](../../Poly/Grammar/) + [`Poly/Grammar/README.md`](../../Poly/Grammar/README.md)  
**Product DSL today:** [`Poly/DomainModeling/Parsing/`](../../Poly/DomainModeling/Parsing/) (~2.4k LOC hand RD)  
**Product truth:** [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md)  
**Platform:** [`docs/CORE.md`](../CORE.md) — Grammar owns pattern-table engine; DomainModeling owns domain DSL until this plan lands  
**Related:** temporal research [`p1-temporal-research.md`](p1-temporal-research.md) · absorption [`domain-dsl-absorption-proposals.md`](domain-dsl-absorption-proposals.md) · decomposition [`domainmodeling-decomposition-proposal.md`](domainmodeling-decomposition-proposal.md)

---

## 1. Purpose

Make the **product** `.poly` pipeline (tokenize → parse → `DomainChange[]` → evolve; print from `Domain`) driven by **`Poly.Grammar`** (pattern table + matcher + printer), so:

1. New **syntax** is registration, not forking `PolyDslParser`.  
2. **Built-in and optional packs** (SQL facets, later temporal units/forms) share one parse/print seam.  
3. Agents get position-aware errors and a path to `ExpectedTokens` / completion.  
4. Hand recursive descent stops absorbing every new surface (multi-hop, any/all, peer `as`, …).

This is **not** a redesign of domain semantics, evolution, or analysis. Output remains the same `DomainChange` / `Domain` types.

---

## 2. Why now (2026-08)

| Driver | Detail |
|--------|--------|
| **Temporal pack (P1)** | Accepted direction: built-in temporal pack + specialization registries. Implementing units/`Now`/duration **in hand RD** then re-porting is waste. **GI before temporal pack implement.** |
| **RD surface strain** | Product DSL has grown (path-prefix, multi-hop, quantifiers, peer binder, create-in, …) inside ~1.5k LOC parser + ~0.6k printer. Each feature is another special case. |
| **Pack annotations** | `column(...)` still goes through core `ParseAnnotation` + `IAnnotationSyntax` — every facet keyword is a core edit. |
| **Engine proven (structure)** | JSON unit tests + **GIP C99 dual-run** (structure Matcher + RD expr hybrid). See §3.4 (done) and [`gi-preflight-c99-notes.md`](gi-preflight-c99-notes.md). E1 pure expr grammar still open. |

### Named consumers (admit bar)

| Consumer | Needs GI when… |
|----------|----------------|
| **Temporal pack** | Open units / `Now` / duration forms without core RD forks |
| **SQL / facet packs** | Annotation patterns registered outside `PolyDslParser` |
| **MCP `apply_dsl`** | Same product surface; better line/col errors over time |
| **DSL agents** | Completions / recovery via grammar first-token sets |

### Explicit non-consumers (do not block GI)

- VM / analysis / catalog monopath  
- Temporal **runtime** IR (`DateOperation` already exists)  
- Host `schedule at` (P9)  

---

## 3. Current state inventory

### 3.1 Engine (done)

| Piece | Location | Notes |
|-------|----------|--------|
| Pattern table, Matcher, Printer | `Poly/Grammar/` (~12 sources) | Media-agnostic `TokenReader` / `TokenWriter` |
| Tests | `Poly.Tests/Grammar/` | Matcher 12 + edge 13 + JSON grammar 17 + JSON printer 12 ≈ **54** |
| Docs | `Poly/Grammar/README.md` | Element set, usage |

### 3.2 Product DSL to re-home (live counts 2026-08)

| File | ~LOC | Role after GI |
|------|------|----------------|
| `PolyDslTokenizer.cs` | ~300 | → `DslTokenReader : StringTokenReader<DslTokenKind>` |
| `PolyDslParser.cs` | ~1,475 | → Matcher + **handlers** producing `DomainChange` (+ expression strategy §5) |
| `DomainDslPrinter.cs` | ~620 | → Printer + domain walk callbacks |
| `IAnnotationSyntax` / `AnnotationRegistry` | small | → pack grammar registration (facets) |
| `DomainExpressionJsonParser.cs` | ~200 | **Parallel track** — JSON policies; not blocking text DSL cutover |

### 3.3 Gone / stale (do not plan around these)

| Old plan assumption | Reality |
|---------------------|---------|
| DomainAuthoringContext on critical path | **Removed** (dar suite) — use explicit inputs / session / packs as they exist today |
| “31 round-trip tests” only | **Regression bar is the full product DSL suite** — at least `PolyDslRoundTripTests`, `N1NavigationTests`, `AnnotationRoundTripTests`, subscription/path-prefix/quantifier goldens, MCP DSL smokes. Count evolves; **do not hardcode 31**. |
| Phase 1a-only grammar | Product surface is **guide-current** (see §4), not archived phase1a docs |

### 3.4 GI-preflight — verify Grammar before product integration

**Status: DONE (2026-08-07).** Findings: [`gi-preflight-c99-notes.md`](gi-preflight-c99-notes.md). Product GI-1…3 already used the same hybrid pattern.

**User direction (2026-08-06):** high-value gate — **prove `Poly.Grammar` under a realistic non-JSON language** before product GI. Shipped as C99 structure Matcher dual-run (expr remains RD = E2 hybrid).

#### Why C99 (existing harness)

| Asset | Location / role |
|-------|-----------------|
| **Harness** | [`Poly.Tests/Integration/C99ParserInterpreterTests.cs`](../../Poly.Tests/Integration/C99ParserInterpreterTests.cs) (~1k LOC, ~20 end-to-end cases) |
| **Lexer already Grammar** | `C99TokenReader : StringTokenReader<C99TokenKind>` — token media path is already real |
| **Parser still hand RD** | `C99Parser` — recursive descent → Poly AST → LINQ → execute |
| **Stress shape** | Arithmetic / comparison / logical / ternary; if/else, while, for; structs, member access, assignments — closer to **DSL expression + block** load than JSON |

JSON grammar tests prove pattern tables + printer for **data**. C99 dual-run proves Matcher + handlers for **language-like** structure without touching product `DomainChange` or hand RD.

#### What preflight is / is not

| Is | Is not |
|----|--------|
| Default **readiness** suite before product GI admit | Part of product DSL cutover (GI-1…GI-7) |
| Dual-run: **hand `C99Parser` vs Matcher+handlers** on the same source corpus | Replacing C99 as a product language |
| Evidence that E2 hybrid / E1 expression work is feasible on this engine | A substitute for product golden corpus |
| Cheap feedback if Matcher/pattern elements need gaps filled | Blocking temporal **research** docs |

#### Preflight work (suggested slice IDs)

Use when solidifying: `gi-preflight` (or `gip-*`) — **before** `gi-0` product corpus locks if the engine is unproven for this load.

| ID | Work | Exit |
|----|------|------|
| **GIP-0** | Inventory C99 subset surface from existing tests; list statement/expr constructs | Checklist in suite README or this § appendix |
| **GIP-1** | `Grammar<C99TokenKind>` for the supported subset (expr precedence strategy explicit: nested rules vs Pratt-in-handler) | Patterns cover every construct used by current tests |
| **GIP-2** | Handlers: `MatchResult` → same Poly AST shape as `C99Parser` (or dual-run structural equality of AST / same LINQ result) | Dual-run green on **all** existing `C99ParserInterpreterTests` cases |
| **GIP-3** | Gaps doc: missing pattern elements, precedence pain, handler complexity vs RD | Written findings → feed GI-4 expression strategy; only then admit product `gi` |

**Fail closed:** if dual-run diverges or a construct cannot be expressed without engine changes, **stop** — fix Grammar or document a product-GI waiver. Do not start GI-1 product tokenizer work on a known-broken Matcher story.

#### Sequencing impact

```text
DONE:     engine unit + JSON; GIP-0…GIP-3 (C99 dual-run); product GI-1…3 hybrid
NEXT:     GI-4 pack annotations → GI-5 printer → GI-7 cutover
THEN:     p1 temporal pack implement (needs E1 or open-form registration)
```

---

## 4. Product surface the grammar must cover

Single source of **syntax** truth: **`poly-dsl-guide.md`**. At cutover, grammar + handlers must accept and print everything the guide claims is product, including at least:

| Area | Examples (not exhaustive) |
|------|---------------------------|
| Structure | `domain`, `entity`, properties, constraints, enums, stages, actions, params, `-> Type` |
| Relationships | N1 nav, `many`, `owned` |
| Effects | transition, assign, create / create in, delete, invoke (+ any/all), if/else, composite flatten |
| Subscriptions | stage + entity-level `when`, `any`/`all`, peer `as name` |
| Policies / require | expressions: path-prefix, multi-hop to-one, exists, quantifiers, comparisons, bool/arithmetic |
| Facets | `column` / pack annotations as registered today |

**Out of product grammar v1 (park):** experiment-only actors, schedule, for/parallel, temporal **until** pack registers forms post-GI.

---

## 5. Architecture locks

### 5.1 Dual path until cutover

```text
                    ┌─ hand RD (PolyDslParser) ──┐
  .poly text ───────┤                           ├──► DomainChange[] ──► Evolution
                    └─ Grammar Matcher+handlers ─┘
                         (parity harness)
```

- **One** product entry (`apply_dsl` / `PolyDslParser.Parse` façade) until GI gate.  
- Internal dual implement **allowed** only with **shared golden corpus** (parse both → same change shape / domain round-trip).  
- Cutover: façade calls grammar path only; delete hand RD in GI-cleanup.

### 5.2 Expression strategy (critical)

Policy/effect **expressions** are the hard part (precedence, multi-hop, quantifiers). Options:

| Option | When |
|--------|------|
| **E1. Nested expression grammar** | Preferred end state — `Grammar` rule for expr with precedence via pattern layering or Pratt-in-handler |
| **E2. Hybrid** | Structure + top-level members via Matcher; **expression body** still RD **behind a single interface** `IExpressionParser` until E1 |
| **E3. Full RD forever for expr** | Reject for temporal pack — reintroduces the problem |

**Lock for this plan:**

1. ~~E2 hybrid structure~~ **done** (GI-3…7).  
2. ~~**E1**~~ **done (2026-08-07):** `DslExpressionParser` owns precedence layers; packs register open primaries via `ExpressionFormRegistry` / `IExpressionPrimaryForm` without core edits. Temporal units/`Now` register forms here.  
3. Pure pattern-table left-assoc (no RD loops) is **not** required — Pratt/RD layers inside `DslExpressionParser` are the E1 end state for this engine.

### 5.3 Facets vs open expression forms

- **Facets-first** remains default for **declaration** metadata (`column(...)`).  
- **Expression specialization** (temporal units, `Now`) is a **second** extension surface (registries) — grammar must not force every unit into `TokenKind` forever; prefer **identifier/unit registration** or open value + analysis resolve (see temporal design).

### 5.4 Placement

| Concern | Module |
|---------|--------|
| Pattern engine | `Poly/Grammar` |
| DSL token kinds, product grammar table, handlers, DSL printer | `Poly/DomainModeling` (or `Poly/DomainModeling/Dsl/` under same assembly) until a second consumer forces `Poly.Dsl` |
| Packs | Register into product grammar + annotation hooks; **no** pack-private full parser |

### 5.5 Non-goals

- Changing `DomainChange` / evolution / analysis contracts  
- Intent-log mutation path  
- Binary/non-text streams as gate for text DSL cutover (follow-on GI-X)  
- Completing ExpectedTokens UX in v1 (nice-to-have after green cutover)  
- Parallel CURRENT with temporal pack **implementation**

---

## 6. Work slices (GI)

Slice IDs **GI-N**. Prefer one micro-task suite file per slice when admitted (`docs/plans/simple-agent-tasks/gi-*.md`).

### GI-0 — Design locks + corpus (docs + harness skeleton)

**Do first.**

- Freeze dual-path + expression hybrid policy (§5).  
- List **regression corpus**: all product DSL tests that must stay green (by path/class name).  
- Note temporal: pack implement blocked until cutover + expr registration path.  

**Exit:** Short `gi-0` notes in suite or this plan’s appendix; no product behavior change required.

### GI-1 — Tokenizer → `DslTokenReader`

Port `PolyDslTokenizer` to `StringTokenReader<DslTokenKind>`.

**Exit:**

- Golden token streams for ≥10 representative inputs (structure + expr + annotations).  
- Existing callers compile (adapter OK).  
- Risk: Low.

### GI-2 — Product structure grammar table

`Grammar<DslTokenKind>` for **domain structure** (domain, entity members, stages, actions headers, when **headers**, constraints, nav props) — not full expression trees if hybrid.

**Exit:**

- Patterns cover guide structure surface.  
- First-token sets non-empty for major rules.  
- Gaps vs element set listed (no silent skip).  
- Risk: Medium (when/action headers, nested blocks).

### GI-3 — Structure parse handlers → `DomainChange`

Matcher loop + handlers for structure; expressions via hybrid interface.

**Exit:**

- Full **corpus green** on grammar structure path (or dual-run equality).  
- Same `DomainChange` types.  
- Line/col errors for structure failures.  
- Risk: Medium-high.

### GI-4 — Expression path (hybrid complete or E1)

| Prefer | Work |
|--------|------|
| Hybrid v1 | Isolate RD expr behind `IDslExpressionParser`; all expr tests green |
| E1 (needed before temporal pack) | Expression grammar + nested matcher / precedence; remove RD expr |

**Exit:** Document which; all expression-related product tests green.  
**Risk:** High for E1; Medium for hybrid isolation.

### GI-5 — Facet / pack annotation registration

Replace hard-wired annotation parse with grammar registration; SQL `column` round-trip without editing matcher core.

**Exit:** Annotation round-trips green; pack registers patterns.  
**Risk:** Medium.

### GI-6 — Printer port

Domain walk + `Printer`/`TokenWriter`; round-trip corpus green; stable-ish output (canonical rules as today).

**Exit:** Print → parse → structural equality (or documented whitespace rules).  
**Risk:** Medium.

### GI-7 — Cutover + delete hand RD

- Product façade uses grammar path only.  
- Remove obsolete tokenizer/parser/printer (or thin obsolete shims one release).  
- Update CORE, DomainModeling README, guide if error shapes change.  

**Exit:** No dual product path; CI green; CORE placement accurate.  
**Risk:** Medium (delete blast radius).

### GI-8 — JSON expression parser (optional / pull)

Port `DomainExpressionJsonParser` onto Grammar **after** text cutover. Does **not** block temporal pack if JSON stays bag-local.

#### Why GI-8 exists (and when it matters)

| Fact | Implication |
|------|-------------|
| **Two authoring media for the same IR** | Text `.poly` uses `DslExpressionParser`. MCP/oracle tools also accept **JSON expression bags** via `DomainExpressionJsonParser` (`DomainTools`, `OracleTool`). |
| **Same `DomainExpression` tree** | Policies/effects must mean the same thing whether written as `Age >= 18` or `{"property":"Age","op":">=","value":18}`. |
| **Text path is now Grammar-hosted** | Open forms (E1 registry) land on the text cursor first. JSON remains a **hand-written shape detector** — packs that add `Now` / units to text do **not** automatically appear in JSON. |
| **What GI-8 would do** | One pattern table (or shared form registry) driving **both** media so specialization stays single-source; fewer dual-oracle bugs. |
| **Why not blocking now** | JSON surface is smaller (no full precedence string); product goldens and MCP JSON are green; temporal pack authoring is **text-first**. Pull GI-8 when JSON must carry the same open forms or dual-media drift hurts. |

### GI-9 — Non-text streams (defer)

Binary token payloads / stream readers — only with a concrete consumer. **Not** CURRENT for text DSL.

---

## 7. Sequencing vs temporal and roadmap

```text
DONE:     product pipeline (dogfood, amu, p4, p3, p2, …)
          Grammar unit + JSON; GIP C99 dual-run; product GI-1…3 hybrid
CURRENT:  (none) until admit next GI slice (GI-4+)
THEN:     GI-4…GI-7 cutover; then p1 temporal pack (E1 or open-form path)
PARK:     GI-8 JSON, GI-9 binary, ExpectedTokens polish
```

| Rule | |
|------|--|
| **GIP preflight complete** | §3.4 — no longer a gate |
| Do not implement temporal **pack** keywords in hand RD while GI is planned | |
| Research/design lock for temporal may proceed in parallel (docs only) | |
| One CURRENT implementation suite at a time | |

---

## 8. Agent pick

### 8a. Preflight — DONE

GIP-0…GIP-3 complete. Evidence: `DualRun_*` + default `CompileC99` dual-compiles both paths; notes in [`gi-preflight-c99-notes.md`](gi-preflight-c99-notes.md).

### 8b. Product suite (when admitted)

```text
DONE:    gi-1 tokenizer, gi-2 structure grammar, gi-3 structure handlers (hybrid)
CURRENT: (none) until admit
THEN:    gi-4 pack annotations  (or expression isolation / E1)
THEN:    gi-5 printer
THEN:    gi-6/7 cutover + docs  (optional gi-8 JSON)
PULL:    gi-9 non-text
BLOCK:   temporal pack product suite until GI-7 (or explicit hybrid waiver + E1 date)
```

### Copilot / orchestrator

```bash
copilot --agent plan-suite-until-done -p "Suite: gi. Mode: until-done."
```

Suite files when solidifying: `docs/plans/simple-agent-tasks/gi-README.md`

---

## 9. Hard rules

1. **Corpus green** every merge — no “fix later.”  
2. **Same DomainChange types** — no parallel mutation IR.  
3. **Guide honesty** — if errors or syntax change, update `poly-dsl-guide.md` same change.  
4. **CORE** updated at cutover (Grammar owns engine; DomainModeling owns product grammar table).  
5. **No pack-private full parsers.**  
6. **Document gaps** in the task before inventing new pattern elements.  
7. **Name types for what they are** (`DslTokenReader`, `DomainDslGrammar`) — not “Visitor.”  

---

## 10. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Matcher unproven on expr/blocks | **GIP C99 dual-run** before product GI (§3.4) |
| Expression precedence hell | Hybrid first; E1 as dedicated GI-4; C99 preflight surfaces pain early |
| Dual-path drift | Shared corpus; fail CI if hand vs grammar diverge on corpus |
| Scope creep to “perfect grammar” | Cutover when guide-product surface green; polish later |
| Temporal forced early | Roadmap block: pack implement after GI-7 |
| Printer whitespace churn | Prefer structural equality tests; golden text only where already required |

---

## 11. Success definition

### 11.1 Preflight (GIP) — before product suite

- [x] C99 structure covered by `C99Grammar` + Matcher dispatch (expr hybrid RD).  
- [x] Dual-run: Matcher path matches hand `C99Parser` **execute** outcomes on integration corpus (`DualRun_*`).  
- [x] Gaps documented → [`gi-preflight-c99-notes.md`](gi-preflight-c99-notes.md); E2 hybrid confirmed under load.  

### 11.2 Product suite (GI)

- [x] Product `.poly` **structure + annotations** grammar-driven; façade `PolyDslParser`.  
- [x] Legacy `PolyDslTokenizer` deleted (GI-7).  
- [x] Facet packs: `CanAccept` + optional `ContributePatterns` without editing matcher core.  
- [x] Domain walk printer remains product print façade; `DslTokenWriter` for Grammar Printer.  
- [x] **E1** `DslExpressionParser` + `ExpressionFormRegistry` (open primaries; precedence layers).  
- [x] CORE + DomainModeling README placement updated.  
- [x] Temporal pack registration path = `IExpressionPrimaryForm` + grammar contributors.  
- [ ] GI-8 JSON dual-media (pull).  
- [ ] Master-roadmap CURRENT — set when admitting next suite.  

---

## 12. Appendix — stale content removed from prior draft

- “CURRENT: GI-1” as if already admitted — replaced by admit control.  
- DomainAuthoringContext as critical path — gone.  
- Hardcoded “31 tests” — replaced by living corpus.  
- Phase 1a-only scope — replaced by guide-current product surface.  
- Obsolete subscription example (`enter on entry`) — ignored.  
- “Engine proven” via JSON alone — superseded by §3.4 C99 preflight gate.  

---

## 13. Next actions (human / agent)

1. ~~GIP preflight~~ **done.**  
2. Solidify `gi-README` + remaining `gi-4`…`gi-7` micro-tasks (gi-1…3 already in tree).  
3. Admit next GI slice (prefer **gi-4** pack annotations or **gi-5** printer) as CURRENT.  
4. Keep `p1-temporal-design-lock.md` parked until GI cutover / E1 path clear.  
5. Do not start temporal product code until GI success definition met (or written waiver).  
