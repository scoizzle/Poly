# Product pipeline — first principles (for Scot)

**Date:** 2026-09-20
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).
**SHA:** `934b2409` (merge of [PR 71](https://github.com/scoizzle/Poly/pull/71); tip includes PR 68–71 analysis FailFast / snapshot / `HasErrors`)
**Open PR out of scope:** [PR 72](https://github.com/scoizzle/Poly/pull/72) `refactor/domainmodeling-core-library-seams` — library extract (Temporal out of the core list, registration-order schedule). **Do not block. Do not review. Do not rebase this note onto it.**
**Parked (locked):** store-vs-lower **Item 5** Occupancy / `BusySections`. Still PARKED. Not a candidate in this note. Do not unpark. Do not mill Hotel occupancy in DEI.
**Audience:** Scot
**North star:** a domain is a library of legal operations that lowers to one Syntax module; the VM is how those programs mean what they mean.
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / hard lines · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`](../decisions/2026-09-05-lowered-module-is-domain-meaning.md) · [`docs/decisions/2026-09-03-facts-concerns-bags-store-bind.md`](../decisions/2026-09-03-facts-concerns-bags-store-bind.md) · [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../decisions/2026-08-15-domain-library-extensions-mcp-harness.md) · [`docs/decisions/2026-06-08-vm-as-canonical-semantics.md`](../decisions/2026-06-08-vm-as-canonical-semantics.md)

This note does not implement C#, does not delete code, does not change PIPELINE-STATUS, and is not a CURRENT suite. It does **not** choose salvage / cut / reset of DomainModeling.

---

## What this is

A plain-English map of the **product pipeline** — the path a domain takes from authoring to something that actually runs or prints.

It is not a keep/kill ranking. It is not a suite. It does not pick how to mill DomainModeling. Those are other conversations. This one answers: **what is the product, and where does meaning live?**

---

## The product in one paragraph

You write a **domain**: the types, stages, actions, policies, and relationships that say what is legal. Poly does not turn that into a `Main` or a process. It turns it into a **library of named operations**. Each shipped operation becomes a complete program in the Syntax AST. Analysis hangs derived facts (bags) on the authoring nodes. Lowering **reads** those bags and emits **one** operation module — trees with no bag types in them. The **VM** is the canonical executor of those trees. C# print is a projection of the same trees. Scratch Store and `DomainEntityInstance` bind a directory so you can simulate. MCP is how an agent authors and pokes a named operation with caller-supplied context. None of those consumers are the domain.

If simulate and print disagree, the bug is in **lowering** (one tree), not in the store, the MCP walk, or a new consumer flag.

---

## The pipeline (left to right)

```text
Domain (facts + uses ids)
  → DomainSession (libraries the domain named)
  → session.Analyze  → bags on nodes, replacements (tree stays immutable)
  → session.Lower    → one operation module (generic Syntax; tree has no bags)
                     and surface bags → host artifacts (persistence / HTTP / later CLI)
  → consumers bind: execute (VM) | print (C#) | doors (routes that call the module)
MCP: harness over cataloged operations + session instances — not a product door
```

Named stages (same picture, from the 2026-09-04 transformation — **executed as P1–P6, still not CURRENT**):

| Stage | In English | Product |
|-------|------------|---------|
| **0 Parse** | Read `.poly` into facts | `Domain` |
| **1 Load** | Honor `uses` ids | analyzers, maps, artifact contributors on the session |
| **2 Analyze** | Derive views; do not rebuild catalogs later | concern **bags** on nodes |
| **3 Lower** | Compile each shipped operation | **one** operation module |
| **4 Check** | Analysis of that module, fail closed | Interpretation `AnalysisResult` |
| **5 Consume** | Bind; do not re-lower | (a) VM execute (b) print the module (c) host files from **surface bags** |

Later stages do not redo earlier ones. Hosts are replaceable. The architecture is AST / Node / Analysis plus that one module.

---

## First principles

### 1. Facts → bags → `session.Lower` / one tree

Use these words. Do not invent a framework catalog.

| Word | Meaning |
|------|---------|
| **Facts** | `Domain` — types, relationships, operations, `uses` ids |
| **Bag** | Analysis metadata for a concern (catalog, storage mapping, HTTP, dispatch, …) |
| **Surface** | Opt-in door that *selects* an implementation (`uses sqlite`, `uses http`) |
| **Lower** | Facts + bags → generic Syntax. The **process** may read bags. The **tree** has none. |
| **Module** | The product of `session.Lower`: types + operation bodies as Nodes |
| **Store** | Named collaborator the tree already invokes (`Create`, `CreateIn`, `EnsureUnique`, …) |
| **Bind** | Host supplies that collaborator. Caller-supplied. Not a DI container in the VM. |

`Domain` holds what the author said. Analysis publishes what consumers need to know (bags). Lowering is the compiler: it consults bags, then emits ordinary Syntax. After that, the running program does not still know the domain model. Simulate and C# print consume **that module**. Host files (DbContext, `Program.cs`) consume **surface bags** because they *are* the bound implementations — they call the module; they do not rewrite operations.

Two products from one analyze, still: the operation module, and host artifacts. Not two pipelines.

When execute and emit disagree, **fix lowering**. Do not add a consumer-specific lowering flag. `LowerStageTransitions` is gone; do not grow a sibling (`UseThisReference` as a second module is that same mistake, cached).

### 2. Legal operations, not a process

A domain is business logic: entities, stages, actions, policies, subscriptions. That is a **library of legal operations**, not an app with a known `Main`.

- **Legal:** what the domain forbids is in the operation tree (guards), not only in a later factory the export might skip.
- **No required entry point.** Capability / catalog is the menu. Core seed does not emit `Program.cs`.
- **Doors are opt-in.** REST appears if `uses http` is loaded. CLI flags seed ids; they do not invent a host.
- A door **maps** already-lowered operations onto routes (or equivalent). If the operation did not lower, the door fails closed. It does not complete missing lowering in strings.

Interpretation runs **named operations** (and known algorithms on the same AST → VM spine). It is not “run the domain.”

### 3. Shipped ⊆ AST

A construct ships only if it lowers to a **complete, legal, generic** Syntax tree and program analysis of that tree is clean.

- **Complete:** no `Comment`, no `null` from lowering, no host tree-walk beside the VM, as *shipped meaning*.
- **Generic:** no domain VM opcodes. Stage, notify, invoke, create, unique, clocks are ordinary Syntax (assignments, `Invoke`, BCL members, Store jobs on the tree).
- **Fail closed** if it cannot lower. Gaps stay in `docs/plans/`, not in the parser or the DSL guide.
- **Shrink the language** if the next construct cannot lower. Do not ship a keyword whose implementation is a harness escape.

Authoring IR (`DomainExpression`, `Effect`) can remain **parse output**. It must not be **execution input**. Residual execute-time `LowerActionBody` (nested transition flush, Domain-null standalone) and per-call policy lower are **debt** — they belong in `session.Lower`.

### 4. VM is canonical

The VM is the ground truth for what a Syntax program does. C# print is a projection. LINQ is a same-tree checker, not a second language. There is no product-path primitive IR beside the AST.

`Interpreter.Compile` is the one compile door (fail-closed on analysis errors). DomainModeling lowers **into** that language. New meaning goes: lower to existing nodes, analyze, and/or **replace nodes** — not an emitter patch, ABI one-off, or host-only walk.

A construct “works” when:

1. It lowers into the module (fail closed if it cannot).
2. The **same** body executes (VM on bound `This`, or the generated CLR method).
3. Print of that body matches execute (no consumer-only flag, no Effect-IR walk as shipped meaning).

Green compile of generated C# is not execute proof. Green DEI is not T2.

### 5. Store / DEI are consumers, not proof

Scratch `DomainInstanceStore` and `DomainEntityInstance` (dictionary `This` + Store) are the **current** simulate bind. C# `Stay.Create` / `CreateNav` are the **current** host bind of Create inside generated factories. Store job names on `This` are how that dictionary calls the directory.

Frozen ADR lists them as **current consumers**, not architecture. Replacing them is a planned slice through frozen seams after the module is complete. Deleting DEI is **not** this note (and is a frozen-ADR non-goal of the 2026-09-05 lock).

What they are not:

- not a second interpreter
- not the customer API
- not T2 / product-surface proof
- not a license to grow MCP `link_instances` the module does not name
- not a place to paper over a missing tree (Hotel occupancy in DEI, Unique-only bag restore as atomicity, Effect-IR at execute)

University / CRM / Hotel DEI dogfood stays valid **harness smoke**. Label it that way. Product-surface tests construct and call generated types, or execute cached module bodies on a bound directory.

Store **hosts state**. It implements directory jobs the module already names. It does not replace ontology → operation-tree. It does not own occupancy, require, sequential Failure, or policy bodies.

### 6. MCP is the harness

Poly.MCP is how agents **use Poly** in a conversation. It holds a `DomainSession` + revision + scratch store. It authors (`apply_dsl` / evolve), inspects (catalog, diagnostics), and **simulates** a named policy or action on a store instance (`create_instance` then `evaluate_policy(instanceId)` / `invoke_action`).

It is not the domain session’s architecture. It is not a product entry-point extension. It is not the customer API. Tool `Description` text is usage (call / pass / result), not Interpreter, AST, or store types.

Simulate must run the **lowered program** with a bound Store. The agent names the operation and supplies context. MCP does not invent CRUD, infer `Main`, or treat `Comment` / host-only effect dispatch as success.

Do not grow MCP theater (new simulate tools, more instance-store semantics) as the place domain meaning “actually happens.”

---

## Item 5 — PARKED

**Occupancy / `BusySections` stays PARKED.**

Hotel `Occupied` last-writer and `Hall.BusySections` counters are **authoring patterns** (subscriptions + invoke), not directory jobs and not a DomainModeling cut. Shipped ⊆ lowerable: do not invent occupancy keywords whose implementation is a harness escape. Do not add `Occupied` / `BusySections` as Store ABI. Do not rewrite Hotel occupancy in DEI to match last-writer export. Hotel probe = harness smoke.

This lock is repeated here so agents do not “find” Occupied as a pipeline gap and mill it.

---

## PR 72 — out of scope

[PR 72](https://github.com/scoizzle/Poly/pull/72) `refactor/domainmodeling-core-library-seams` extracts libraries from the core pipeline (Temporal off the core list, registration-order schedule honesty).

This note does **not** review, merge-block, rebase, or rewrite files it touches. Library load is already the frozen session seam (`uses` ids, unknown/duplicate fail closed). Let PR 72 merge or park on its own.

**Do not hold PR 72** for Occupancy, DEI deletion, `Stay.Create`, a DomainModeling salvage/cut/reset, or CURRENT admission.

---

## What this note does not decide

| Temptation | This note |
|------------|-----------|
| **Salvage / cut / reset** DomainModeling | **Does not choose.** First principles of the pipeline, not a keep/kill of the front-end. Write a separate note if that call is needed. |
| Admit PIPELINE-STATUS **CURRENT** | **No.** CURRENT stays `(none)`. This file is not a suite. |
| Implement C# / lower residual / bind EF | **No.** Consult only. |
| Delete DEI, `Stay.Create`, Runtime, Ontology, Language | **No.** Do not delete from this file. Frozen ADR already says DEI deletion is a non-goal of the module-meaning lock. |
| Unpark Item 5 | **No.** Occupancy / `BusySections` stays PARKED. |
| Block or rebase onto PR 72 | **No.** Out of scope. |
| Treat scratch store, `Stay.Create`, Store job names, HTTP Minimal API, or virtual actors as frozen | **No.** Current consumers. Compose them; do not freeze them; do not grow a sibling path to keep one of them working. |
| Grow a second pipeline so a consumer stays green | **Forbidden.** Dual-path runtime vs export is a bug, not a host-bind footnote. |

---

## How to tell the pipeline is true

A reader can say, without flags:

1. Analyze once.
2. Lower once to a module (`session.Lower`).
3. Check that module.
4. Hand the **same** module to simulate **or** C# print.
5. Hand surface bags to host files that **call** the module, not rewrite it.

That is the pipeline that matches frozen core. Everything else is a consumer.

---

## Related (do not execute from here)

| Doc | Role |
|-----|------|
| [`docs/CORE.md`](../CORE.md) §0 | Frozen pipeline; current machinery in §3 |
| [`pipeline-transformation-2026-09-04.md`](pipeline-transformation-2026-09-04.md) | Named stages. P1–P6 executed. **Not CURRENT.** |
| [`ontology-pr51-pipeline-alignment-2026-09-04.md`](ontology-pr51-pipeline-alignment-2026-09-04.md) | Dual-path as cached product — diagnosis, not a suite |
| [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) | Sole CURRENT/DONE. Leave it. |

---

## Non-goals of this note

- Implementing C#, deleting code, or reviewing PR 72 to merge.
- Admitting PIPELINE-STATUS CURRENT (or a second CURRENT line).
- Choosing salvage, partial cut, or hard reset of DomainModeling.
- Unparking Occupancy / `BusySections`.
- Treating scratch store, `Stay.Create`, or Store job names as frozen.
- Rewriting Hotel occupancy in DEI.
- A suite README for this note.
