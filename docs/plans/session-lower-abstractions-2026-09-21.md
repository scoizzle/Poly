# session.Lower — abstractions (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**When:** **Before accept / eng.** This glossary does **not** accept the Lower plan. Slices A–E stay unapproved. Eng WIP = **0**.
**Scot decision (2026-09-21):** post-analyze Lower is product SoT. **Lower-inside-analysis** is superseded / not SoT.
**Scot addendum (2026-09-21):** Lower’s result = **artifact set** from **1+ producers** / **multiple compilation units**. The Syntax module is the **sim tree among them**, not the entire output.
**Scot locked defs (2026-09-21):** compilation unit = **library**; catalog = **on the session** (fail closed if empty when required); Syntax module **privileged** for meaning + sim.
**Parked (locked):** **Item 5** Occupancy / `BusySections`. **PR 73**. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, start a slice, or invent residual / cache / This-bind / accept.
**Audience:** Scot
**Grounding:** tip `d5e1a9cc` on `ontology/talk-alignment-2026-09-21` · [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) (SoT for the **vision**)

**This note is SoT for the named concepts.** The Lower plan is SoT for post-analyze Lower + artifact-set addendum.

---

## Glossary

| Name | What it is | How it fits post-analyze Lower |
|------|------------|--------------------------------|
| **Facts** | What the author wrote: `Domain` — types, stages, operations, relationships, `uses` ids. | Analyze **input**. Not the running program. |
| **Bags / concerns** | Analysis metadata hung on **authoring** nodes (catalog, storage, HTTP, dispatch, …). | Analyze **output**; Lower **reads** them. They do **not** appear in the Syntax tree. |
| **Analysis pass** | One `INodeAnalyzer` (or equivalent) in the **one** analyze. | Proves the domain (bags, replacement). Does **not** replace the Lower door. |
| **Node replacement** | Analysis may **replace** a node with another Node. Tree identity stays the analysis seam. | Authoring analysis. Not a second product Lower. Not a side table. |
| **Artifact set** | **Result of Lower:** artifacts from **1+ producers** registered by libraries, possibly **different concepts**. | The whole Lower output. Not “one module” by itself. Cataloged on the **session**. |
| **Compilation unit** | A **library** (`uses` / `IDomainLibrary`) that **registers producers**. | **Decided.** Not domain-as-unit. Not per-operation-as-unit. |
| **Artifact-set catalog** | The session’s list of artifacts Lower produced. | Held on the **session** after Lower. **Fail closed** if empty when required. |
| **Operation module / lowered body** | Generic Syntax: types + operation bodies as Nodes. **No bags in the tree.** | **Privileged** artifact for **operation meaning + Interpreter sim**. Not the entire output story. Other artifacts call/bind it. |
| **Store jobs on the tree** | Names the sim tree already invokes (`Create`, `CreateIn`, `EnsureUnique`, …). | **Meaning of that tree.** The directory is not the domain. |
| **Bind vs meaning** | **Meaning** (operation + sim) = the lowered Syntax artifact. **Bind** = caller supplies `This` / Store. Other artifacts = delivery / other concepts. | Bind is not a second meaning path. Other artifacts are **not** a second sim. |

**Related names (same picture, not extra product):**

| Name | What it is |
|------|------------|
| **Session Compile** | Load libraries → **one** analyze (fail-closed) → **then** `session.Lower` → **artifact set**. |
| **Surface bags** | Opt-in door concerns (`uses sqlite`, `uses http`) that **select** a host implementation. |
| **Artifact producers** | Registered by **libraries**. Run as part of **Lower** (after clean analyze). Emit artifacts into the set (host **call-sites** and other concepts). **Not** simulate. **Not** a fake runtime. They call/bind the module. |
| **Interpreter** | Product **sim**: executes the **real** Syntax artifact in the set. The only simulate path. |
| **`session.Lower`** | The **allowed product door** after clean analyze. Output = the **artifact set**. |

---

## Map (one line)

Facts → **one analyze** (bags / concerns, passes, replacement) → **gate** (dirty → STOP) → **`session.Lower`** → **artifact set** cataloged **on the session** (Syntax **sim tree**, privileged for meaning + Interpreter, + other **library** producer artifacts) · empty catalog when required → **fail closed** · Interpreter **binds** Store/`This` and **runs the sim tree** · other artifacts **call/bind** that tree, they do not simulate.

---

## What is **not** an abstraction

Do not treat these as lowering concepts.

| Not an abstraction | Why |
|--------------------|-----|
| **Lower-inside-analysis** | Superseded framing. Not product SoT. Retracted. Do not revive. |
| **DEI sim** | DEI is not product sim and not meaning. Harness bind at most. Do not delete DEI from this file. |
| **Twin trees** (`UseThisReference` sibling) | One sim-tree body from Lower. Interpreter and print share it. A second runtime-shaped tree is a bug, not a concept. |

Also not abstractions here: Effect-IR as execute, consumer lowering flags, producers-as-sim / fake runtime.

**Allowed (not in this table):** the **post-analyze Lower door**, and Lower’s result as a **multi-producer artifact set**.

---

## Decided (these three — not open)

1. **Compilation unit** = **library** (`uses` / `IDomainLibrary`) that registers producers.
2. **Artifact-set catalog** = held on the **session** after Lower; **fail closed** if empty when required.
3. **Syntax module** = privileged for **operation meaning + Interpreter sim**. Other artifacts = delivery / other concepts (host, HTTP, …); **not** a second sim; they call/bind the module.

Sequencing (clean analyze → then Lower) was already decided.

## Open questions (still Scot’s — not answered here)

This glossary does **not** invent:

1. **Residual placement** — execute-time `LowerActionBody` belongs in `session.Lower`; how complete is that list?
2. **Sim-tree cache** — once per session revision?
3. **`This` bind** vs DEI harness smoke.
4. **What accept unlocks.**

Those live on the Lower plan. Still not CURRENT. Still no slice greenlight.

---

## Locks

- **Not CURRENT.** This does **not** accept the Lower plan.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete / no slice greenlight.** Eng WIP = 0.
- **Compilation unit = library.** Catalog on **session**; fail closed if empty when required. Syntax module privileged for meaning + sim.
- **Lower-inside-analysis is not SoT.**

## Related

| Doc | Role |
|-----|------|
| [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) | SoT for the **vision** (post-analyze Lower + artifact set) |
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
