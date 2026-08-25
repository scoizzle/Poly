# ADR: Platform Trust Bar and Dogfood Gates

**Date:** 2026-07-11  
**Updated:** 2026-07-11 — first-customer strategy; external contracts; **customer product generation funds neurosymbolic work**  
**Status:** Accepted  
**Deciders:** Primary author  

**Related:**

- [`AGENTS.md`](../../AGENTS.md) — core principles (especially §1 domain artifact, §2 end-to-end ownership, §3 customer value, §5 shipped capability)
- [`docs/CORE.md`](../CORE.md) — pipeline and ownership (mechanisms)
- [`docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`](2026-05-31-neurosymbolic-platform-vision.md) — historical vision
- [`docs/decisions/2026-06-08-vm-as-canonical-semantics.md`](2026-06-08-vm-as-canonical-semantics.md) — VM as ground truth
- [`docs/decisions/2026-06-08-domain-lowering-boundary.md`](2026-06-08-domain-lowering-boundary.md) — domain → generic ops only
- [`docs/decisions/2026-core-engineering-principles.md`](2026-core-engineering-principles.md) — principles ADR
- [`docs/plans/2026-07-11-review-fix-plan.md`](../plans/2026-07-11-review-fix-plan.md) — current hardening (trust **layer 1**)

---

## Context

Poly is sold (and used) as a **neurosymbolic platform**: domain models are the key artifact; they lower to a symbolic AST; analysis and the VM give operational meaning; thin adapters (today MCP, later other modules) expose APIs so agents and systems can interact with a generated domain.

Two pressures conflict if left implicit:

1. **Correctness ambition** — In the limit, dogfooding should mean not only domain → executable behavior, but **extensions/plugins/modules that build interaction APIs** from the domain, so the platform could produce a **product-equivalent** version of its own surface.
2. **Ship discipline** — Full self-reproduction of every host line (`DirectVmAbiEmitter`, analysis framework, Introspection providers) is a never-finish bar and violates **shipped capability over completeness**.

Without a written trust doctrine, agents and humans either:

- treat “self-host everything” as a blocker to any customer conversation, or  
- ship a platform pitch while the product surface is still only hand-maintained host code that customers cannot get via the same path, or  
- build the “real” customer product as a permanent parallel stack and only dogfood as theater.

This ADR locks the **customer-trust bar**, the **first-customer strategy**, the **meaning of dogfood**, and **staged gates** so execution plans stay honest.

---

## Decision

### 1. Trust doctrine (one sentence)

**We will not ask the market to trust Poly as a platform until we trust it with our own domain product surface — same model class, same pipeline, same class of interaction APIs we give customers.**

That is **behavioral product equivalence**, not reimplementation of the host runtime as domain scripts.

### 1b. First-customer strategy (how the product is built)

**We are our own first customer.**

The **real customer-facing platform surface** is intended to be built **through** the platform path (domain model + execution spine + interaction modules that expose APIs), not indefinitely as a separate, hand-only product that customers never get.

| Role | What it is |
|------|------------|
| **Substrate (host)** | VM, analysis framework, Introspection providers, module host, generators — owned in C# / core `Poly`. Not “the product pitch.” |
| **Product** | Domains + lowered/executable behavior + **modules that build the APIs** agents and systems use to interact with those domains |
| **First customer** | Us — we define, evolve, and operate Poly’s own product domain(s) using that product path so pain points hit us first |

**Why this is strategy, not vanity:**

1. **Pain is discovery.** Gaps in evolution honesty, lowering, policy/effects, tool generation, and operability show up while *we* depend on them — before an external customer’s domain is the first real stress.
2. **No dual product.** A permanent “customer stack” built only in host one-offs while dogfood stays a side demo re-creates the V2-style drift problem. Hand-written MCP/tools are a **bootstrap**, not the forever shape of the sold surface.
3. **Customer path = our path.** When we add a capability we intend to sell, the preferred move is: express it on the domain / module path (or extend substrate only so that path can express it) — not a private shortcut we never productize.

**Bootstrap is allowed; forever dual-track is not.**

- **Early (toward T1):** Hand-written direct API + MCP to prove the spine. That *is* substrate + thin adapter — correct.
- **Toward T2/T3:** Customer-facing surface migrates to **domain-defined product + module-built APIs**. New sold surface prefers that path. Host-only shortcuts for “just us” require an explicit plan to productize or delete.

**Pain-handling rule:** When dogfood hurts, fix the **platform seam** (lower / analyze / replace / evolve / module generation) or honestly narrow the sold claim — do not paper over with a host-only parallel feature and call the platform done.

### 1c. External contract surface (why this hurts first)

Being our own first customer means the **external system contract surface** — how domains expose and consume APIs (MCP tools, HTTP/RPC façades, event/webhook modules, auth boundaries, third-party service adapters that are *part of the product path*) — is the surface that **hits us hardest and earliest**.

