# session.Lower — plan (for Scot)

**Date:** 2026-09-21
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change `simple-agent-tasks/PIPELINE-STATUS.md`.
**Slices:** A–E stay **unapproved** until Scot **accepts this Lower plan**. This supersedes immediate slice greenlight. Eng WIP = **0**.
**Parked (locked):** store-vs-lower **Item 5** Occupancy / `BusySections`. Still PARKED. Do not unpark.
**This file does not** implement C#, delete product code, or start a slice.
**Audience:** Scot
**Grounding:** tip `23ef8554` on `ontology/talk-alignment-2026-09-21` · [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) · [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md)

**Product rule (already locked there):** simulation = **Interpreter** on the real `session.Lower` tree. Artifact producers = host call-sites, not sim. DEI / Effect-IR are not product sim.

---

## 1) What `session.Lower` is

The **compile** step inside **Session Compile**.

`DomainSession` has already loaded libraries and run **one** analyze. If that analyze is clean, **`session.Lower`** turns the analyzed result into **one operation module**: a complete generic Syntax tree per shipped action, policy, subscription, create, and transition.

It is not execute. It is not print. It is not MCP. It is not DEI. Those consume the module after Lower (or, for producers, emit files that **call** it).

---

## 2) Inputs

Only after **clean analyze** (fail-closed gate):

- **Facts** — the `Domain` the author wrote (types, stages, operations, relationships, `uses` ids).
- **Bags** — analysis metadata on those authoring nodes (catalog, storage, HTTP, dispatch, …). Lower **reads** bags. It does not leave bag types in the output tree.

Dirty analyze → **no Lower**. Authoring IR (`DomainExpression`, `Effect`) is **parse output**, not Lower input-to-execute.

---

## 3) Output

**One** Syntax **operation module**.

- Generic trees (ordinary assignments, `Invoke`, BCL members, Store jobs the tree already names).
- **No bags in the tree.**
- Same module for **Interpreter** and for C# print. No consumer-specific lowering flag. No twin tree.

Host files are **not** this output. Library **artifact producers** emit those from **surface bags** after the same clean analyze. They **call** the module. They are not a second Lower, and they are not simulate.

---

## 4) Fail-closed rules

Keep these tight:

| If | Then |
|----|------|
| Analyze is dirty | **STOP.** No Lower. No host files. No execute invent. |
| A shipped construct cannot lower to a complete legal tree | **Fail.** Do not ship `Comment`, `null`, or a host-escape as meaning. Shrink the language rather than fake it. |
| Lower is incomplete (missing body, leftover authoring IR as execute input) | **Fail.** Do not let Interpreter / print / a door “finish” lowering. |
| A door or producer needs an operation that did not lower, or a bag that was never published | **Fail closed** at that consumer — they do not invent the op. |
| Interpreter and print disagree | Bug is in **Lower** (one tree). Not Store, MCP, DEI, or a new flag. |

Unknown / duplicate `uses` fail at **Load**, before Lower. That gate stays.

---

## 5) How Interpreter sim binds that module

**Simulate** = **Interpreter** executes the **real** lowered Syntax tree.

Bind is consumer-side:

- Caller supplies **`This`** / **Store** as the directory jobs the module already names (`Create`, `CreateIn`, `EnsureUnique`, …). Bind is not a DI container inside the VM, and it is not a second meaning path.
- C# print projects the **same** tree. Generated types, when used, **call** that module.
- MCP may **ask** Interpreter to run a named op with caller-supplied context. That ask is sim only if it is this Interpreter run.

Not sim: DEI walks, Effect-IR walks, scratch fake execute, producer emit, host `Stay.Create` as if it were the operation.

---

## 6) What must die

Honesty, not a delete-tomorrow list. This note does not delete code.

| Die as meaning / as sim | What that means |
|-------------------------|-----------------|
| **Execute-time `LowerActionBody` residual** | Nested StageTransition flush / Domain-null standalone (and leftover per-call policy lower) belong **in `session.Lower`**. Execute never lowers. |
| **`UseThisReference` twin / sibling trees** | One cached module body. Interpreter and print do not keep a runtime-shaped tree beside an emit `this` tree. |
| **DEI as simulate / proof** | Label: not-sim, not-proof, harness bind at most. **Do not delete DEI from this file.** |
| **Consumer lowering flags** | Fix Lower once. Do not grow a sibling flag to keep one consumer green. |
| **Producers-as-sim** | Host call-site delivery only. |

Does **not** die here: Ontology facts, `.poly`, session/libraries, evolution, fact-publishing analysis, Item 5.

---

## 7) Open design questions (Scot)

Real choices. Not fake options. Accepting this plan still does **not** admit CURRENT, unpark Item 5, or start eng.

1. **Residual inventory.** Path names nested StageTransition flush and Domain-null standalone. Is per-call policy lower the rest of the list, or is there more execute-time lower to find before A/B are even proposable as slices?
2. **One module cache.** Lower once per session revision; Interpreter and print share that cache. Re-Lower on domain/session change — never per invoke. Agree?
3. **Producers vs Lower order.** Both run only after clean analyze. Should host emit also **fail closed if Lower failed** (no call-site for an op that did not lower), or is the analyze gate enough and doors fail later?
4. **`This` bind without DEI-as-sim.** Interpreter needs a bound directory. What is the honest bind for product sim (caller-supplied Store/`This` on the module) vs leftover DEI harness smoke — without pretending DEI is the run?
5. **What “accept this plan” unlocks.** Next talk is slice greenlight, **A then B first** (one real tree). Still one slice at a time. Still Eng WIP = 0 until that greenlight. Item 5 stays PARKED. DEI stays not-deleted. Accepting is **not** CURRENT and **not** a license to mill C–E.

---

## Locks

- **Not CURRENT.** PIPELINE-STATUS stays `(none)`.
- **Item 5 PARKED.**
- **No implement / no delete** from this file.
- **No slice greenlight** until Scot accepts this plan, then still per-slice greenlight.
- **Product sim = Interpreter on the `session.Lower` tree.**

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`product-pipeline-first-principles-2026-09-20.md`](product-pipeline-first-principles-2026-09-20.md) | Pipeline + sim rule |
| [`domain-modeling-abstraction-path-2026-09-21.md`](domain-modeling-abstraction-path-2026-09-21.md) | Session Compile + slices A–E (unapproved) |
| `simple-agent-tasks/PIPELINE-STATUS.md` | Sole CURRENT/DONE — leave it |
