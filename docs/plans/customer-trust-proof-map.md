# Customer trust — proof map

**Date:** 2026-08-07  
**Status:** Design lock / living index — **not** an implementation suite  
**Policy:** [`docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](../decisions/2026-07-11-platform-trust-bar-and-dogfood.md)  
**Related:** [`instance-commit-and-outbox-design-lock.md`](instance-commit-and-outbox-design-lock.md) · [`mcp-mutation-safety.md`](mcp-mutation-safety.md) · grammar (archived [`archive/completed-2026-08-mid/grammar-integration.md`](archive/completed-2026-08-mid/grammar-integration.md)) · pre-ship gate [`v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md) · index [`README.md`](README.md)

---

## 1. Purpose

Make **safety and trust auditable**: every customer-facing claim maps to a **proof instrument**, a **gate (T1/T2/T3)**, and (where it exists) a **suite, test class, or doc**. Gaps are explicit — not silent optimism.

**One sentence for customers (and us):**

> We only claim what we can fail closed on, dual-check where two paths exist, and run ourselves on the same path we sell — and we name the durability boundary before we claim durable external effects.

This document does **not** admit work. It steers prioritization and honesty of claims.

---

## 2. Trust stack (mandatory order)

```text
4. Dogfood — our product surface on the customer path     T2+ market story
3. Interaction honesty — tools/APIs match behavior
2. Vertical slices — evolve + policy/effects + real tools
1. Ground truth — fail-closed, dual-oracle, no silent success
```

Higher layers do **not** substitute for lower ones. Dogfood on a lying spine multiplies confidence in a lie.

| Layer | What “proof” looks like |
|-------|-------------------------|
| **1** | Dual-run / dual-oracle tests; fail-closed diagnostics; review gate; no vacuous success |
| **2** | Named vertical suite green end-to-end (bootstrap → evolve → invoke → assert) |
| **3** | Guide + tool schemas = behavior; mutation/session safety; revision honesty |
| **4** | We operate day-to-day on domain + modules we sell; pain → seam fix or narrowed claim |

---

## 3. Claim → proof matrix

Status legend: **Green** = evidence in tree · **Yellow** = partial / known gap · **Red** = claim not yet defensible · **N/A** = not claimed

### 3.1 Ground truth (layer 1)

| Claim | Gate | Proof instrument | Where (pointer) | Status |
|-------|------|------------------|-----------------|--------|
| Evolution fails loud on invalid targets | T1 | Domain API tests + analysis gate | `DomainEvolution` / evolution tests; not MCP-only | **Green** (core); keep regression |
| Unshipped / invalid ops fail closed | T1 | Runtime + analyze diagnostics; three-layer defense | AGENTS pre-ship gate; effect/policy analyzers | **Green** culture; residual per review plan |
| Dual path ⇒ dual agreement | T1 | Dual-oracle / dual-run | Policy VM-primary + LINQ reference where dual; GI dual-path when live; C99 GIP `DualRun_*` | **Yellow** (partial surfaces green; not universal) |
| Grammar structure dispatch matches RD handlers | T1→GI | Dual-run execute equality | `C99ParserInterpreterTests` DualRun_*; product GI dual-path planned | **Green** preflight; product cutover still dual until GI-7 |
| Product DSL parse/print does not invent syntax | T1 | Round-trip + guide honesty | `PolyDslRoundTripTests`, annotation goldens, `poly-dsl-guide.md` | **Green** corpus; GI must keep corpus green |
| Empty sets / missing matches do not vacuous-succeed | T1 | Fail-closed tests | Subscription/policy/effect analyzer tests | **Green** rule; re-check each suite |

### 3.2 Vertical slices (layer 2)