In practice:

```text
We need third-party services to build *our* service
        ↑
We must interact with the outside world through contracts
        ↑
Those contracts are (target) module-built APIs over domains
        ↑
That path must be honest enough that *we* can use it
        ↑
Trust layer 1 + thin vertical slices (this is why order matters)
```

So yes: **the contract surface is the gauntlet.** We feel tool honesty, evolution fail-loud, session/revision, generated vs hand schemas, and “can an agent actually operate this?” before we get a smooth loop of “wire Stripe/Auth0/whatever and ship.” That is intentional discovery, not accidental masochism — *if* we keep substrate vs product straight.

**Two kinds of “talking to the outside” (do not conflate):**

| Kind | Role | Policy |
|------|------|--------|
| **A. Substrate / ops glue** | Host needs to run at all: process hosting, logging, secrets, CI, local DB, raw SDK calls that never appear as the *sold* domain product API | Allowed as ordinary host code. Not dogfood theater. Do not pretend it is “domain-driven.” |
| **B. Product external contracts** | How *a domain* (including ours) exposes or consumes services as part of the **platform product story** — tools, integration modules, customer-visible I/O | Prefer domain + interaction modules. Bootstrap hand adapters OK with **convergence intent**. Pain here → fix seams or narrow claim. |

**What this does *not* mean:**

- Block all third-party SDKs until T3.  
- Route every `HttpClient` call through DomainExpression.  
- Delay scaffolding *our* service’s non-product infrastructure until the meta-domain is complete.

**What it *does* mean:**

- Anything we intend customers (or agents acting as customers) to treat as **“how you connect a domain to the world”** must eventually live on path **B**, and **we use B first** so the contract surface fails on us.  
- Until B is good enough for *us*, we do not claim the platform is ready for third parties to hang production domains on those contracts.  
- When B blocks us, the backlog priority is the **contract/module/domain seam**, not a forever private integration that customers can never get.

### 1d. What funds the neurosymbolic work

**Customer product generation is the engine that funds neurosymbolic platform work over time.**

The long-horizon research/platform ambition (symbolic AST, analysis, VM-canonical semantics, domain fidelity, multi-host introspection, deeper effect/policy execution) is real and non-negotiable as *direction*. It is **not** funded by completing the platform in isolation first.

| Concept | Meaning |
|---------|---------|
| **Customer product generation** | Turning domains into operable products: structure, policies/effects as shipped, and **interaction modules** that produce the APIs/tools/contracts customers and agents use |
| **Neurosymbolic work** | Substrate and pipeline depth: Interpretation, VM, analysis, lowering, Introspection, correctness, expressiveness |
| **Funding relationship** | Shipped, trusted **product generation** (including our own first-customer products) creates the value, revenue, and prioritization pressure that **pay for** ongoing neurosymbolic investment |

```text
Domain + modules → customer-facing products (incl. our own)
        → trust, usage, revenue, clear pain
        → funds and steers neurosymbolic substrate work
        → better generation / execution / contracts
        → loop
```

**Implications for prioritization:**

1. **Product generation is not a demo bolted on the side of “real” platform work.** It *is* how the platform becomes a business and how the research stays aimed at load-bearing seams.
2. **Neurosymbolic depth is justified by product generation need** (or trust layer 1 honesty), not by completeness catalogs. Prefer substrate work that unblocks generating/operating real products over elegant depth with no product consumer (§3, §5, §6).
3. **We do not starve generation to perfect the VM**, and we do not ship lying generation to “fund” work on a broken spine. Trust stack order still holds: honesty first, then generation we depend on, then deeper substrate funded by that loop.
4. **Roadmaps and agent tasks** should be able to answer: *how does this help generate or operate a customer (or first-customer) product?* If the only answer is “platform purity,” park it unless it is trust layer 1.

**Anti-patterns:**

- Infinite platform runway with no product-generation loop  
- Product hacks that never rejoin domain + modules (no funding of the *right* work — only of dual-track debt)  
- Treating revenue features and neurosymbolic work as opposing tribes; they are **one loop** with different altitudes  

### 2. What dogfood means for Poly

Dogfood is **product self-hosting**, three layers:

