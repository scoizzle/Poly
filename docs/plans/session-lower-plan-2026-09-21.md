# session.Lower — plan (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**Scot decision (2026-09-21):** **post-analyze Lower is product SoT.** Clean analyze (fail-closed) → **then** Lower produces **one** Syntax module.
**Superseded:** **Lower-inside-analysis** (tip `abb3435a` explore) is **not** product SoT. Retracted.
**Grounds:** tip `6c2572e3` on `ontology/talk-alignment-2026-09-21`.
**Slices:** A–E stay **unapproved** until Scot **accepts this plan**. Eng WIP = **0**. This file does **not** accept the plan.
**Parked (locked):** **Item 5** Occupancy / `BusySections`. **PR 73**. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, or start a slice.
**Audience:** Scot
**Grounding:** [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) · [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md)

**Product rule (unchanged):** simulation = **Interpreter** on that **real** `session.Lower` module. Artifact producers = host call-sites, not sim. DEI / Effect-IR are not product sim.

**This note is SoT** for the Lower **vision** (post-analyze door). Glossary: [`session-lower-abstractions-2026-09-21.md`](session-lower-abstractions-2026-09-21.md) — SoT for named concepts.

---

## 1) What `session.Lower` is

The **compile** step **after** a clean analyze. Part of **Session Compile**.

`DomainSession` loads libraries and runs **one** analyze (bags on nodes). If that analyze is **dirty → STOP** (no Lower, no host files). If it is **clean**, **`session.Lower`** produces **one operation module**: complete generic Syntax per shipped action, policy, subscription, create, and transition.

It is not execute. Not print. Not MCP. Not DEI. Those consume the module **after** Lower (or, for producers, emit files that **call** it).

**Not SoT:** folding Lower into analyze so “clean analyze *is* the trees.” That was an explore. Retracted.

---

## 2) Inputs / outputs

**In (after clean analyze only):**

- **Facts** — the `Domain` the author wrote.
- **Bags / concerns** — analysis metadata on authoring nodes. Lower **reads** bags. Authoring IR (`DomainExpression`, `Effect`) is parse output, not execute input.

**Out:**

- **One** Syntax **operation module**. Generic trees. **No bags in the tree.** Same module for **Interpreter** and C# print. No twin. No consumer flag.

Host files are **not** this output. Library **artifact producers** emit **call-sites** from **surface bags** after the same clean-analyze gate. They **call** the module. Not simulate.

---

## 3) Fail-closed

| If | Then |
|----|------|
| Analyze is dirty | **STOP.** No Lower. No host files. No execute invent. |
| A shipped construct cannot lower to a complete legal tree | **Fail.** No `Comment` / `null` / host-escape as meaning. Shrink the language. |
| Lower is incomplete | **Fail.** Do not let Interpreter / print / a door finish lowering. |
| A door or producer needs an op that did not lower, or a bag that was never published | **Fail closed** at that consumer. |
| Interpreter and print disagree | Bug is in **Lower** (one tree). Not Store, MCP, DEI, or a new flag. |

Unknown / duplicate `uses` fail at **Load**.

---

## 4) How Interpreter sim binds that module

**Simulate** = **Interpreter** executes the **real** `session.Lower` tree.

Bind is consumer-side: caller supplies **`This`** / **Store** for jobs the module already names. Bind is not a second meaning path. C# print projects the **same** tree. MCP may **ask** Interpreter to run a named op — sim only if it is this run.

Not sim: DEI, Effect-IR, scratch fake execute, producer emit, host `Stay.Create` as the operation.

---

## 5) What must die

Honesty, not delete-tomorrow. This note does not delete code.

| Die as meaning / sim / extra door | What that means |
|-----------------------------------|-----------------|
| **Lower-inside-analysis as product SoT** | Superseded framing. Do not mill it. |
| **Execute-time `LowerActionBody` residual** | Nested StageTransition flush / Domain-null standalone / leftover per-call policy lower belong **in `session.Lower`**. Execute never lowers. |
| **`UseThisReference` twin / sibling trees** | One cached module from Lower. Interpreter and print share it. |
| **DEI as simulate / proof** | Not-sim, not-proof. **Do not delete DEI from this file.** |
| **Producers-as-sim** | Host call-site delivery only. Gated by clean analyze. |
| **Consumer lowering flags** | Fix Lower once. |

Does **not** die: Ontology facts, `.poly`, session/libraries, evolution, fact-publishing analysis, the **post-analyze Lower door**, Item 5, PR 73.

---

## 6) Open design questions (Scot)

Scot **decided** sequencing: clean analyze → then Lower. That is **not** open.

Remaining real choices. Accepting this plan still does **not** admit CURRENT, unpark Item 5 or PR 73, or start eng.

1. **Residual inventory.** Nested StageTransition flush and Domain-null standalone belong in `session.Lower`. Is per-call policy lower the rest of the list, or is there more execute-time lower to find?
2. **One module cache.** Lower once per session revision; Interpreter and print share that cache. Re-Lower on domain/session change — never per invoke. Agree?
3. **Producers vs Lower.** Both only after clean analyze. Should host emit also **fail closed if Lower failed**, or do doors fail later?
4. **`This` bind without DEI-as-sim.** Honest product-sim bind (caller-supplied Store/`This` on the module) vs leftover DEI harness smoke.
5. **What “accept this plan” unlocks.** Next talk is slice greenlight, **A then B first** (one real tree; Lower at Lower). Still one slice at a time. Still Eng WIP = 0 until that greenlight. Item 5 PARKED. PR 73 PARKED. DEI not deleted. Accepting is **not** CURRENT.

---

## Locks

- **Not CURRENT.** PIPELINE-STATUS stays `(none)`.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete** from this file.
- **No slice greenlight** until Scot accepts this plan, then still per-slice greenlight.
- **Product sim = Interpreter on the `session.Lower` module.**
- **Lower-inside-analysis is not SoT.**

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`session-lower-abstractions-2026-09-21.md`](session-lower-abstractions-2026-09-21.md) | Named concepts. Post-analyze Lower. |
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule. Stage 2′ then 3 Lower matches this SoT. |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices A–E (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
