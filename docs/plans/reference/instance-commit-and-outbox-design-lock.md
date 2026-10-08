# Instance commit & outbox — design lock

**Date:** 2026-08-07  
**Status:** **Design lock — parked.** No product suite until explicit admit (typically when a host persists instance state beyond process memory).  
**Trust role:** Enables honest **durable** and **async external** claims — see [`customer-trust-proof-map.md`](customer-trust-proof-map.md) §3.4  
**Policy:** [`docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](../decisions/2026-07-11-platform-trust-bar-and-dogfood.md) · stage-as-observable ADR under `docs/decisions/` (no domain event bus)  
**Related:** absorption P9 `schedule at` (host) · [`mcp-mutation-safety.md`](mcp-mutation-safety.md) (authoring session, not instance durability) · CORE runtime (`DomainInstanceStore`, `DomainEntityInstance`) · index [`README.md`](README.md)

---

## 1. Purpose

Name the **real mutation storage boundaries** and where **outbox-style “go do this”** intents attach — so customers and hosts can trust durability claims without conflating:

- schema evolution,  
- in-memory effect cascades,  
- durable instance writes,  
- async external work.

**This is not an implementation plan.** Types below are vocabulary for a future suite.

---

## 2. Three mutation surfaces (do not conflate)

| Layer | Mutates | Mechanism today | Durable? | Outbox? |
|-------|---------|-----------------|----------|---------|
| **A. Schema / model** | `Domain` definition | `DomainChange` → `DomainEvolution.Apply` → new immutable root + analysis gate | Session / files / whatever host stores models | **No** (authoring). Optional “publish model version” is host versioning, not domain outbox. |
| **B. Instance / runtime** | Property bags, stage, links, soft-delete, creates | `DomainEntityInstance` + `DomainInstanceStore` + effect dispatch | **In-memory only** today | **Yes — primary** once instances persist |
| **C. Projection / media** | Tables, columns, API shapes | Storage analysis, facets, exporters | Generated hosts | Outbox protocol lives in **host persistence**, not in codegen |

`EvolutionTransaction` was removed on purpose (immutable propose-or-discard). That is **not** the instance unit-of-work model.

**Session mutation safety** (MCP concurrent evolve) is layer **A + session store**, documented in [`mcp-mutation-safety.md`](mcp-mutation-safety.md). It is a **different** problem from instance commit/outbox.

---

## 3. Domain run vs host commit

### 3.1 Domain run (sync, no I/O)

A **domain run** is one host-initiated unit of domain work, typically:

- invoke an action (or apply a structured command that maps to effects),  
- including **synchronous** subscription fan-out (`NotifyTransition`, depth-capped).

During the run, only the **in-memory instance graph** mutates. No sockets, no EF `SaveChanges`, no email.

**Outputs (logical):**

```text
DomainRunResult
  ├── InstanceDelta     // creates, assigns, stage, links, deletes, soft-delete
  └── OutboxIntents[]   // declared external work; empty for pure domain cascades
```

DomainModeling owns the **meaning** of these structures. Host owns serialization and storage.

### 3.2 Host commit boundary (the storage boundary)

```text
1. COMMAND     host receives intent (API, MCP, worker)
2. DOMAIN RUN  policies + effects + sync subscriptions → DomainRunResult
3. COMMIT ★    one host unit of work:
                 • apply InstanceDelta to durable store
                 • insert OutboxIntents
               success ⇒ domain fact is durable
4. DRAIN       async worker: process outbox → mark done / dead-letter
```

**Rules:**

1. **One commit per successful domain run** (or explicit multi-run host batch — host product decision, not domain).  
2. **Do not commit per effect** (assign/create/transition).  
3. **Do not perform external I/O inside effect dispatch.**  
4. **Schema apply (A) and instance commit (B) stay separate** unless a specific host product versions them together deliberately.  
5. **Fail before commit:** policy failure, analysis/runtime throw, depth limit — no partial durable write of that run.

### 3.3 What is *not* a commit boundary

| Not a boundary | Why |
|----------------|-----|
| Each `AssignEffect` | Mid-run; not customer-atomic |
| Each subscription handler | Part of same domain run |
| `DomainEvolution.Apply` | Schema layer; different aggregate |
| MCP tool return | Adapter; durability is host’s job after run |
| Grammar parse | Authoring only |

---

## 4. InstanceDelta (vocabulary)

Logical facts about what the domain run changed. Exact IR TBD at implement; must be:

- **Replayable** onto a store that understands entity ids + relationship names,  
- **Independent of** EF/SQL (host maps delta → rows),  
- **Complete enough** that a crash after commit does not need to re-run non-deterministic domain logic to recover instance state.

Minimum conceptual contents:

| Kind | Examples |
|------|----------|
| Upsert instance | New entity instance + initial properties / stage |
| Property set | Scalar (and known structured) assigns |
| Stage set | Current stage after transition |
| Link / unlink | Relationship edges |
| Soft-delete / remove | Delete effect semantics as shipped |

**Non-goals for v1 delta:** full temporal event log, multi-hop derived views, projection cache invalidation (host may derive).

---

## 5. Outbox intents (vocabulary)

An **intent** is a durable record that **something outside the domain graph** should happen **after** the instance fact is committed.

### 5.1 What may emit intents (future)

| Source | Intent role |
|--------|-------------|
| Host projection of **stage transition** or **action completed** | Often enough — no new DSL |
| Authorable **host-fulfilled** forms (`schedule at`, notify) — P9-shaped | Domain records *what/when*; host drains |
| Integration modules (email, webhook) registered as intent kinds | Product external contracts (trust path B) |

### 5.2 What must not become “outbox”

| Domain behavior | Stays |
|-----------------|-------|
| `when` subscription effects (assign, create, invoke, transition) | **Sync** domain cascade |
| Policy evaluation | Sync inside run |
| Stage transition as observable | Domain fact in delta; not an “event bus” product surface |

Reintroducing a general domain **event/publish** surface for outbox is **rejected** (conflicts with stage-as-observable ADR). Host may *observe* transitions when building intents.

### 5.3 Intent shape (logical)

```text
OutboxIntent
  Id              // stable for idempotent drain
  Kind            // e.g. ScheduleFire, HttpNotify, CustomModule
  Payload         // structured; host/module schema
  CausedBy        // action name / transition / command id
  EntityRef       // instance id + type if applicable
  NotBefore       // optional (schedule)
  CreatedAt
```

**Delivery claims (host product, must be honest):**

| Claim | Requirement |
|-------|-------------|
| At-least-once drain | Idempotent handlers or dedupe keys |
| Exactly-once *effect* | Stronger; only claim with proof |
| Ordered per entity | Optional host policy; not domain default |

Domain does **not** claim broker semantics. Host docs must.

---

## 6. Subscriptions vs outbox

```text
Peer → Overdue
   │
   ├─► Sync: subscriber when-handler (create Fine, assign, …)   ← Domain run / delta
   │
   └─► Optional host: intent "notify finance" after commit      ← Outbox
```

| | Sync subscription | Outbox intent |
|--|-------------------|---------------|
| Latency | Immediate in-run | After commit + drain |
| Failure | Fails the domain run | Retries / dead-letter; domain fact already true |
| Authoring | `when` in DSL | Host policy or P9-style intent form |
| Trust story | “Domain cascade completed” | “External work after durable fact” |

---

## 7. Injection points (locked)

| Location | Verdict |
|----------|---------|
| Effect lowering / VM | **No** — no I/O |
| Ad hoc inside each effect handler | **No** — invisible channels |
| Domain `publish` / `event` effect | **No** — ADR conflict |
| **After domain run, before host commit** | **Yes — primary** |
| Host maps transition/action → intent rows | **Yes** |
| Authorable schedule/notify intent (later) | **Yes — host-fulfilled** |
| MCP session lock | **Authoring only** — not instance outbox |

---

## 8. Placement (modules)

| Concern | Module |
|---------|--------|
| Effect meaning, InstanceDelta production (when implemented) | `Poly/DomainModeling` (Runtime) |
| OutboxIntent as pure data contract | DomainModeling or small shared contracts assembly — **no** I/O |
| DB transaction, outbox table, drain worker | **Host** (generated app, MCP host, customer host) |
| SQL facet / storage packs | Shape of instance tables — **not** outbox protocol owner |
| Schedule wall-clock | Host (P9); temporal pack is expression/IR only |

CORE line: DomainModeling does not own domain-specific VM opcodes; similarly it does **not** own SMTP or message brokers.

---

## 9. Relationship to current code (2026-08-07)

| Asset | Today | After admit (sketch) |
|-------|--------|----------------------|
| `DomainInstanceStore` | In-memory instances + links + sync notify | Still the domain-run workspace; host may load/save around the run |
| `DomainEntityInstance` effect path | Mutates bag/stage/links immediately | Same during run; host persists `InstanceDelta` at commit |
| `NotifyTransition` | Sync, depth max 10 | Remains sync inside run |
| Evolution | Immutable domain root | Unchanged (layer A) |
| MCP session | In-memory domain authoring | Mutation safety separate; not instance outbox |

**Honest claim today:** runtime is **process-local and synchronous**. Durable/async claims require this design **implemented** on a named host.

---

## 10. Negative tests (document; author at suite admit)

- Domain run fails mid-cascade → **no** durable delta, **no** outbox rows.  
- Commit fails after run → no partial outbox without matching instance rows (single Tx).  
- Drain without commit → impossible (no rows).  
- Re-drain same intent → idempotent or safe duplicate per host claim.  
- Subscription assign does **not** create outbox rows unless host maps that transition.  
- Schema evolve does **not** write instance outbox.  
- Unknown intent kind → fail closed at register/drain (no silent drop if claimed supported).

---

## 11. Seeded simulator (optional follow-on)

Thin **TigerBeetle spirit**, not VOPR:

- Seed → random legal **domain runs** on in-memory store.  
- Invariants: link consistency, stage in entity stages, soft-delete visibility, depth limits, catalog required for dispatch.  
- Later: dual-apply InstanceDelta to a second store and compare.

**Out of scope:** network partitions, disk bitrot, multi-region.

See trust proof map §4 “Seeded invariant runner.”

---

## 12. Sequencing vs other work

```text
NOW:     in-memory runtime claims only; fix MCP mutation safety for T1 authoring
GI:      product DSL on Grammar (authoring trust) — independent of this lock
P1:      temporal pack expressions — not schedule drain
WHEN:    first host persists instances (EF/SQLite product path, etc.)
THEN:    admit implementation suite for DomainRunResult + host commit + outbox table
P9:      schedule at as intent kind on that seam
```

Do **not** block grammar integration or temporal expression work on outbox implementation.

---

## 13. Decision

1. **Instance durability has exactly one storage boundary:** host commit of `InstanceDelta` (+ optional `OutboxIntents`) after a successful domain run.  
2. **Outbox is host-drained intents**, not a domain event bus; subscriptions stay sync.  
3. **No I/O in DomainModeling effect dispatch.**  
4. **No product claim** of durable external effects until a host implements this seam and tests it.  
5. **Park implementation** until admit; this document is the vocabulary lock.

---

## 14. Success definition (when a suite is admitted)

- [ ] `DomainRunResult` (or equivalent) produced for product invoke path.  
- [ ] At least one host implements single-Tx apply(delta)+insert(intents).  
- [ ] Drain worker with explicit delivery claim + tests.  
- [ ] Negative tests in §10 green.  
- [ ] Trust proof map §3.4 rows move Yellow/Green.  
- [ ] CORE / DomainModeling README updated for placement.  
- [ ] Guide honesty: durable/async behavior documented only if shipped.

---

## 15. Non-goals

- Replacing stage-as-observable with Kafka-style domain events.  
- Exactly-once across arbitrary third parties without host design.  
- Unifying schema evolution and instance commit into one always-on transaction.  
- Building outbox inside `Poly.Grammar` or MCP session store.  
- Full distributed simulation as a gate.
