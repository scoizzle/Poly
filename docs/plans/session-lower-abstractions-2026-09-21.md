# session.Lower — abstractions (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**When:** **Before accept / eng.** This glossary does **not** accept the Lower plan. Slices A–E stay unapproved. Eng WIP = **0**.
**Scot decision (2026-09-21):** post-analyze Lower is product SoT. **Lower-inside-analysis** is superseded / not SoT.
**Scot addendum (2026-09-21):** Lower’s result = **artifact set** from **1+ producers** / **multiple compilation units**. The Syntax module is the **sim tree among them**, not the entire output.
**Parked (locked):** **Item 5** Occupancy / `BusySections`. **PR 73**. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, start a slice, or answer the open design questions.
**Audience:** Scot
**Grounding:** tip `61d0688a` on `ontology/talk-alignment-2026-09-21` · [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) (SoT for the **vision**)

**This note is SoT for the named concepts.** The Lower plan is SoT for post-analyze Lower + artifact-set addendum.

---

## Glossary

| Name | What it is | How it fits post-analyze Lower |
|------|------------|--------------------------------|
| **Facts** | What the author wrote: `Domain` — types, stages, operations, relationships, `uses` ids. | Analyze **input**. Not the running program. |
| **Bags / concerns** | Analysis metadata hung on **authoring** nodes (catalog, storage, HTTP, dispatch, …). | Analyze **output**; Lower **reads** them. They do **not** appear in the Syntax tree. |
| **Analysis pass** | One `INodeAnalyzer` (or equivalent) in the **one** analyze. | Proves the domain (bags, replacement). Does **not** replace the Lower door. |
| **Node replacement** | Analysis may **replace** a node with another Node. Tree identity stays the analysis seam. | Authoring analysis. Not a second product Lower. Not a side table. |
| **Artifact set** | **Result of Lower:** artifacts from **1+ producers**, possibly from **multiple compilation units**, possibly **different concepts**. | The whole Lower output. Not “one module” by itself. |
| **Compilation unit** | A unit that can contribute producers / artifacts into that set. | **Named, not specified.** Library vs domain vs per-operation is still Scot’s (below). |
| **Operation module / lowered body** | Generic Syntax: types + operation bodies as Nodes. **No bags in the tree.** | **One artifact in the set** — the **sim / meaning tree**. What **Interpreter** executes. Not the entire output story. |
| **Store jobs on the tree** | Names the sim tree already invokes (`Create`, `CreateIn`, `EnsureUnique`, …). | **Meaning of that tree.** The directory is not the domain. |
| **Bind vs meaning** | **Meaning** (for sim) = the lowered Syntax artifact. **Bind** = caller supplies `This` / Store. | Bind is not a second meaning path. Other artifacts in the set are not a second sim. |

**Related names (same picture, not extra product):**

| Name | What it is |
|------|------------|
| **Session Compile** | Load libraries → **one** analyze (fail-closed) → **then** `session.Lower` → **artifact set**. |
| **Surface bags** | Opt-in door concerns (`uses sqlite`, `uses http`) that **select** a host implementation. |
| **Artifact producers** | Run as part of **Lower** (after clean analyze). Emit artifacts into the set (host **call-sites** and other concepts). **Not** simulate. **Not** a fake runtime. |
| **Interpreter** | Product **sim**: executes the **real** Syntax artifact in the set. The only simulate path. |
| **`session.Lower`** | The **allowed product door** after clean analyze. Output = the **artifact set**. |

---

## Map (one line)

Facts → **one analyze** (bags / concerns, passes, replacement) → **gate** (dirty → STOP) → **`session.Lower`** → **artifact set** (Syntax **sim tree** + other producer artifacts from 1+ compilation units) · Interpreter **binds** Store/`This` and **runs the sim tree** · host artifacts **call** that tree, they do not simulate.

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

## Open questions (still Scot’s — not answered here)

Words above are fixed. Sequencing is **decided** (clean analyze → then Lower). Sim tree = Interpreter on real Syntax. This glossary does **not** pick:

1. **Compilation unit** — library? domain? per-operation?
2. **Artifact-set catalog** — how the set is listed / fail-closed.
3. **Privilege of the Syntax module** — meaning-only among artifacts, or do other artifacts carry other meaning?

Residual placement, sim-tree cache, `This` bind, and what accept unlocks live on the Lower plan.

---

## Locks

- **Not CURRENT.** This does **not** accept the Lower plan.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete / no slice greenlight.** Eng WIP = 0.
- **Lower-inside-analysis is not SoT.**

## Related

| Doc | Role |
|-----|------|
| [`session-lower-plan-2026-09-21.md`](session-lower-plan-2026-09-21.md) | SoT for the **vision** (post-analyze Lower + artifact set) |
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