| Claim | Gate | Proof instrument | Where (pointer) | Status |
|-------|------|------------------|-----------------|--------|
| Author → evolve → query works | T1 | Vertical suite | Dogfood waves; MCP apply_dsl smokes | **Green** shipped slices |
| Policies evaluate on VM path as claimed | T1 | Policy tests + VM-primary | `PolicyEvaluator` / domain expression VM tests | **Yellow** — keep dual-oracle where LINQ remains |
| Effects run (assign, create, invoke, transition) | T1 | Runtime goldens | Action/effect integration tests; p3 returns; p4 subscriptions | **Green** for shipped kernel |
| Multi-hop path-prefix / quantifiers | T1 | Golden + analysis fail-closed | p2 suite tests | **Green** (suite done) |
| Entity returns on create/create-in | T1 | Analysis + runtime goldens | p3 suite | **Green** (suite done) |
| Temporal authoring (`Now`, `N days`) | T2 product | Pack + goldens post-GI | Parked: p1 design lock | **Red** until admit + GI |
| Wall-clock `schedule at` | — | Host only (P9) | Absorption P9 | **N/A** — not domain claim |

### 3.3 Interaction honesty (layer 3)

| Claim | Gate | Proof instrument | Where (pointer) | Status |
|-------|------|------------------|-----------------|--------|
| Tool descriptions match behavior | T1 | Guide smoke + tool tests | `GetDslGuide_ReturnsProductSurface`; MCP tool honesty | **Yellow** — enforce on every tool change |
| Session mutations do not silently lose concurrent writes | T1 | Revision / lock tests | [`mcp-mutation-safety.md`](mcp-mutation-safety.md) | **Red** — known race; fix before multi-agent design partners |
| Rollback / conflict is diagnosable | T1 | Payload tests | Same plan | **Red** / proposal |
| MCP does not claim unshipped domain capability | T1 | Honesty review | CORE: MCP must not claim core lacks | **Yellow** — process + tests |
| MCP expressions are product DSL only (no JSON IR bags) | T1 | Grep + fragment tests | `mcp-minify` suite: zero `DomainExpressionJsonParser`; `DslExpressionFragmentTests` | **Green** (mcp-minify 2026-08-08) |

### 3.4 Durability & external effects (layer 1–2 for operated products)

| Claim | Gate | Proof instrument | Where (pointer) | Status |
|-------|------|------------------|-----------------|--------|
| In-memory runtime semantics are well-defined | T1 | Store + instance tests | `DomainInstanceStore`, `DomainEntityInstance` | **Green** — claim only in-memory |
| Durable instance writes are atomic w.r.t. domain run | T2 durable | Host unit-of-work tests | [`instance-commit-and-outbox-design-lock.md`](instance-commit-and-outbox-design-lock.md) | **Red** — design only; no product claim |
| External “go do X” runs only after domain fact is durable | T2 durable | Outbox + drain tests | Same design lock | **Red** — design only |
| Subscriptions are sync domain cascade (not email) | T1 | Docs + tests | CORE / store NotifyTransition | **Green** — honesty of scope |
| Stage transition is the domain observable | T1 | ADR + no event surface | `2026-07-17-stage-transition-as-observable` | **Green** |

### 3.5 Dogfood / first customer (layer 4)

| Claim | Gate | Proof instrument | Where (pointer) | Status |
|-------|------|------------------|-----------------|--------|
| We use the same class of APIs we sell | T2 | Operated product domain + modules | Trust ADR §1b, §4 T2 | **Yellow** — bootstrap MCP OK; convergence intent required |
| Pain → seam fix or narrowed claim | T2 | Process (not a test) | Trust ADR pain-handling rule | **Yellow** — cultural |
| Product generation funds neurosymbolic depth | — | Roadmap discipline | Trust ADR §1d | Process |

---

## 4. Proof instruments (catalog)

