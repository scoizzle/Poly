# Lower-as-analysis — plan (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**Revises:** tip `754e9b8c` on `ontology/talk-alignment-2026-09-21` (same file/URL). Vision = **Lower as analysis**, not a post-analyze `session.Lower` door.
**Slices:** A–E stay **unapproved** until Scot **accepts this revised plan**. Eng WIP = **0**.
**Parked (locked):** store-vs-lower **Item 5** Occupancy / `BusySections`. **PR 73** stays PARKED. Do not touch. Do not unpark.
**This file does not** implement C#, delete product code, or start a slice.
**Audience:** Scot
**Grounding:** [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) · [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md)

**Product rule (unchanged):** simulation = **Interpreter** on the **real lowered Syntax trees**. Artifact producers = host call-sites, not sim. DEI / Effect-IR are not product sim.

**This note is SoT** for the Lower-as-analysis revision. Path / principles that still say “clean analyze, *then* `session.Lower`” are historical sequencing — not the product door.

**Abstractions (glossary):** [`session-lower-abstractions-2026-09-21.md`](session-lower-abstractions-2026-09-21.md) — SoT for the named concepts. This plan stays SoT for the vision. Does not accept this plan.

---

## 1) Lower is an analysis concern / pass

**Session Compile** still names the compile unit: `DomainSession` loads libraries and runs **one** analyze.

**Revision:** lowering is **part of that analyze**, not a second door after it.

Analysis that **proves** the domain also **produces** the real lowered Syntax trees — or **fail-closes**. Clean analyze **means** those trees exist (same gate). There is no product sequencing “analyze went green, now call `session.Lower`.”

If a `session.Lower` method still exists in code, that is leftover naming. Product-wise it can only **read** what analyze already made. It must not compile again.

It is not execute. Not print. Not MCP. Not DEI. Those consume the trees analyze produced (or, for producers, emit files that **call** them).

---

## 2) Inputs / outputs

**In (analysis):**

- **Facts** — the `Domain` the author wrote (types, stages, operations, relationships, `uses` ids).
- **Bags / concerns** — what analyze publishes on authoring nodes (catalog, storage, HTTP, dispatch, …). Lowering **reads** those concerns. Authoring IR (`DomainExpression`, `Effect`) stays **parse output**, not execute input.

**Out (same analyze, or fail):**

- Real **lowered Syntax** operation module / trees (every shipped action, policy, subscription, create, transition).
- Generic Syntax (assignments, `Invoke`, BCL members, Store jobs the tree already names).
- **No bags in the tree.** Same trees for **Interpreter** and C# print. No twin. No consumer flag.

Host files are **not** this output. Producers still emit **call-sites** from **surface bags** — only after **clean** analyze — and **call** those trees. Not a second Lower. Not simulate.

---

## 3) Fail-closed with analyze

One gate. No “analyze green then Lower fails later.”

| If | Then |
|----|------|
| Analyze is dirty (including incomplete or illegal lower) | **STOP.** No module. No host files. No execute invent. |
| A shipped construct cannot lower to a complete legal tree | **Analyze fails.** Do not ship `Comment`, `null`, or a host-escape as meaning. Shrink the language. |
| Trees missing after a “clean” analyze | That analyze was **not** clean. Fail closed. Do not let Interpreter / print / a door finish lowering. |
| A door or producer needs an op that did not lower, or a bag that was never published | **Fail closed** at that consumer — they do not invent the op. |
| Interpreter and print disagree | Bug is in **analysis/lowering** (one tree). Not Store, MCP, DEI, or a new flag. |

Unknown / duplicate `uses` still fail at **Load**.

---

## 4) How Interpreter sim binds those trees

**Simulate** = **Interpreter** executes the **real** trees analyze produced.

Bind is consumer-side: caller supplies **`This`** / **Store** for jobs the module already names. Bind is not a second meaning path. C# print projects the **same** trees. MCP may **ask** Interpreter to run a named op — sim only if it is this run.

Not sim: DEI, Effect-IR, scratch fake execute, producer emit, host `Stay.Create` as if it were the operation.

---

## 5) What must die

Honesty, not delete-tomorrow. This note does not delete code.

| Die as product door / meaning / sim | What that means |
|-------------------------------------|-----------------|
| **Standalone Lower-after-analyze** | No product door `session.Lower` after a green analyze. Lower lives **in** analyze. |
| **Execute-time `LowerActionBody` residual** | Nested StageTransition flush / Domain-null standalone / leftover per-call policy lower belong **in analysis**. Execute never lowers. |
| **`UseThisReference` twin / sibling trees** | One module body from analyze. Interpreter and print share it. |
| **DEI as simulate / proof** | Not-sim, not-proof. Harness bind at most. **Do not delete DEI from this file.** |
| **Producers-as-sim** | Host call-site delivery, gated by **clean analyze** (which already includes the trees). |
| **Consumer lowering flags** | Fix lowering once, inside analyze. |

Does **not** die here: Ontology facts, `.poly`, session/libraries, evolution, fact-publishing analysis, Item 5, PR 73.

---

## 6) Open design questions (Scot)

Real choices. Accepting this plan still does **not** admit CURRENT, unpark Item 5 or PR 73, or start eng.

1. **Pass vs bag.** Is Lower **one analysis pass** that emits/replaces into Syntax trees, or a **concern bag that holds the module** (trees as bag payload, still not bags *in* the Syntax tree)? CORE already has passes, bags, and node replacement — pick one; do not invent a third IR.
2. **Where the module lives after analyze.** Session field? `AnalysisResult`? Bag on a root node? Replacement of authoring bodies? Decide storage so Interpreter/print **read**, they do not re-lower.
3. **Producers without a Lower door.** They must still run **only after clean analyze**, emitting call-sites that **call** the trees that analyze already made. How do they find those trees without resurrecting `session.Lower` as a compile step?
4. **Session Compile naming.** Keep **Session Compile** as the session-shaped compile unit (load + one analyze that includes Lower)? Or say “one analyze” and retire the extra name? Same picture either way — do not grow a second pipeline.
5. **If `session.Lower` the method stays.** Read-only accessor of analyze output, or delete-as-door later? Not eng now; name honesty only.
6. **What “accept this revised plan” unlocks.** Next talk is still slice greenlight, **A then B first** (one real tree, no execute-time lower) — **after** accept. Still one slice at a time. Still Eng WIP = 0 until that greenlight. Item 5 PARKED. PR 73 PARKED. DEI not deleted. Accepting is **not** CURRENT.

---

## Locks

- **Not CURRENT.** PIPELINE-STATUS stays `(none)`.
- **Item 5 PARKED.** **PR 73 PARKED.**
- **No implement / no delete** from this file.
- **No slice greenlight** until Scot accepts **this** revised plan, then still per-slice greenlight.
- **Product sim = Interpreter on the real trees analyze produced.**

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule. Stage “3 Lower after 2′” is historical; this file is SoT for the fold-in. |
| [`session-lower-abstractions-2026-09-21.md`](session-lower-abstractions-2026-09-21.md) | SoT for named concepts. Does not accept this plan. |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices A–E (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