| Layer | Job | Dogfood form |
|-------|-----|----------------|
| **Domain** | What the world is and may do | A domain whose subject is Poly’s **product** concepts (entities, policies, stages, evolution affordances — not every C# type) |
| **Execution** | What behavior means | Same spine: lower → analyze → replace → **VM-canonical** execute; no self-special opcodes |
| **Interaction modules** | How the outside talks to the domain | Bootstrap hand-written (MCP); **target** derived/generated tools, façades, and adapters from the domain — this is how the real customer surface is built |

**In scope for “equivalent version of itself”:** the **sold product surface** (author domain, evolve, query, analyze, evaluate policies/effects as shipped, operate via the same class of APIs/tools) — including that **we ship that surface to ourselves first** via the same construction path.

**Out of scope for equivalence:**

- Byte-identical replacement of `Poly.dll` or the CLR host  
- Expressing `DirectVmAbiEmitter`, the analysis framework, or Introspection providers as DomainExpressions  
- A single mega-plugin that completes every future host  

The host (VM, type providers, module host) remains the **substrate**. The domain + modules remain the **product** — and are how the **customer-facing** product is meant to be realized.

### 3. Trust stack (order is mandatory)

Dogfood sits on top of ground truth. Higher layers do not substitute for lower ones.

```text
4. Dogfood — our domain product surface rides customer path     ← market platform trust (T2+)
3. Interaction modules honest; increasingly domain-derived
2. Vertical slices — evolve + policy/effects + real tools
1. Ground truth — VM semantics, dual-oracle where dual paths exist,
                  fail-loud evolution, fail-closed unshipped ops
```

**Layer 1 is non-negotiable.** Dogfood on a lying spine multiplies confidence in a lie. Current execution for layer 1: [`docs/plans/2026-07-11-review-fix-plan.md`](../plans/2026-07-11-review-fix-plan.md).

### 4. Staged gates (T1 / T2 / T3)

#### T1 — Design partner (careful external use)

Allowed to work with a design partner when **all** hold:

- Evolution is **fail-loud** on missing/invalid targets (domain API, not only MCP fingerprint).
- Unshipped VM node shapes **fail closed** (no silent identity / wrong values presented as success).
- Policy evaluation (and any other claimed runtime path) is **VM-primary** with tests; dual-oracle where LINQ remains a reference.
- A coherent vertical slice exists: bootstrap → evolve structure → query → policy (and later one effect) on the **direct API**, with MCP as a thin honest adapter.
- Tool/API descriptions match behavior (no “assigns existing action” when creating empty stage-local actions).

Hand-written modules are acceptable at T1. Full self-domain is **not** required.

**Not claimed at T1:** “Trust us as a general platform product.” Claim: “Use this path under joint scrutiny; spine does not silently lie on the shipped slice.”

#### T2 — Platform product (market trust bar)

**This is the bar for asking a customer to trust Poly as a platform.**

Required:

- A **Poly product domain** (working name: `DomainKernel` / PolyMeta — name free) models the **customer-visible** modeling concepts we sell (not the full host).
- A **non-trivial fraction** of the interaction surface used day-to-day for that product (at minimum: session/bootstrap, evolve ops we document, overview/query, analysis, policy inspect/eval as shipped) is **driven or generated from that domain** (or forced equal by tests), not only a parallel hand-maintained universe that drifts.
- **We are the first customer of that surface** — internal domain product work runs on it enough that a failure mode that would hurt a customer hurts us first.
- The **direction of travel** is clear: new customer-facing capabilities are added on this path, not only as permanent host-only tools.
- Divergence between model and tools is **tested** (schema / affordance / tool honesty).
- Layers 1–2 of the trust stack remain green.

**Claim at T2:** “The way you work with a domain is the way we work with ours — same pipeline, same class of APIs. We built (and use) the product surface that way on purpose.”

#### T3 — Strong dogfood (maturity, not a ship blocker for T2)

- Most customer-facing domain **product** behavior and APIs are domain + module-derived — the **default** way the product is extended.
- Host code is clearly substrate (runtime, providers, module host, generators).
- Expanding a customer capability means evolving the product domain and regenerating/updating modules, not forking host one-offs that never rejoin the path.
- Pain found in self-use has been closed at the platform seam or reflected in narrowed claims.

T3 is **maturity**, not a prerequisite to every sale after T2.

### 5. Module / plugin policy (so the future stays open)

1. **Modules are adapters and generators**, not a second domain language or second evaluator.
2. Modules call **DomainEvolution**, **queries**, **PolicyEvaluator** / **Interpreter** — they do not mutate domain graphs in place or special-case the VM ABI for one feature ([`docs/CORE.md`](../CORE.md)).
3. **No premature plugin framework.** Extract a shared module host only when a **second** real generator/consumer forces it (AGENTS §6).
4. First module remains **MCP** (or its successor); additional hosts (HTTP, typed library façades, etc.) appear with named consumers.
5. Affordances and tool schemas should trend toward **derivation from the domain** so honesty is structural, not editorial.

### 6. What we will not do

- Delay all external learning until T3 complete.
- Call a demo domain “dogfood” if we do not operate our product surface on the same path.
- Build the **sold** customer surface indefinitely as a host-only parallel product while dogfood remains optional theater.
- Add a host-only “just for us / just for demos” capability that we intend customers to have, without a path to the domain + module product path.
- Paper over dogfood pain with a private shortcut instead of fixing the seam or narrowing the claim.
- Conflate **substrate ops glue** with **product external contracts** — either by forcing every SDK through the domain, or by shipping customer integration story only as private host glue.
- Claim “integrations / agent tools / external APIs are ready” while *we* cannot stand up our own service interactions on path B.
- Treat neurosymbolic platform work as self-funding or as a prerequisite empire before any product-generation loop exists.
- Fund the business only with host-only product hacks that never strengthen domain + modules (that funds dual-track debt, not the neurosymbolic path).
- Warp the domain model to express compiler internals so we can claim self-hosting (§1 fidelity).
- Invent domain-specific VM opcodes to make self-hosting easier ([domain-lowering boundary](2026-06-08-domain-lowering-boundary.md)).
- Treat dual-oracle green on arithmetic alone as platform trust.

---

## Rationale

- **Platform vs library.** A library can be trusted via tests. A platform whose pitch is “the domain is the system” must show that **its own product surface** is subject to that claim.
- **We are the first customer.** Building the real product *through* domain + modules makes pain operational, not speculative — the highest-leverage way to harden a neurosymbolic platform before external domains depend on it.
- **External contracts first.** The API/tool/integration surface is where agents and services actually couple; if that surface is wrong, third-party build-out of *our* service and customers’ services both fail. Feeling that before scale is the point.
- **Product generation funds the science.** Neurosymbolic depth is the long game; **customer product generation** (domain → operable product + contracts) is how that game is resourced and steered over time. Without the loop, platform work is unpaid ambition.
- **Product equivalence is the honest grail.** Customers care that APIs, rules, and evolution match the model — not that the emitter is a DomainExpression.
- **Bootstrap ≠ dual-track.** Hand-written adapters prove the spine; they must not become a permanent second product that never converges.
- **Staged gates prevent never-ship.** T1 allows design partners under honesty constraints; T2 is the market platform bar; T3 is depth of derivation.
- **Aligns with AGENTS.** Domain key artifact (§1); end-to-end ownership (§2); customer value — *we* are the first customer (§3); go well to go fast via felt pain (§4); shipped slices on the real path (§5); abstract late for plugin hosts (§6); this ADR is a guardrail with a clear consumer — everyone who would claim “customers can trust the platform” (§7).

---

## Consequences

### Immediate

- Execution priority: **trust layer 1** ([review fix plan](../plans/2026-07-11-review-fix-plan.md)) before large self-hosting or plugin-framework work.
- Product/marketing language: do not claim platform-level customer trust before **T2**.
- Design partners may proceed at **T1** with explicit scope.
- When prioritizing features: prefer work that unblocks **us as customer** of the domain + module path over host-only polish that never joins that path.
- When prioritizing neurosymbolic depth: prefer work that **unblocks or hardens product generation** (or trust layer 1) over depth with no generation consumer.

### Near-term planning

- After hardening: thin work toward **derived interaction** (e.g. generate a subset of MCP/evolve tools from domain metadata) and a small **product domain** — steps toward T2, not a reflective VM. That work is **product construction** (the funding engine), not a side demo.
- Effect execution (one thin slice) remains on the path to a believable product surface (structure-only dogfood is incomplete).
- Inventory hand-written MCP/direct-API surface: mark each piece **substrate bootstrap** vs **must converge to domain/module-derived** for T2.

### Docs and agents

- This ADR is the **policy** for trust, dogfood, and **how the customer-facing product is built**. Plans implement gates; CORE remains mechanisms only.
- Agents must not open “full Poly self-hosting” epics as a substitute for fail-loud / fail-closed / VM honesty work.
- Agents must not add permanent customer-facing host-only tools without noting the bootstrap exception and convergence intent.

### Success metrics (qualitative)

| Gate | Evidence |
|------|----------|
| T1 | Fix-plan P0/P1 honesty items closed; partner slice documented; tools match behavior |
| T2 | Poly product domain exists; ≥ documented fraction of daily product interaction path domain-driven/generated; honesty tests; internal use |
| T3 | Expanding sold capability is mostly domain evolve + regenerate modules |

---

## Non-goals (explicit)

- Multi-host Introspection completeness as a trust prerequisite (CLR-first host is fine through T2 if product path is honest).
- JIT, perf campaigns, or Syntax module split as trust gates.
- Replacing human judgment in security/sandbox policy with domain-only rules without a separate review.

---

## Review trigger

Revisit this ADR if:

- A real customer requires a different trust evidence shape (e.g. formal methods) — amend gates, do not silently lower honesty.
- Module generation becomes the primary product — promote module-host rules into CORE mechanisms.
- T2 criteria prove too vague in practice — tighten “non-trivial fraction” with a numbered checklist in a plan, leave doctrine here.