| Instrument | What it proves | Customer translation |
|------------|----------------|----------------------|
| **Fail-closed tests** | Invalid configs do not succeed empty | “It won’t pretend it worked.” |
| **Dual-run / dual-oracle** | Two implementations agree on a corpus | “Wherever we claim two ways, they match.” |
| **Round-trip / golden corpus** | Parse ↔ print ↔ evolve stable | “What you write is what you get.” |
| **Vertical suite** | One real path green end-to-end | “You can run this story yourself.” |
| **Three-layer defense** | Parse / analyze / runtime all reject bad input | “Defense in depth, not one filter.” |
| **Tool/guide honesty tests** | Surface text = behavior | “Agents won’t be lied to.” |
| **Session revision / lock tests** | No silent lost update | “Parallel agents won’t erase each other.” |
| **Instance commit + outbox tests** | Durability + async intent | “External work is after the fact is saved.” |
| **Seeded invariant runner** (future) | Random legal sequences preserve invariants | “Stress without demo cherry-picking.” |
| **Pre-ship review gate** | Process bar before Done | “We don’t merge unreviewed trust debt.” |

**Explicit non-instruments (alone):** marketing slides, TB/VOPR full network sim, “engine proven” via JSON-only tests, dogfood demos without layer 1 green.

---

## 5. Audience scripts

### Design partner (T1)

**Claim:** “Use this path under joint scrutiny; the shipped slice does not silently lie.”

**Show:**

1. One vertical domain story (apply DSL → evolve → policy/effect).  
2. Fail-closed example (bad target / unshipped form).  
3. Dual-run or dual-oracle on that slice if dual paths exist.  
4. Guide snippet matching the tool.

**Do not claim:** general platform durability, schedule, full self-host, concurrent multi-agent session safety (until mutation safety is green).

### Platform buyer (T2)

**Claim:** “The way you work with a domain is the way we work with ours.”

**Show:**

1. T1 bar still green.  
2. First-customer product surface on domain + modules (or tested equality).  
3. Named durability story if state leaves memory (commit + intents design implemented for that host).  
4. Divergence between model and tools is tested.

### Engineering due diligence

**Show this map:** claim → instrument → test/doc → gap status.  
Walk **Red** rows honestly. Prefer dual-run and fail-closed over architecture diagrams.

---

## 6. Priority backlog driven by trust gaps

Ordered by **trust ROI** (not completeness):

| Priority | Gap | Outcome |
|----------|-----|---------|
| **P0** | MCP session lost-update / mutation safety | T1 multi-agent authoring trustworthy |
| **P0** | Keep dual-path green (GI dual-run corpus; any LINQ/VM dual) | Layer 1 does not regress |
| **P1** | Tool/guide honesty automation on change | Layer 3 drift fails CI |
| **P1** | Instance commit + outbox design → implement only when host persists | Honest durable claims |
| **P2** | Seeded domain-run invariant simulator | Eng proof under load |
| **P2** | T2 dogfood surface fraction measured | Market platform story |
| **Park** | Full distributed fault simulator | No customer claim depends on it yet |

Implementation of any row requires **explicit admit** on the master-roadmap — this map does not open parallel CURRENT work.

---

## 7. Maintenance rules

1. **New customer-facing claim** → add a row here in the same change (or refuse the claim).  
2. **New dual product path** → dual-run or dual-oracle row, or delete one path.  
3. **Narrowed claim** → update Status and customer script; do not leave Red as Green.  
4. **GI cutover / temporal pack / durable host** → refresh §3.1–3.4 status in the same PR family.  
5. Link suites by **stable name**; do not hardcode ephemeral test counts.

---

## 8. Success definition (for this doc)

- [x] Claims for T1/T2 durability and interaction honesty are listed with status.  
- [x] Red gaps (mutation safety, durable commit) are explicit.  
- [x] Dual-run / fail-closed named as primary layer-1 instruments.  
- [ ] Living: status columns updated when suites land or claims narrow.  
- [ ] Linked from master-roadmap / workstream map when useful for agents.

---

## 9. Decision

**Use this map as the trust audit surface.** Product and research work that cannot answer “which claim and which proof?” is either substrate (no customer claim) or out of scope until a row exists.
