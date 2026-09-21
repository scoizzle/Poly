# Lower-as-analysis — abstractions (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**When:** **Before accept / eng.** This glossary does **not** accept the Lower plan. Slices A–E stay unapproved. Eng WIP = **0**.
**Parked (locked):** **Item 5** Occupancy / `BusySections`. **PR 73**. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, start a slice, or answer the open design questions.
**Audience:** Scot
**Grounding:** tip `abb3435a` on `ontology/talk-alignment-2026-09-21` · [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) (SoT for the **vision**) · principles + path + sim rule on this branch

**This note is SoT for the named concepts.** The Lower plan stays SoT for Lower-inside-analysis.

These words are **fixed**. Two questions stay **Scot’s** (pass vs bag; where the module lives). Do not invent a third IR.

---

## Glossary

| Name | What it is | How it fits Lower-inside-analysis |
|------|------------|-----------------------------------|
| **Facts** | What the author wrote: `Domain` — types, stages, operations, relationships, `uses` ids. | Analysis **input**. Not the running program. |
| **Bags / concerns** | Analysis metadata hung on **authoring** nodes (catalog, storage, HTTP, dispatch, …). | Analysis **input to lowering**. Lowering **reads** them. They do **not** appear in the Syntax tree. |
| **Analysis pass** | One `INodeAnalyzer` (or equivalent) in the **one** analyze. | Lowering is **in** this analyze — a pass and/or a concern — **not** a door after it. Which of those two is still open (below). |
| **Node replacement** | Analysis may **replace** a node with another Node. Tree identity stays the analysis seam. | How a body can become generic Syntax **during** analyze. Not a side table. Not a second pipeline. |
| **Operation module / lowered body** | Generic Syntax: types + operation bodies as Nodes. Complete trees for shipped actions, policies, subscriptions, creates, transitions. | **Output of clean analyze** (or analyze fails). Same trees for Interpreter and print. **No bags in the tree.** |
| **Store jobs on the tree** | Names the module already invokes (`Create`, `CreateIn`, `EnsureUnique`, …). | **Meaning is in the tree.** The directory is not the domain. |
| **Bind vs meaning** | **Meaning** = the lowered Syntax. **Bind** = caller supplies `This` / Store so Interpreter can run jobs the tree already names. | Bind is not a second meaning path. Not a DI container in the VM. |

**Related names (same picture, not extra product):**

| Name | What it is |
|------|------------|
| **Session Compile** | The session-shaped compile unit: load libraries + **one** analyze (Lower lives **inside** that analyze). |
| **Surface bags** | Opt-in door concerns (`uses sqlite`, `uses http`) that **select** a host implementation. |
| **Artifact producers** | After **clean** analyze, emit host **call-sites** that **call** the module. Delivery. **Not** simulate. **Not** a Lower door. |
| **Interpreter** | Product **sim**: executes the **real** lowered trees. The only simulate path. |

---

## Map (one line)

Facts + bags/concerns → **one analyze** (passes, bags, replacement; Lower **inside**) → **operation module** (generic Syntax) · producers emit **call-sites** from surface bags · Interpreter **binds** Store/`This` and **runs** the module.

Clean analyze **is** those trees, or fail-closed. One gate.

---

## What is **not** an abstraction

Do not treat these as lowering concepts. They are debt or forbidden product doors.

| Not an abstraction | Why |
|--------------------|-----|
| **DEI sim** | DEI is not product sim and not meaning. Harness bind at most. Do not delete DEI from this file. |
| **Twin trees** (`UseThisReference` sibling) | One lowered body from analyze. Interpreter and print share it. A second runtime-shaped tree is not a concept — it is a bug. |
| **Post-analyze Lower door** | No product `session.Lower` after a green analyze. If a method by that name remains, it can only **read** what analyze already made. |

Also not abstractions here: Effect-IR as execute, consumer lowering flags, producers-as-sim.

---

## Open questions (still Scot’s — not answered here)

Words above are fixed. These two stay open in the Lower plan. This glossary does **not** pick them.

1. **Pass vs bag.** Is Lower **one analysis pass** that emits/replaces into Syntax trees, or a **concern bag that holds the module** (trees as bag payload — still not bags *in* the Syntax tree)? CORE already has passes, bags, and node replacement. Pick one later. Do not invent a third IR.
2. **Where the module lives after analyze.** Session field? `AnalysisResult`? Bag on a root node? Replacement of authoring bodies? Storage so Interpreter/print **read** — they do not re-lower.

Other Lower-plan questions (producers finding trees, Session Compile naming, leftover `session.Lower` method) stay on the plan. Not this file.

---

## Locks

- **Not CURRENT.** This does **not** accept the Lower plan.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete / no slice greenlight.** Eng WIP = 0.

## Related

| Doc | Role |
|-----|------|
| [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) | SoT for the **vision** (Lower-inside-analysis) |
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
