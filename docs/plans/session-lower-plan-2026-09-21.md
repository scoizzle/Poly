# session.Lower — plan (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**Scot decision (2026-09-21):** **post-analyze Lower is product SoT.** Clean analyze (fail-closed) → **then** Lower.
**Scot addendum (2026-09-21):** **Lower’s result = a set of artifacts from 1+ producers.** May come from **multiple compilation units** producing **varying different concepts** — not solely “one Syntax module” as the entire output story.
**Superseded:** **Lower-inside-analysis** is **not** product SoT. Retracted. Do not revive.
**Grounds:** tip `61d0688a` on `ontology/talk-alignment-2026-09-21`.
**Slices:** A–E stay **unapproved** until Scot **accepts this plan**. Eng WIP = **0**. This file does **not** accept the plan.
**Parked (locked):** **Item 5** Occupancy / `BusySections`. **PR 73**. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, or start a slice.
**Audience:** Scot
**Grounding:** [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) · [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md)

**Product rule (unchanged):** simulation = **Interpreter** on the **real lowered Syntax** (the sim tree in that set). Producers are **not** a fake runtime and **not** a second simulate path. DEI / Effect-IR are not product sim.

**This note is SoT** for the Lower **vision**. Glossary: [`session-lower-abstractions-2026-09-21.md`](session-lower-abstractions-2026-09-21.md) — SoT for named concepts.

---

## 1) What `session.Lower` is

The **compile** step **after** a clean analyze. Part of **Session Compile**.

`DomainSession` loads libraries and runs **one** analyze (bags on nodes). If that analyze is **dirty → STOP** (no Lower, no artifacts). If it is **clean**, **`session.Lower`** runs.

**Result of Lower:** a **set of artifacts** emitted by **one or more producers**. Those producers may belong to **multiple compilation units** and produce **different kinds of concept** (not one shape of file).

**Among that set:** the **Syntax operation module** is **one artifact** — the **sim / meaning tree** (generic Syntax; no bags in the tree). **Interpreter** executes **that** tree. It is **not** the entire output story by itself.

Host / surface artifacts from producers are **also** in the set: **call-sites / delivery** that **call** the module. They are part of Lower’s result. They are **not** simulate.

It is not execute. Not MCP. Not DEI.

**Not SoT:** folding Lower into analyze. Retracted.

---

## 2) Inputs / outputs

**In (after clean analyze only):**

- **Facts** — the `Domain` the author wrote.
- **Bags / concerns** — analysis metadata on authoring nodes. Lower **reads** bags. Authoring IR (`DomainExpression`, `Effect`) is parse output, not execute input.

**Out (the artifact set):**

- **Syntax operation module** — one artifact in the set. The sim/meaning tree. Same tree for **Interpreter** and C# print. No twin. No consumer flag.
- **Other producer artifacts** — host call-sites and whatever other concepts other compilation units emit. Delivery, not sim. Catalog of “what counts” is still open (below).

---

## 3) Fail-closed

| If | Then |
|----|------|
| Analyze is dirty | **STOP.** No Lower. No artifact set. No execute invent. |
| A shipped construct cannot lower to a complete legal **sim tree** | **Fail.** No `Comment` / `null` / host-escape as meaning. Shrink the language. |
| Lower is incomplete (missing sim tree, or a required producer cannot emit) | **Fail.** Do not let Interpreter / print / a door finish lowering. |
| A door or producer needs an op that did not lower, or a bag that was never published | **Fail closed** at that consumer. |
| Interpreter and print disagree on the **sim tree** | Bug is in **Lower** (that tree). Not Store, MCP, DEI, or a new flag. |

Unknown / duplicate `uses` fail at **Load**.

---

## 4) How Interpreter sim binds the Syntax artifact

**Simulate** = **Interpreter** executes the **real lowered Syntax** artifact in the set.

Bind is consumer-side: caller supplies **`This`** / **Store** for jobs that tree already names. Bind is not a second meaning path. C# print projects the **same** tree. MCP may **ask** Interpreter to run a named op — sim only if it is this run.

Not sim: DEI, Effect-IR, scratch fake execute, producer emit, host `Stay.Create` as the operation. Producer artifacts in the set are still **not** a second simulate path.

---

## 5) What must die

Honesty, not delete-tomorrow. This note does not delete code.

| Die as meaning / sim / extra door | What that means |
|-----------------------------------|-----------------|
| **Lower-inside-analysis as product SoT** | Superseded. Do not mill it. Do not revive. |
| **“One module is the entire Lower output”** | Too small. Module is the **sim tree** in a **multi-producer artifact set**. |
| **Execute-time `LowerActionBody` residual** | Nested StageTransition flush / Domain-null standalone / leftover per-call policy lower belong **in `session.Lower`**. Execute never lowers. |
| **`UseThisReference` twin / sibling trees** | One sim-tree body from Lower. Interpreter and print share it. |
| **DEI as simulate / proof** | Not-sim, not-proof. **Do not delete DEI from this file.** |
| **Producers-as-sim / fake runtime** | They emit artifacts in the set (call-sites / other concepts). They do not simulate. |
| **Consumer lowering flags** | Fix the sim tree once. |

Does **not** die: Ontology facts, `.poly`, session/libraries, evolution, fact-publishing analysis, the **post-analyze Lower door**, producer emit as **delivery**, Item 5, PR 73.

---

## 6) Open design questions (Scot)

**Decided (not open):** sequencing = clean analyze → then Lower. Sim = Interpreter on the real Syntax artifact. DEI not sim. Producers not a second sim path. Lower-inside-analysis retracted.

Remaining real choices. Accepting this plan still does **not** admit CURRENT, unpark Item 5 or PR 73, or start eng.

1. **What is a compilation unit?** Library? Domain? Per-operation? Something else? Do not mill a graph until this is named.
2. **How is the artifact set cataloged?** Who lists the artifacts Lower produced? Session? Producers’ own registries? Fail closed if the catalog is empty when it must not be?
3. **Is the Syntax module privileged among artifacts for meaning only?** Interpreter sim is that tree — is “meaning” only that artifact, with everything else delivery? Or do other artifacts carry other kinds of meaning?
4. **Residual inventory.** Nested StageTransition flush and Domain-null standalone belong in `session.Lower`. Is per-call policy lower the rest of the sim-tree list?
5. **Sim-tree cache.** Lower the Syntax artifact once per session revision; Interpreter and print share it. Re-Lower on domain/session change — never per invoke. Agree?
6. **`This` bind without DEI-as-sim.** Honest product-sim bind vs leftover DEI harness smoke.
7. **What “accept this plan” unlocks.** Next talk is still slice greenlight, **A then B first** (one real sim tree; Lower at Lower). Still one slice at a time. Still Eng WIP = 0 until that greenlight. Item 5 PARKED. PR 73 PARKED. Accepting is **not** CURRENT.

---

## Locks

- **Not CURRENT.** PIPELINE-STATUS stays `(none)`.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete** from this file.
- **No slice greenlight** until Scot accepts this plan, then still per-slice greenlight.
- **Product sim = Interpreter on the real lowered Syntax artifact.**
- **Lower-inside-analysis is not SoT.**

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`session-lower-abstractions-2026-09-21.md`](session-lower-abstractions-2026-09-21.md) | Named concepts. Artifact set + sim tree. |
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule. “One module” there = the **sim tree**, not the whole artifact set. |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices A–E (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
