# Effect Surface Completeness

**Date:** 2026-07-18  
**Status:** Active backlog — **after** Phase 3 thin + RT + SA MVP; **before** L\* host codegen / containers  
**Current pick:** Inventory-driven — close **authoring gaps** for effects that already **execute**  
**Related:**  
- [`mcp-phase3-oracle-surface.md`](mcp-phase3-oracle-surface.md) §6c RT · §6e SA  
- [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0  
- [`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)  
- Product DSL: [`Poly.Mcp/Docs/poly-dsl-agent-guide.md`](../../../Poly.Mcp/Docs/poly-dsl-agent-guide.md)  

**Principle:** Usefulness = **executable × authorable × honest**. Prefer finishing the path for effects that already run over inventing many new effect kinds. No domain VM opcodes for host I/O (email/HTTP) — host adapters later.

---

## 1. Why this plan

Phase 2–4 closed structure, spawn-and-wire, MCP runtime exercise, and stage-action footguns. Dogfood domains (Order/Customer-style) work with a **small** effect set.

Product doubt remains: **have we modeled enough effects to be useful?**  

Honest answer from inventory:

- **IR + CallAction** can do more than agents can write.  
- **DSL product path** only authors a subset.  
- **MCP** has almost **no** effect-add micro-tools (effects via `apply_dsl` or C# evolution).

This plan tracks a **parity matrix** and a thin vertical to make the **lifecycle effect language** useful without a completeness catalog.

---

## 2. Parity matrix (source of truth)

Legend: **✅** product-ready · **🟡** partial · **❌** missing · **🚫** non-goal (this plan)

| Effect (IR) | Runtime (`CallAction`) | DSL parse/print | Evolution builder | MCP micro-tool | Notes |
|-------------|------------------------|-----------------|-------------------|----------------|-------|
| **StageTransition** | ✅ direct | ✅ `transition to S` | ✅ `AddStageTransitionEffect` | ❌ | Core lifecycle |
| **Assign** | ✅ VM | ✅ `assign P to expr` | ✅ `AddEffectToAction` | ❌ | Core data change |
| **CreateEntity** | ✅ direct | ✅ `create T { }` | ✅ helpers | ❌ | Optional `RelationshipName` auto-link |
| **CreateInRelationship** | ✅ direct | ✅ `create in Rel { }` | 🟡 via `AddEffectToAction` | ❌ | Spawn-and-wire |
| **Composite** | ✅ VM | 🟡 flatten/comment | 🟡 construct | ❌ | Nested structure |
| **Conditional** | ✅ VM | 🟡 weak print | 🟡 construct | ❌ | Branching |
| **InvokeAction** | ✅ direct | ❌ rejected (`invoke` unsupported) | 🟡 construct | ❌ | Multi-entity workflows |
| **DeleteEntityInstance** | ✅ soft-delete | ❌ no DSL | 🟡 construct | ❌ | Soft-delete flag |
| **LinkRelationship** | 🟡 / store | ❌ no DSL | 🟡 construct | ❌ | Often `Store.Link` API |
| **UnlinkRelationship** | 🟡 / store | ❌ no DSL | 🟡 construct | ❌ | |
| **TransitionRelationship** | 🟡 | ❌ | 🟡 | ❌ | Weaker product story |
| OnEntry / OnExit effects | ✅ on transition | ✅ `entry`/`exit` blocks | ✅ stage effect changes | ❌ | Stage hooks |
| Stage **when** subscriptions | ✅ store notify | ✅ `when Rel Stages { }` | 🟡 | ❌ | Not an Effect type; related |
| Host I/O (email, HTTP, queue) | 🚫 | 🚫 | 🚫 | 🚫 | Post–P3 / host adapters |

**Authoring bottleneck:** rows that are ✅ runtime but ❌ DSL or ❌ MCP.

---

## 3. “Useful enough” bar (lifecycle kernel)

A domain is **useful** for internal process modeling when agents can author and **exercise**:

| Capability | Effect / feature | Status |
|------------|------------------|--------|
| Advance lifecycle | transition | ✅ DSL + RT |
| Mutate fields | assign | ✅ DSL + RT |
| Spawn related work | create / create in | ✅ DSL + RT |
| React to peers | when + store | ✅ DSL + RT |
| Guard | policy / require | ✅ |
| Soft-remove | delete | 🟡 runtime only |
| Connect existing instances | link / unlink | 🟡 runtime / store |
| Call another action | invoke | 🟡 runtime; DSL rejects |

**Kernel bar (dogfood-2):** met for Order/Customer-style.  
**Workflow bar:** not met until delete + link + invoke (or honest substitutes) are authorable.

---

## 4. Design rules

1. **Runtime first** — do not add DSL for effects that CallAction cannot execute.  
2. **DSL before MCP micro-tools** — batch path is proven; micro-tools only where incremental edit pain is real (dogfood).  
3. **One golden domain per slice** — e.g. support ticket: open → assign (link) → escalate (invoke) → close (delete).  
4. **Honesty** — guide + tool Description match parser; no lab keywords.  
5. **SA constraints** — stage-action Option B snapshot limits still apply when placing actions with effects ([§6e](mcp-phase3-oracle-surface.md)).  
6. **No effect soup** — prefer compose of assign/transition/create over one-off “business” effect types.  
7. **Host I/O out of scope** — email/HTTP/payments are not Phase 1a effects.

---

## 5. Slices (execution order)

### E0 — Matrix freeze + guide honesty (**small**)

- [ ] **E0.1** Keep this matrix updated when effects change (same PR as product change).  
- [ ] **E0.2** Product guide § effects matches parser only (`transition`, `assign`, `create`, `create in`, entry/exit).  
- [ ] **E0.3** Document in guide or DomainModeling README: which effects exist in IR but not DSL (delete, link, invoke).  
- [ ] **E0.4** Optional: dogfood domain shortlist that *should* hurt (ticket / loan / fulfillment).

**Exit:** Agents can see “supported vs library-only” without reading source.

---

### E1 — Soft-delete product path (**small–medium**)

**Goal:** Author and exercise “close / cancel / archive” without custom C#.

- [ ] **E1.1** DSL: parse/print `delete` (or chosen keyword) → `DeleteEntityInstance`.  
- [ ] **E1.2** Guide + printer round-trip.  
- [ ] **E1.3** Golden: action with delete → CallAction → `IsDeleted` / MCP refuses further actions.  
- [ ] **E1.4** Optional MCP: not required if DSL suffices; thin tool only if dogfood demands.

**Exit:** Soft-delete is first-class on product path; RT′.6 remains correct.

---

### E2 — Link / unlink product path (**medium**)

**Goal:** Connect existing instances from domain effects, not only create-in or `Store.Link` from tests.

- [ ] **E2.1** Spec: expression form for target instance in DSL (hard: instances are runtime). Decide:
  - **(a)** effect only meaningful at runtime with bound targets, or  
  - **(b)** link only via create-in / RelationshipName (document as product limit), or  
  - **(c)** parameter-bound link (`link Rel to param`) once action params are first-class in DSL.  
- [ ] **E2.2** Implement chosen form end-to-end (parse → evolve → CallAction → store).  
- [ ] **E2.3** Golden multi-instance: create A, B, link, observe subscription if applicable.  
- [ ] **E2.4** Unlink symmetric if link ships.

**Exit:** Documented decision + either working path or explicit non-goal with create-in as substitute.

**Note:** Link-in-DSL is the hardest slice because **instance identity is runtime**. Do not pretend compile-time entity names are instances.

---

### E3 — Invoke product path (**medium**)

**Goal:** Multi-entity workflows without manual second CallAction from the agent.

- [ ] **E3.1** Spec: `invoke` target (self vs related instance path).  
- [ ] **E3.2** Un-reject or reintroduce DSL keyword only when runtime semantics + store resolution are clear.  
- [ ] **E3.3** Guard recursion / re-entrancy (OnEntry → invoke → transition).  
- [ ] **E3.4** Golden: parent action invokes child action on linked instance.  
- [ ] **E3.5** Guide honesty: what can be invoked.

**Exit:** At least one multi-entity invoke path green under MCP RT.

---

### E4 — Conditional / composite authoring (**medium**, after E1–E3 if needed)

- [ ] **E4.1** DSL sugar for conditional effects (if product needs branchy actions without C#).  
- [ ] **E4.2** Printer round-trip (stop “flattened comment” only).  
- [ ] **E4.3** Goldens: conditional assign/transition.

**Exit:** Branchy actions authorable without hand-built IR.

---

### E5 — MCP thin effect tools (**small**, pull with dogfood)

Only if agents refuse full `apply_dsl` for incremental effect edits after E1–E3:

| Tool (sketch) | Wraps |
|---------------|--------|
| `add_transition_to_action` | `AddStageTransitionEffect` |
| `add_assign_to_action` | `AssignEffect` |
| `add_create_in_to_action` | `CreateEntityInRelationshipEffect` |

- [ ] **E5.1** Named dogfood pain (agent quotes).  
- [ ] **E5.2** One tool vertical + smoke.  
- [ ] **E5.3** Descriptions respect SA snapshot rules for stage actions.

**Exit:** Not a full effect catalog — only proven thin wrappers.

---

### E pull / non-goals

| Item | When / why |
|------|------------|
| Email, HTTP, queue, payment effects | Host integration / post–P3 packs |
| Full effect-micro for every IR type | Completeness trap |
| Event publish/subscribe effects | Retired product path |
| Containers / C# codegen for effects | §6d post–Phase 3 |
| Fixing SA stale snapshot (Option A) | phase3 SA′′.4 — separate unless blocked here |

---

## 6. Suggested PR stack

1. **E0** — matrix + guide “IR vs DSL” honesty  
2. **E1** — delete DSL + golden  
3. **E2** — link decision + implement or document substitute  
4. **E3** — invoke product path  
5. **E4** — conditional DSL only if a golden domain demands it  
6. **E5** — MCP thin tools only with dogfood quotes  

---

## 7. Test / dogfood plan

| Check | Asserts |
|-------|---------|
| Parity table | Updated in same change as new effect surface |
| Round-trip | parse → print → parse for each new DSL effect |
| RT smoke | create_instance → call_action → observe side effects |
| Multi-entity | at least one E2 or E3 golden with two instances |
| Negative | unsupported keywords still fail-loud |

**Recommended dogfood domain (hurts on purpose):** support **Ticket**

```text
Ticket: Open → (assign agent / link) → InProgress → (escalate / invoke) → Resolved → (archive / delete)
Customer when tickets Resolved { … }
```

What you cannot write in DSL without this plan is the backlog order.

---

## 8. Success criteria (thin vertical)

- [ ] E0 honesty: guide/docs list supported vs library-only effects  
- [ ] At least **E1** green (delete on product path) **or** explicit reject with substitute  
- [ ] E2 **decision** recorded (implement or create-in-only)  
- [ ] One multi-entity workflow green (E2 or E3) **or** deferred with reason  
- [ ] Suite green; product guide in sync  
- [ ] No host I/O effects; no event tools  

**“Useful enough” claim:** lifecycle kernel + soft-delete + (link **or** invoke) authorable and exercisable via MCP RT without custom C# for the dogfood Ticket (or named substitute domain).

---

## 9. Agent pick (right now)

```text
DONE:    Phase 3 thin; RT; SA MVP; dogfood-1/2 lifecycle kernel
CURRENT: E0 — freeze matrix + guide honesty (IR vs DSL)
THEN:    E1 delete product path
THEN:    E2 link decision / E3 invoke (order by dogfood domain pain)
LATER:   E4 conditional DSL; E5 MCP thin effect tools
PULL:    Host I/O; full micro-catalog; L* containers
```

**Implementer watch-outs**

- Do not add DSL for effects CallAction cannot run.  
- Link-in-DSL needs a **runtime target** story — do not fake it with type names alone.  
- Prefer `apply_dsl` for batch effect graphs; micro-tools are optional sugar.  
- Keep SA snapshot honesty when placing stage actions.  
- Update this matrix in the same PR as product changes.
