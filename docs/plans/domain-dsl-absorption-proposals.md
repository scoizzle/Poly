# Domain DSL absorption proposals — experiment → product

**Date:** 2026-08-02  
**Status:** **Parked** — research + recommended styles complete; **not** an open implementation queue  
**Parked:** 2026-08-04 (admission control — one primary workstream only)  
**Unpark when:** Explicitly admit **one** P* (or a thin vertical that is only that P*) as the sole current suite; create `simple-agent-tasks/*` then.  
**Do not:** start P1–P5 in parallel, re-open grammar re-base as a prerequisite, or treat this doc as CURRENT work.  
**Source experiment:** [`docs/experiments/DOMAIN-DSL-SPEC.md`](../experiments/DOMAIN-DSL-SPEC.md)  
**Product truth:** [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md)  
**Related:** [`CORE.md`](../CORE.md) · [`2026-06-08-domain-lowering-boundary.md`](../decisions/2026-06-08-domain-lowering-boundary.md) · [`2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](../decisions/2026-07-22-persistence-units-medium-facets-pack-syntax-export.md) · [`2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`](../decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md) · SPE suite (complete) · store-aware `Rel exists` (shipped)

---

## 1. Purpose

Recommend **what** from the experiment DSL to absorb next and **how** to implement each slice so it fits Poly’s real architecture (immutable domain → analysis → lower to Syntax → VM; MCP as thin consumer), not a second language stack or intent-log rewrite.

This is **not** a task suite and **not** the agent pick. After an explicit unpark + acceptance of **one** proposal, turn that slice into `docs/plans/simple-agent-tasks/*` micro-tasks.

---

## 2. Research findings (platform constraints)

### 2.1 What the experiment is

A **future-facing language + platform vision** (actors, schedule, packs, imports, HATEOAS). Much of Phase 1–2 semantics already ship under product names (`entity`, nav relationships, `when … as name`, quantifiers, invoke, owned path-prefix, store-aware `exists`).

### 2.2 Seams that already exist (reuse these)

| Concern | Existing machinery | Implication |
|---------|-------------------|-------------|
| Expression IR | `DomainExpression` (+ `DateOperation`, nav, quantifiers, Exists) | Prefer **parse/print + analyze + EvaluatePolicy preprocess** over new IR kinds |
| Date builtins | `CanonicalBuiltInTypeCatalog` `Date` / `DateTime`; CLR map to `DateOnly` / `DateTime` | Types exist; **DSL authoring** is the gap |
| Date arithmetic | `DateOperation` + `DomainExpressionLoweringPass` → `AddDays` / `AddMonths` / `DiffDays`; effect assign can rewrite `Date + n` | Core already has the **runtime shape** for days/months |
| “Now” | Effect lowering `now` / `today` / `guid` defaults | Host clock is already assumed on the VM path |
| Policy store reads | `EvaluatePolicy` → `PreprocessQuantifiers` (path-prefix, quantifiers, `Rel exists`) | Multi-hop and richer exists compose here |
| Action results | `Action.Result` / `InvocationResult`; parser already accepts `-> Type`; printer emits it | Return types are **partially product**; runtime result bag may be thin |
| Subscription quantifiers | IR `StageSubscriptionQuantifier` Each/Any/All; store dispatches all three; **DSL `when` hard-codes Each** | Authoring gap, not runtime gap |
| Invoke quantifiers | DSL `invoke any/all Rel.Action` already product | Pattern to copy for `when any/all` |
| Packs / extensions | Facets + annotation registry + type maps (ADR 2026-07-22); grammar integration says **facets-first**, no pack-specific parser forks | Exotic temporal/calendar rules → pack; universal lifecycle dates → core |
| Analysis | Fail-closed bags; `DomainCatalog` monopath; export consumes analysis | New features must declare deps and fail loud |
| Domain fidelity | Stage transition as observable (ADR 2026-07-17); no event/pub-sub | No reintroducing `event.*`; no schedule as domain “events” |

### 2.3 Implementation styles that fit Poly

| Style | When to use | When not |
|-------|-------------|----------|
| **A. Authoring-only (parse/print)** | Surface already has IR + runtime | Never alone if agents must execute |
| **B. Authoring + analysis + EvaluatePolicy preprocess** | Related reads, store presence, multi-hop | Effects that must lower to VM without preprocess |
| **C. Full lower-to-Syntax/VM** | Assign RHS, action bodies, export C# | Host I/O, real wall-clock timers |
| **D. Facet/pack extension** | Medium-specific or optional domain dialects | Core lifecycle truth every domain needs |
| **E. Host adapter** | Identity, external policy resolution, real `schedule` | Pure domain expression truth |

**Default for this absorption set:** **B then C** for expression/policy work; **D** only after core date *compare/add-days* if calendars diverge; **E** for actors / external / schedule.

### 2.4 Explicit non-absorptions (leave experimental)

- Intent log as product mutation path (product = evolution + analysis).
- Implicit `event` (replaced by `as name`).
- Completing experiment “Phase 2 checklist” as one suite.
- Card editor / full LSP as domain tasks.
- `parallel` / host-orchestrated workflow engines.
- Multi-DBMS pack completeness (DAU parked).

### 2.5 Pack specializations for expressions and effects (units-of-measure style)

**Motivation:** Authors want natural temporal (and later physical) phrasing — `Now - 12 days`, `DueDate + 2 weeks` — without (a) hardcoding every unit into core forever, or (b) forcing C# statics into the domain DSL. C++ libraries that add units-of-measure / user-defined literals are the right *metaphor*: **the language stays generic; libraries specialize operators and units.**

**What exists today**

| Seam | What packs can do |
|------|-------------------|
| `IAnnotationSyntax` + `AnnotationRegistry` | Keywords like `column(...)` / `table(...)` — **declaration** facets |
| Type maps / storage conventions | How `Date`/`Number` project to media |
| Analysis passes (in theory) | Pack-registered analyzers (under-used product path) |
| **Not yet** | Pack-registered **expression operator** or **effect form** specializations |

**Recommended model (Poly-shaped)**

```text
Parse (core grammar)
  → open forms:  Now | today | literal N | Prop
  → open ops:    ± , comparison
  → open units:  bare number | unit suffix/token (days, weeks, …)

Specialization registry (core + packs)
  → resolve (lhs type, op, rhs type|unit) → DomainExpression IR
  → e.g. (DateTime, −, 12 days) → DateOperation(now, 12, AddDays)
  → unknown combo → analysis/parse fail closed

Lower (core)
  → DateOperation / now → generic Syntax (Member/Invoke on host types)
  → no domain opcodes; C# statics only at this boundary
```

| Layer | Responsibility |
|-------|----------------|
| **DSL surface** | Host-neutral: `Now`, `days`, not `DateTime.UtcNow` |
| **Specialization table** | Core seeds temporal defaults; packs add units / exotic ops |
| **Domain IR** | Still small (`DateOperation`, literals, props) — specializers *produce* IR, they don’t invent VM opcodes |
| **Effect lowering** | Same registry for assign RHS / policy expr; new **effect kinds** only when runtime can execute them (fail closed if pack effect without host) |
| **C# / export** | Map resolved IR to CLR statics/instance methods (already: `UtcNow`, `AddDays`) |

**Effects vs expressions**

- `Now - 12 days` is primarily an **expression** specialization (used in assign, policies, later schedule).  
- Pack **effect** specializations (e.g. pack-defined effect keywords) are a **second** extension surface: register parse keyword → `Effect` subtype or desugar to existing effects.  
- Rule: **no pack effect without a runtime path** (VM execute or host adapter). Prefer desugar to assign/transition/create/invoke first.

**Analogy to annotations (do not conflate)**

| Annotations (`IAnnotationSyntax`) | Operator/unit specialization |
|-----------------------------------|------------------------------|
| Attach **metadata** to declarations | Give **meaning** to open expression forms |
| Print/parse facet args | Resolve types/units to IR |
| Storage/codegen consumers | Eval + export consumers |

**C++ units parallel (and limits)**

- Like UoM libraries: units and operator overloads live in libraries; core language stays small.  
- Unlike C++: Poly specializations must stay **analysis-visible** and **fail closed** — no silent “number is days.”  
- Unlike C++: final execution still lowers to **generic Syntax**, not pack-private VM opcodes.

**Seed vs extensibility (v1)**

1. **Core seed:** `Now`/`today`, `days`/`months` (and maybe `weeks` = 7 days), `Date ± duration`.  
2. **Registry API** designed so a second pack can add `businessDays` without forking the parser table for every unit.  
3. **Parser:** keep a small open form (`N <unit-ident>` or `N.unit`) rather than a closed enum of every unit in `TokenKind` forever — unit id is a **registration key**, not a hard-coded keyword per pack (aligns with facets-first / grammar registration direction).

**Anti-patterns**

- Domain DSL that reads as C# (`DateTime.UtcNow.AddDays(-12)`).  
- Pack that only works if `EffectLoweringPass` is forked in core.  
- Pack effects that analyze green but never run.  
- Core hardcoding every unit in `TokenKind` so packs cannot add `businessDays`.  
- Optional-only temporal pack with no default load (product path must work without ceremony).

### 2.6 Built-in temporal pack as first specialization dogfood

**Name (provisional):** `Poly.Packs.Temporal` (or in-tree `Poly.DomainModeling.Packs.Temporal` until packaging splits).

**Role:** Standard-library pack — always registered on product sessions; tests may use a minimal input set without it.

**Registers (v1):**

| Kind | Examples |
|------|----------|
| Types / builtins contribution | Ensure `Date`/`DateTime` usable where catalog already lists them; pack owns *semantics* of temporal ops |
| Clock expressions | `Now`, `today` (and aliases) → IR for “current instant/date” |
| Units | `days`, `months` (optional `weeks` → 7 days in pack, not core) |
| Binary specializations | `(temporal, −|+, duration)` → `DateOperation`; comparisons temporal×temporal |
| Analysis | Type errors for `Date + Date`, `Number + days` without temporal lhs, unknown unit |
| Lowering hooks | Resolved IR → Syntax (`UtcNow`, `AddDays`, …) — same generic node shapes core already uses |

**Core must expose (minimal API shape — names illustrative):**

```text
IExpressionSpecializationRegistry
  RegisterUnit(name, IDurationUnit)
  RegisterBinary(lhsTypePred, op, rhsTypePred, Resolve → DomainExpression)
  // later: RegisterEffectKeyword / desugar

Parse remains pack-agnostic for open forms:
  primary: Now | today | literal | property | …
  postfix unit: expr unitName     // unitName looked up in registry
  binary: expr ± expr
```

**Product wiring:** `DomainFactory` / MCP `CreateWith…` / default `DomainInputSet` includes temporal pack registration next to SQL annotation syntax when present.

**Second pack test (prove extensibility):** a tiny test-only pack that registers `fortnights` (= 14 days) or `businessDays` stub — without editing tokenizer enums.

**Relationship to existing code:** keep `DateOperation` + existing lowerers; move *authoring resolution* into the pack. Avoid two parallel date systems (core hardcode + pack).

---

## 3. Proposal matrix

| ID | Spec idea | Recommend | Style | Priority |
|----|-----------|-----------|-------|----------|
| **P1** | Dates / time arithmetic & compare | **Absorb as built-in temporal pack + core specialization seams** | D (pack) + C lower + B policies | P0 after dogfood |
| **P2** | Multi-hop path-prefix | **Absorb** | B then C for assign RHS if needed | P0–P1 |
| **P3** | Action return types honesty | **Harden existing** | A+C + analysis | P1 |
| **P4** | `when any/all Rel Stage` | **Absorb** | A + existing store dispatch | P1 |
| **P5** | Comment round-trip | **Absorb (thin)** | A only | P2 cheap |
| **P6** | Actor + `actor` in policies | **Defer** until host identity | E + thin A | P2+ |
| **P7** | `policy external` | **Defer** until resolver consumer | E + A | P2+ |
| **P8** | `value { }` + pure functions | **Defer** (owned/entity cover most) | C later | Pull |
| **P9** | `schedule at` | **Host adapter later** | E | Pull |
| **P10** | `for` / `parallel` effects | **Defer** | C/E heavy | Pull |
| **P11** | `match` expressions | **Defer** (sugar) | A+C | Pull |
| **P12** | Domain kind / import packages | **Defer** | Packaging | Pull |

---

## 4. Detailed proposals

### P1 — Dates and temporal expressions (core thin vertical)

**Experiment need:** `DueDate + 14`, compare dates, `schedule at deadline`, library renew.

**Research conclusion:**  
IR and lowering already model day/month arithmetic (`DateOperation`, assign rewrite to `AddDays`). Builtins `Date`/`DateTime` and `now`/`today` exist. **Product DSL authoring and policy eval honesty** are incomplete, not the type system.

**Recommended style:** **Core product vertical (not a pack first).**  
Rationale:

1. Lifecycle domains universally need due dates (RenewLoan was an original Phase 4 forcing function).  
2. Packs own **storage/medium** (column types, dialects) via facets/type maps — not whether “add 14 days” is meaningful in domain policy.  
3. Calendar calendars (business days, time zones) can later extend via **analysis + pack helpers**, without blocking `AddDays` / compare.

**Decision (2026-08): dates via the pack system, built-in first pack.**

Temporal concepts are **not** hard-wired forever into core parser/`TokenKind` tables. They are supplied by a **built-in temporal pack** that is always on the product path (like a “standard library”), and that pack is the **first real consumer** of expression/operator specialization extension points (see §2.5–§2.6).

| Layer | Owns |
|-------|------|
| **Core (substrate)** | Extension registries (units, binary specializations, optional effect desugarers); generic `DomainExpression` forms packs target (`DateOperation` may live in core IR as the *resolved* shape); fail-closed “unknown specialization”; lowering of *already resolved* IR → Syntax |
| **Built-in temporal pack** (product default) | `Date`/`DateTime` as usable domain types in authoring; `Now`/`today`; units `days`/`months`/(optional `weeks`); specializers for `date ± duration`, compare; analysis rules; map resolved IR → CLR (`UtcNow`, `AddDays`, …) |
| **Optional temporal packs later** | Business days, fiscal calendars, alternate clocks — same seams, not a core fork |
| **Host** | Real wall-clock scheduling (`schedule at`) — P9 |

**Why built-in pack rather than “core forever”:**

1. **Forces the extension API to be real** — dogfoods pack registration the same way Sqlite dogfoods facets.  
2. **Matches units-of-measure intent** — core language stays open; library supplies meaning for `Now - 12 days`.  
3. **Still zero-friction for product** — built-in = always registered for MCP/`apply_dsl`/DomainFactory unless tests opt out.  
4. **Leaves room** for a second calendar pack without keyword explosion in `PolyDslTokenizer`.

**Thin vertical (ship order) — “temporal pack + seams”:**

1. **Core seams:** specialization registry API (unit + binary op); parse open forms (`N unitIdent`, `Now`, `±`) without hardcoding every unit in core.  
2. **Built-in pack:** register seed units + specializers → `DateOperation` / clock IR.  
3. **Wire product hosts** (DomainFactory / MCP session / analyze) to load built-in temporal pack by default.  
4. **Goldens:** renew-style assign + policy compare; export uses CLR members.  
5. **Negative tests:** unknown unit fails closed; session without pack rejects temporal authoring (opt-out path).  
6. Out of vertical: wall-clock `schedule at` (P9); non-default calendars.

**ADR lock (recommended):** “Temporal *authoring and specialization* live in a built-in pack on core extension seams; resolved temporal IR lowers generically; scheduling is host.”

**Success:** `Now - 12 days` / `DueDate + 14 days` work with default session; specialization API has one real pack consumer; a second unit pack could register without core edit.

---

### P2 — Multi-hop path-prefix (`a b c`)

**Experiment need:** Nested related reads without inventing join syntax.

**Research conclusion:**  
`RelationshipNavigation` is already a **tree** (`TargetProperty` can nest). Product policy preprocess resolves **one** hop to a target bag then evaluates the inner expression — recursion already works for nested structure *if* inner is another nav, but multi-link/to-many rules and analysis validation are single-hop oriented. Guide marks multi-hop owned as not shipped.

**Recommended style:** **B — EvaluatePolicy preprocess recursion** (same as single-hop), with **analysis** validating each hop’s cardinality and entity property set.

**Algorithm sketch:**

```text
Preprocess(RelationshipNavigation(rel, inner)):
  targets = outbound(rel)   // fail closed 0; for singular hop fail closed >1
  return Preprocess(inner) evaluated on each/sole target
```

For hop chains `loan book Title`:

- Hop1: loan → Book instance  
- Hop2: on Book bag, property Title (or further nav)

**Rules:**

- Only **to-one** hops for bare path-prefix chains (same as single-hop multi-link throw).  
- Many in the middle requires quantifiers (`any loans where book Title is "…"`) — do not invent silent first.  
- Assign **target** remains local-only (no multi-hop writes).

**Not:** New IR node; SQL joins; graph query language.

**Success:** Policy `loan book Title is "X"` with two links (Patron→Loan→Book), store+link golden; analysis rejects many in chain without quantifier.

---

### P3 — Action return types (harden existing)

**Experiment need:** `action(…) -> Type`, create returns, invoke results.

**Research conclusion:**  
Parser already supports `-> Type` → `InvocationResult` member; printer emits it; export has return-type messaging. Runtime `ActionInvocationResult` is still largely success/stage/guards — **result value plumbing** may be incomplete for domain consumers.

**Recommended style:** **Harden end-to-end one return shape**, not full type system.

**Thin vertical:**

1. Inventory: what `-> Entity` / `-> Text` means today at invoke / create / MCP.  
2. One golden: action returns created instance id or primitive via existing effect result member.  
3. Fail closed: declared `-> T` but no producing effect → analysis error (export already hints).  
4. Guide honesty: document what is actually returned to MCP/agents.

**Not:** Generics, union returns, or “last expression is return” without analysis.

---

### P4 — Subscription quantifiers in product DSL (`when any|all Rel Stage`)

**Experiment need:** Fire when *any* or *all* related entities enter a stage set.

**Research conclusion:**  
Runtime store already dispatches `Each` / `Any` / `All`. Product `when` parse **always** stamps `Each`. Invoke already parses `any`/`all` — copy that tokenizer/parser pattern.

**Recommended style:** **A (parse/print) + zero new runtime** (reuse store). Analysis: quantifier vs cardinality (already warned for singular + Any/All).

**Grammar:**

```text
when [any|all] Rel Stage[, Stage…] [as name] { effects }
```

Default omit quantifier = `Each` (current).

**Peer binder:** Allowed with Any/All; peer instance = transitioned instance for that notify (same as today). Document that All/Any fire on **set state after transition**, not “every peer bag at once.”

**Success:** DSL round-trip + store golden for Any; Each regression green.

---

### P5 — Comment round-trip

**Experiment need:** Diffable, LLM-friendly files with comments.

**Research conclusion:**  
Tokenizer does not preserve `//` / block comments as AST. Domain model has no comment nodes.

**Recommended style:** **A only — trivia channel**, not domain IR.

**Options (pick one in implementation):**

| Option | Pros | Cons |
|--------|------|------|
| **5a. Drop on parse** (document) | Zero IR churn | Export loses comments |
| **5b. Leading trivia on next declaration** | Good enough for human files | Fragile reordering |
| **5c. Comment facets / annotations** | Fits pack facet model | Heavy for `//` |

**Recommend 5b** for product guide files: associate `//` lines with following declaration in parse→print; no analysis. Fail closed: never invent comments.

**Not:** Full source maps or prettier-level fidelity in v1.

---

### P6 — Actors (deferred proposal shape)

**Experiment need:** `actor`, `actor` keyword in policies, `require Employee.Warehouse`, RBAC links.

**Research conclusion:**  
Authorization needs a **host-supplied authenticated subject**. Domain policies already evaluate against entity bags; no session principal in `EvaluatePolicy` / `InvokeAction` today.

**Recommended style when pulled:** **E (host) + thin domain markers.**

1. Domain: `actor` as **entity stereotype** (metadata/facet or `Entity` flag) — lowers to entity, not a second type system.  
2. Policy expressions: reserved `actor` resolves via **evaluation context** parameter (host injects principal bag or domain instance).  
3. `require OtherEntity.Policy` — already partially name-qualified in experiment; implement as policy reference with **evaluation against principal or related actor instance**, fail closed if principal missing.  
4. MCP: session binds principal before `invoke_action` / `evaluate_policy`.

**Do not:** Ship `actor` keyword without principal context (vacuous auth).  
**Do not:** Separate permit/deny language — keep `require` + policies (experiment is right here).

---

### P7 — External policies (deferred proposal shape)

**Experiment need:** `Warehouse: policy external`.

**Recommended style:** **E — named predicate, host resolver.**

- Domain stores `Policy` with external flag / empty body.  
- Analysis: actions may `require` them; body expression optional.  
- Runtime: `EvaluatePolicy` delegates to `IExternalPolicyResolver.Resolve(name, subject)` registered on session/store.  
- Fail closed if resolver missing.

Mirrors ASP.NET policy names; fits “naming is the integration seam” from the experiment.

---

### P8–P12 — Deferred sketches (no suite yet)

| ID | Style | One-liner |
|----|-------|-----------|
| **P8 value types** | C after second consumer | Prefer owned entity until Money/Address repeat forces `value` |
| **P9 schedule** | E host clock + cancel on stage exit | Domain effect is *intent*; host runs timer; auto-cancel via stage exit subscription |
| **P10 for/parallel** | C/E | `for` might lower to loop Syntax; `parallel` is host workflow — not VM domain |
| **P11 match** | A+C sugar | Desugar to if/else chain in analysis/replace |
| **P12 import/kind** | Packaging | After multi-domain packaging consumer; grammar registration if packs add keywords |

---

## 5. Cross-cutting implementation rules

1. **No domain VM opcodes** — all new meaning lowers to Syntax or preprocess to literals (ADR domain-lowering / VM-canonical).  
2. **Fail closed** — empty many path-prefix, missing store for related reads, missing principal for actor policies.  
3. **Guide + CORE same change** as surface.  
4. **Tests:** TUnit; construct illegal state; named consumer golden.  
5. **Grammar framework:** not a prerequisite for P1–P5; hand RD parser remains product path until pack keyword explosion (facets-first ADR).  
6. **Dogfood before actors/schedule** — force P1–P4 with MCP scenarios first.

---

## 6. Suggested sequencing (after this proposal is accepted)

```text
Dogfood current surface
    → P1 dates (core thin)     [optional ADR: temporal core vs pack]
    → P2 multi-hop
    → P4 when any/all  ∥  P3 return-type harden
    → P5 comments
    → (host ready) P6 actors + P7 external
```

Parallelization: P3 and P4 after P1/P2 are largely independent; P5 anytime.

---

## 7. Open decisions for stakeholders

1. **Dates:** ~~core vs pack~~ → **built-in temporal pack + core seams** (accepted direction 2026-08). Remaining: package name / always-on registration surface.  
2. **Multi-hop only in policies first**, or also assign RHS? Recommend **policies + assign RHS** if same preprocess can feed effect eval.  
3. **Comment fidelity** — 5b leading trivia vs ship without comments?  
4. **Actor timeline** — block on MCP session principal design?  
5. **Specialization API breadth v1** — expressions only, or also effect-keyword desugar in the same registry?

---

## 8. Document maintenance

When a proposal is accepted and tasked:

- Link suite under `docs/plans/simple-agent-tasks/`  
- Update `docs/plans/README.md` and master-roadmap agent pick  
- Keep experiment spec as **vision**; product guide remains sole `apply_dsl` truth  

---

## 9. Summary recommendation

**Best platform fit:** absorb experiment features by **extending existing expression IR + store preprocess + hand DSL**, with **pack-registered specializations** for units/ops (built-in temporal pack as first dogfood). Not: C# in the DSL, domain date opcodes, grammar rewrite first, or host-heavy features until identity/clock resolvers exist.

**Next product pulls from the experiment:** **built-in temporal pack + specialization seams**, multi-hop reads, when quantifiers, return-type honesty, comments. **Actors / external / schedule / value / import** stay proposal-deferred with clear host seams.
