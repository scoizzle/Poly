# session.Lower — abstractions (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**When:** **Before accept / eng.** This glossary does **not** accept the Lower plan. Slices A–E stay unapproved. Eng WIP = **0**.
**Scot decision (2026-09-21):** post-analyze Lower is product SoT. **Lower-inside-analysis** is superseded / not SoT.
**Parked (locked):** **Item 5** Occupancy / `BusySections`. **PR 73**. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, start a slice, or answer the open design questions.
**Audience:** Scot
**Grounding:** tip `6c2572e3` on `ontology/talk-alignment-2026-09-21` · [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) (SoT for the **vision**) · principles + path + sim rule on this branch

**This note is SoT for the named concepts.** The Lower plan is SoT for post-analyze Lower.

---

## Glossary

| Name | What it is | How it fits post-analyze Lower |
|------|------------|--------------------------------|
| **Facts** | What the author wrote: `Domain` — types, stages, operations, relationships, `uses` ids. | Analyze **input**. Not the running program. |
| **Bags / concerns** | Analysis metadata hung on **authoring** nodes (catalog, storage, HTTP, dispatch, …). | Analyze **output**; Lower **reads** them. They do **not** appear in the Syntax tree. |
| **Analysis pass** | One `INodeAnalyzer` (or equivalent) in the **one** analyze. | Proves the domain (bags, replacement). Does **not** replace the Lower door. |
| **Node replacement** | Analysis may **replace** a node with another Node. Tree identity stays the analysis seam. | Authoring analysis. Not a second product Lower. Not a side table. |
| **Operation module / lowered body** | Generic Syntax: types + operation bodies as Nodes. Complete trees for shipped actions, policies, subscriptions, creates, transitions. | **Output of `session.Lower`** after clean analyze. Same trees for Interpreter and print. **No bags in the tree.** |
| **Store jobs on the tree** | Names the module already invokes (`Create`, `CreateIn`, `EnsureUnique`, …). | **Meaning is in the tree.** The directory is not the domain. |
| **Bind vs meaning** | **Meaning** = the lowered Syntax. **Bind** = caller supplies `This` / Store so Interpreter can run jobs the tree already names. | Bind is not a second meaning path. Not a DI container in the VM. |

**Related names (same picture, not extra product):**

| Name | What it is |
|------|------------|
| **Session Compile** | Load libraries → **one** analyze (fail-closed) → **then** `session.Lower` → one module. Producers emit host call-sites after the same analyze gate. |
| **Surface bags** | Opt-in door concerns (`uses sqlite`, `uses http`) that **select** a host implementation. |
| **Artifact producers** | After **clean** analyze, emit host **call-sites** that **call** the module. Delivery. **Not** simulate. |
| **Interpreter** | Product **sim**: executes the **real** `session.Lower` module. The only simulate path. |
| **`session.Lower`** | The **allowed product door** after clean analyze. One module. |

---

## Map (one line)

Facts → **one analyze** (bags / concerns, passes, replacement) → **gate** (dirty → STOP) → **`session.Lower`** → **one operation module** · producers emit **call-sites** from surface bags · Interpreter **binds** Store/`This` and **runs** the module.

---

## What is **not** an abstraction

Do not treat these as lowering concepts.

| Not an abstraction | Why |
|--------------------|-----|
| **Lower-inside-analysis** | Superseded framing. Not product SoT. Retracted 2026-09-21. |
| **DEI sim** | DEI is not product sim and not meaning. Harness bind at most. Do not delete DEI from this file. |
| **Twin trees** (`UseThisReference` sibling) | One lowered body from `session.Lower`. Interpreter and print share it. A second runtime-shaped tree is a bug, not a concept. |

Also not abstractions here: Effect-IR as execute, consumer lowering flags, producers-as-sim.

**Allowed (not in this table):** the **post-analyze Lower door** is product SoT.

---

## Open questions (still Scot’s — not answered here)

Words above are fixed. Sequencing is **decided** (clean analyze → then Lower). This glossary does **not** pick the rest.

1. **Module cache.** Lower once per session revision; Interpreter and print share. Re-Lower on domain/session change — never per invoke?
2. **Residual placement.** Execute-time `LowerActionBody` belongs **in `session.Lower`**. How complete is that list?

Other remaining Qs live on the Lower plan (producers if Lower failed; `This` bind vs DEI smoke; what accept unlocks). Not answered here.

---

## Locks

- **Not CURRENT.** This does **not** accept the Lower plan.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete / no slice greenlight.** Eng WIP = 0.
- **Lower-inside-analysis is not SoT.**

## Related

| Doc | Role |
|-----|------|
| [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) | SoT for the **vision** (post-analyze Lower) |
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
