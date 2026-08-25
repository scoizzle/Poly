# DomainModeling + Domain DSL — Tour Feedback

**Date:** 2026-07-16  
**Scope:** `Poly/DomainModeling/`, `docs/CORE.md` (domain path), `docs/experiments/DOMAIN-DSL-SPEC.md`, worked examples under `docs/experiments/examples/`  
**Status:** Opinion / design review — not an ADR or execution plan

This note captures a walkthrough of the DomainModeling engine and the Domain Export DSL draft, then opinions on direction, tensions, and prioritization.

---

## 1. What DomainModeling is

An **immutable domain IR**, not a language. Domain objects hang off `DomainObject : Node`, so the shared Syntax analysis substrate applies.

| Layer | Role |
|--------|------|
| **Core graph** | `Domain` → types (`Entity`, `ValueType`, `Event`, primitives), first-class `Relationship`s, contracts |
| **Lifecycle** | `Stage` (actions, policies, on-entry/exit effects), `Action` (params, effects, policies), `Policy` (`DomainExpression`) |
| **Effects** | Assign, create/delete, publish, transition, link/unlink, invoke, composite, conditional |
| **Evolution** | `DomainEvolution`…`Apply`: batch `DomainChange`s → proposed root → **analysis gate** → accept or discard |
| **Analysis** | Large pass set (structural, semantic, effects, events, correlation, causality, replay, constraints, contracts, …) |
| **Lowering** | `DomainExpression` → Syntax AST fully; effects only partially (assign/composite/conditional → VM; stage/create/invoke still on `DomainEntityInstance`) |
| **MCP** | Thin session + tools over bootstrap / evolution / queries |

Architectural spine matches CORE:

```text
immutable Domain → evolution Apply → analyzers → (eventually) lower to generic AST/VM
```

Hard lines that hold: no domain-specific VM opcodes; immutability at the domain boundary; mutate only through gated evolution.

---

## 2. What the DSL is trying to be

`DOMAIN-DSL-SPEC.md` defines a **human/LLM-facing import/export language**. JSON/MCP remains the machine wire protocol. That split is correct:

| Surface | Job |
|---------|-----|
| **DSL (`.poly`)** | Authoring, review, diffing, prompt generation, handoff artifacts |
| **JSON / intents** | MCP calls, automation, replay, compatibility |

Intended paths:

```text
Parse:  .poly → parser → DomainMutationIntent[] / DomainChange → evolution → committed Domain
Export: committed Domain → canonical printer → .poly
MCP:   tools → JSON args/responses (unchanged)
```

Design has clearly **converged** in the worked examples (v0.3 framing):

- Drop separate `event` / `publish` / `subscribe` / `workflow` as primary concepts
- Observable fact = **entity enters a stage**
- Correlation = **relationship path** (`when calls Ended { … }`)
- Time = `schedule at`
- Auth = **policies** + `require`, with `actor` as entity + authorization metadata
- Relationships as **entity-typed properties** (`orders: many owned Order`)

`Name: Kind` is a strong, uniform declaration pattern. The worked models (`phone-call`, `franchise-crm`, `order-fulfillment`, `grep`) are the strongest material in the experiment docs: they force the ontology against real shapes.

---

## 3. Opinions

### 3.1 The ontology is the product; the C# records are a storage shape

Stages as a graph, actions as the only mutators, policies as named predicates, and “a subject may only mutate itself; others react via subscription” form a **coherent transactional domain calculus**, not CRUD-plus-enums.

If only one vertical is made clean end-to-end, it should be:

**entity · property/relationship · stage · action · effect · policy · gated evolution**

Contracts, parallel execution, library packages, multi-backend codegen, and similar should wait for a second real consumer.

### 3.2 Evolution + analysis gate is the best idea in the module

`Apply` that either installs a new root or rolls back with diagnostics is exactly what agent/MCP loops need. Treating evolution steps as information diagnostics is a good operator-facing detail.

That path matters more than a prettier C# builder API. Dogfood should stay: **mutate through evolution, refuse bad models, export a stable view**.

### 3.3 Biggest tension: IR still loves events; DSL has killed them

| DSL (settled direction) | Engine today |
|-------------------------|--------------|
| Stage transitions are the observable | First-class `Event`, `PublishEventEffect`, `EventSubscription` + correlation bindings |
| `when path Stage` | Analyzer surface around publish / subscribe / flow / replay |
| Relationships as properties | `Relationship` as domain-level member + `SourceOwnsTarget` |
| `actor` keyword | No dedicated actor type (entity + conventions / gap) |

Builders and older examples still teach **publish on entry** (e.g. `PersonLifecycleViaBuilders`). The DSL notes that model as obsolete for primary authoring.

**Recommendation:** Commit the IR toward “stage-as-event,” or treat the DSL as fiction. Keeping both indefinitely freezes analyzers around a vocabulary the authoring surface abandoned. Prefer:

1. IR: minimize event machinery, or treat stage-transition observation as the only bus  
2. DSL: `when … Stage` lowers into whatever residual subscription model remains  
3. Deprecate hand-authored `Event` + `Publish` as the primary product path  

### 3.4 The DSL is the right front; don’t let it become a general language

Phase 1 surface (entities, constraints, stages, actions, policies, relationships-as-properties) is excellent for LLMs and line-oriented diffs.

Phase 2+ accretes a lot:

- `parallel` + dependency solver  
- `for` / `match` / collection query DSL  
- `schedule at`  
- external policies  
- domain kinds `service | cli | library` + package import  
- REST / gRPC / GraphQL / HATEOAS / DB schema as “emergent” benefits  

Those benefits are a strong **narrative** (especially HATEOAS from stage edges, and policies as query predicates). They are also a classic second-system trap.

`grep.poly` is a brilliant design probe and a warning: once the model needs `for line in content.lines where matches…` and `io.readFile`, it is halfway to an embedded programming language. **Keep grep as a stress test, not as Phase 1 acceptance criteria.** Ship Order / Call / CRM first.

### 3.5 Mutation surface vs compact DSL

The engine exposes a wide `DomainChange` algebra (events, contracts, relationship stages, subscription field tweaks, …). The DSL collapses structure into declarative blocks.

**Recommendation:** DSL import should favor fewer high-level changes (“replace/merge this entity block”) over dozens of micro-edits. MCP can stay incremental for exploration; file import should not force forty tool calls to say the same thing.

### 3.6 Relationships-as-properties is the right authoring model

Owning side only + synthesized reverse navigation matches how people and LLMs think. First-class `Relationship` records can remain as **engine normalization** if needed, but the product face should be property lines.

Ownership, many-to-many, and optional reverse aliases need careful analyzer rules; “parser detects alias, not a second edge” is subtle and easy for agents to get wrong.

### 3.7 Lowering is honest but incomplete — and that is OK for now

`DomainExpression` → Syntax is clean and on-mission. Effect lowering returning `null` for stage / create / invoke and running on `DomainEntityInstance` is a pragmatic dual path. It is also exactly the seam CORE warns about: two execution stories.

**Recommendation:** Do not invent domain opcodes. Do prioritize **one coherent runtime loop**:

```text
invoke action → effects → stage change → fire stage-scoped `when`s
```

Until that loop is real under generic AST/VM composition, HATEOAS and full-stack codegen remain aspirational.

### 3.8 Spec quality: vision strong, document multi-era

**Strengths**

- Clear problem statement and non-goals  
- Parse / export / MCP split  
- Four authoring paths converging on `.poly`  
- Policy / `require` / `when` design  
- Phone-call + billing subscription as a worked reactive model  

**Weaknesses**

- Phase 2 e-commerce sample still shows `event` / `workflow`, then notes they are dead  
- Phase 1 sample syntax drifts (`action Cancel` vs `Cancel: action`)  
- Deployment modes and full-stack codegen sit next to unimplemented grammar  

Treat the file as a **design laboratory** (consistent with `docs/experiments/`), not a build checklist. When parser work starts, promote a thin **Phase 1 freeze** into `docs/plans/`.

### 3.9 Fit to platform principles

| Principle | Fit |
|-----------|-----|
| Domain model is the key artifact | Strong — tools serve the model |
| End-to-end ownership | Intentional; incomplete past policy-expression lowering |
| Smallest coherent slice | Spec is ahead of principles; IR is broader than needed for one closed loop |
| Working code before abstractions | Analyzers / contracts / event machinery look inventory-heavy relative to a closed vertical slice |
| Dogfood / trust bar | DSL + MCP + evolution is the right loop if capability claims stay honest |

---

## 4. Bottom line

The direction is strong. This is not “another schema language”; it is a **lifecycle-and-policy calculus** with a stable IR, agent-safe mutation, and a text form that is actually pleasant to read and emit. The post-event simplification (`when stage` + relationships for correlation) is the most important design win in the DSL document.

**Highest-leverage next moves**

1. **Collapse event-centric IR** toward the stage-observation model the v0.3 examples already assume.  
2. **Freeze a brutal Phase 1 DSL** and implement parse → evolution → analyze → print before parallel / schedule / packages / OpenAPI.  
3. **Close one runtime loop** (action + effects + stage + reactive `when`) on the generic AST/VM path, even if thin.  
4. **Shrink the public mutation surface** relative to what one entity block in the DSL can express.  
5. Keep **grep** and **full-stack generation** as north stars, not as the next implementation PR.

---

## 5. Optional follow-up

A concrete Phase 1 **IR ↔ DSL mapping matrix** (what lowers to what; what to freeze or delete in `DomainChange` / analyzers) would be the natural next artifact if this feedback is accepted as direction.
