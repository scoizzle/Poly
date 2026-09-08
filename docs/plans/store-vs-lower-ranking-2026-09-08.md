# Store vs Lower ranking (backlog 1–6)

**Date:** 2026-09-08
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite. Do not change [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md).
**SHA:** `d05fee3d` (merge of PR 63 on this branch)
**Open PRs in scope:** [PR 62](https://github.com/scoizzle/Poly/pull/62) (review) · [PR 64](https://github.com/scoizzle/Poly/pull/64) (100× mill)
**Merged in scope:** [PR 61](https://github.com/scoizzle/Poly/pull/61) · [PR 63](https://github.com/scoizzle/Poly/pull/63)
**North star:** deterministic agent codegen from an ontology of ontology systems. Dual-path runtime vs export is a **bug**, not a host-bind footnote.
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / hard lines · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`](../decisions/2026-09-05-lowered-module-is-domain-meaning.md) · [`docs/plans/pipeline-transformation-2026-09-04.md`](pipeline-transformation-2026-09-04.md)

This note does not implement C#, does not change PIPELINE-STATUS, and is not a CURRENT suite.

---

## Hard line (non-negotiable)

**`session.Lower` is domain meaning.** Stage 3 produces complete operation trees. Stage 5a (simulate) and 5b (C# print) consume **those trees**. When they disagree, **fix lowering** — one tree.

**Store hosts state.** Scratch `DomainInstanceStore` / later SQLite / later EF implement directory jobs the module already names (`Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique` / `ExistsRelated` / `Notify`). Store does **not** replace ontology → operation-tree. Store does **not** own occupancy, require, sequential Failure, or policy bodies.

`DomainEntityInstance` is scratch bind (dictionary `This` + Store) for MCP/authoring. It is **not** the customer API and **not** T2 proof. Hotel DEI walks are **harness smoke**, not product-surface proof. Policy: ADR 2026-09-05.

Buckets used below:

| Bucket | Meaning |
|--------|---------|
| **LOWER** | Domain meaning belongs in `session.Lower` (complete Syntax). Simulate + print share that tree. |
| **STORE** | Directory / graph / uniqueness / persist — host bind of jobs the module already names. |
| **DSL** | Spell / keyword / authoring form. Ships only if it can lower. Park if it cannot. |
| **DEFER** | Real, but not this IC stream. Do not mill. |

---

## Ranked table

Rank is **where to spend (or stop spending) IC**, not chronology. Items 1 and 3 are done; they stay in the table so agents do not reopen them as host work.

| Rank | Item | Bucket | Status | Why | Stop |
|------|------|--------|--------|-----|------|
| **1 (stop mill)** | **4.** Sequential Failure ≠ keep prior assigns | **LOWER** | PR 64 open; 100× mill on DEI `RestoreActionState` | Failure-without-prior-mutate is already mixed if+create **in the tree** (guarded `ProbeCreate` + body; CORE: prior assigns not applied). Sequential assign-then-fail is the same product claim. PR 64 snapshots the DEI bag and restores on any `ExecuteEffectList` Failure — harness compensation, Unique-only restore grown into a second atomicity interpreter. Generated C# of sequential assigns still keeps them unless the **module** probes first / does not partial-commit. ADR: when simulate and emit disagree, fix lowering. | **Stop milling DEI restore as product meaning.** One tree: fail-before-mutate (probe prefix or no partial commit); simulate **and** print of that body agree; drop Unique-only restore as shipped meaning. `IfOnMutatedProperty` is a tree order/probe question, not a bag snapshot. |
| **2** | **2.** Public `Create` wires inverses / subscription registries | **STORE** | PR 62 in review (hot-wire feel is correct) | Graph wiring and `WhenEach*` registries are **Store bind of Create**, not new operations. ADR 2026-09-03: uniqueness and graph wiring belong on Store; constraint checks may stay on the factory. PR 62 patches `DomainToCSharpExporter` `Type.Create` → `Attach*` so generated CheckIn can see Occupied — print-host theater in the same family as `Stay.Create` / `CreateNav`. The module already names `Create`; the factory is a consumer. | Merge only if the C# factory **calls the same Create job** simulate binds (unique inverse attach is one Store contract). Do **not** add `Attach*` as operation-tree meaning. Do **not** mill more exporter hot-wire. Do **not** grow MCP `link_instances` as the product path the module does not name. |
| **3 (done)** | **3.** Subs / transition batches / `EvaluatePolicy` at Lower | **LOWER** | PR 63 merged | ADR 2026-09-05 residual: those bodies belong in `session.Lower` / `GetOrLower`, not execute-time `LowerActionBody`. Domain-bound hot path now binds cached trees. | Do not reopen execute-time `LowerActionBody` on the Domain-bound hot path. CORE §3.4 still says “subscriptions and transition batches still lower at execute time” — **doc lag**, not a new slice. |
| **4 (done)** | **1.** Path-prefix require → `DomainResult.Failure` | **LOWER** | PR 61 merged | Unlinked singular path-prefix under `require` is operation meaning: module `DomainResult.Failure` (`requires a linked 'rel'`), not coalesce-throw. One tree: actions emit Failure guards; print is Failure, not `?? throw`. | Do not reopen. Do not restore `GetRelatedOne` throw as shipped require. Bare `evaluate_policy` throw-on-unlinked is harness, not a new Lower flag. |
| **5 (queued)** | **6.** Constraint lower on assign / optional length | **LOWER** (optional length = **DSL** spelling of the same family) | Queued | Unique assign already lowers (`EnsureUnique` then assign). `required` / `range` / `pattern` / `length` on assign still live on the entity factory / create guards (L8). That is honest **until** assign-time checks are shipped meaning — then they must be in the module so simulate+print agree. Optional `length` (`Guest.Phone: Text length(7, 20)` without `required`) is null vs empty vs skip-check **spell**, not a Store job. | Queue **after** sequential-Failure is a tree. If it ships, it lowers. Do not grow factory-only assign checks as shipped meaning. Do not add an “optional length” keyword until the assign tree exists. |
| **6 (parked)** | **5.** Occupancy / `BusySections` DSL | **DSL** | Parked | Hotel `Occupied` last-writer + `Hall.BusySections` counter are **authoring patterns** (subscriptions + invoke), not directory jobs. ADR 2026-09-05 non-goal: rewriting Hotel occupancy in DEI to match export. Shipped ⊆ lowerable: do not invent occupancy keywords whose implementation is a harness escape. | Parked. Do not add `Occupied` / `BusySections` as Store ABI. Do not mill Hotel DEI until generated types (or cached module bodies) are the oracle. Hotel probe = harness smoke. |

### Host-adjacent findings (not backlog 1–6, same ranking)

| Rank | Finding | Bucket | Why | Stop |
|------|---------|--------|-----|------|
| **do not admit** | Dict + SQLite host ([`dict-sqlite-host-2026-08-30.md`](dict-sqlite-host-2026-08-30.md)) | **STORE** / **DEFER** | Plan’s own diagnosis still holds: simulation cost is a **second product**, not missing SQLite. Target ABI (`Insert` / `Link` / `Unlink`) is **stale** vs shipped Store jobs (`Create` / `CreateIn` / `EnsureUnique`). P2 “one action program” largely landed via pipeline P1–P6 + PR 57/63. `rg SyncFromCache` in `*.cs` is already empty — do not reopen a six-phase rewrite to chase a stop that is not the ontology bug. | Do **not** admit as CURRENT. Do **not** rename jobs to `Insert`+`Link`. Do **not** delete `DomainEntityInstance` in the same campaign. Do **not** put SQL in Interpretation. Two slices then reassess was the bet; do not start a third under a new name. |
| **later host** | `Stay.Create` / `CreateNav` emit-bind | **STORE** | Same family as item 2: C# factories wrap a different bind of the same job names. Frozen ADR lists them as *current consumers*, not architecture. | After Store owns inverse attach (item 2 stop). Bind an EF/SQLite Store; do not add a consumer lowering flag. |
| **later host** | `EvaluateDefaultValue` create-time `now`/`today`/`guid` | **LOWER** leftover on bag fill | P6 removed preprocess-to-literal from operation trees; create-time defaults still host-eval `DomainExpression` on `DomainEntityInstance.Create`. | Not next. Same-tree clocks at construction, or document bag-fill as host (not operation meaning). |
| **later** | Stage 5c HTTP / `.http` Domain walk | **DEFER** | Fail-closed is a **name** check; generators still re-derive from `Domain` + bags. Doors must not invent operations. | After the module is the simulate+print oracle. Not this ranking’s IC. |
| **later** | Unbound `RuntimeAnalysisCache` core-catalog fallback | **DEFER** | Vendor maps drop until `Analyze` binds. | Bound session is the product door. Not a Store rewrite. |
| **never this stream** | Virtual actors, `Insert`+`Link` as frozen names, MCP as customer API, consumer lowering flags | **DEFER** | Frozen ADR forbidden growth. | Do not. |

---

## What NOT to burn IC time on

1. **PR 64 100× mill** of `RestoreActionState` as product atomicity. That is DEI wrapping a tree that still sequential-mutates. The mixed if+create probe already shows the right layer.
2. **PR 62 further exporter `Attach*` hot-wire** once the factory diverges from Store. Graph wiring is a Store job; more print special cases make fake implementation cheaper than finishing Create bind.
3. **Dict-sqlite campaign** (SQLite `:memory:` as the architecture, `Insert`/`Link` rename, deleting DEI, unifying emit onto SQL). The 2026-08-30 plan said two slices then stop; ABI is stale; simulation dual-path is a Lower bug.
4. **Occupancy / `BusySections` keywords** or rewriting Hotel occupancy in DEI to match last-writer export. Wrong layer. Probe stays harness smoke.
5. **Reopening PR 61 / 63.** Path-prefix Failure and populate-at-Lower landed. Doc lag in CORE §3.4 is a one-line CORE fix in some later change, not a suite.
6. **Factory-only assign constraints** (item 6) as shipped meaning before they lower. Optional-length spell without a tree.
7. **MCP theater:** new simulate tools, `link_instances` the module does not name, Hotel DEI as T2 proof, `*_Export_Compiles` as execute proof.
8. **Consumer-specific lowering flags**, Effect-IR as shipped meaning, `Comment` / `null` lower, domain VM opcodes, `Main` in core.
9. **Admitting this note (or dict-sqlite, or occupancy) as PIPELINE-STATUS CURRENT.**
10. **Stage 5c HTTP Domain walk, `uses cli`, EF codegen, mut-safety, Grammar wrap-up** as this ranking’s work — THEN/PARKED/PULL elsewhere.

---

## Recommended next slices

### Next Lower slice

**Sequential Failure as one tree (item 4, not PR 64’s bag restore).**

Same shape as mixed if+create: probe / fail-closed **before** prior assigns commit, in the module `session.Lower` already produces. Simulate runs that body; print prints that body.

**Stop:** a sequential assign-then-illegal-create (and assign-then-nested-require-Failure) leaves the bag unchanged on **both** dictionary-`This` execute **and** generated CLR invoke of the same method node. No Unique-only branch. No DEI-only snapshot as shipped meaning.

Not this slice: PR 62 Attach*, occupancy DSL, constraint-on-assign, dict-sqlite, CURRENT admission.

If PR 64 must move at all: relabel the DEI restore as **harness** (test-only / scratch bind), and put the product oracle on the module body — otherwise close or park the PR until Lower exists.

### Next host slice

**Store-owned unique-inverse attach on `Create` (honest kernel of item 2 / PR 62).**

One Store job: public Create wires the unique inverse collection and subscription registries, or fail-closed on ambiguous inverses. Dictionary bind and C# factory **call that job**. No new operation names. No `Insert`+`Link`. No DEI deletion.

**Stop:** generated `Type.Create` does not invent an `Attach*` protocol the module does not name; simulate Create and print Create agree on inverse+registry side effects for one unambiguous pair (Hotel `Guest.reservations` / `Reservation.guest` is enough). Ambiguous inverse fails closed on both consumers.

Not this slice: dict-sqlite rewrite, `Stay.Create` deletion campaign, occupancy, sequential-Failure restore, CURRENT admission.

**If only one IC stream:** Lower sequential Failure first. Host Create inverse is real but is consumer bind; milling it while the tree still partial-commits is the attractor ADR 2026-09-05 forbids.

---

## Explicit locks (do not blur)

| Lock | Implication |
|------|-------------|
| **Store does not replace Lower** | Directory jobs implement named collaborators. Ontology → operation-tree stays stage 3. SQLite/EF/scratch are bind. |
| **Simulate + print share the same trees** | `session.Lower` module method bodies. Dictionary `This` is consumer bind, not a second lower. When they disagree, fix lowering. |
| **Hotel probe is harness smoke** | `Hotel_Runtime_*` / MCP walks are not product-surface proof. `Hotel_Export_Compiles` is print-only. Do not extend them as the C# API. |
| **This file is not CURRENT** | Consultant ranking. No suite admission. No second CURRENT line. |

---

## Dict-sqlite skim (so we do not re-litigate)

[`dict-sqlite-host-2026-08-30.md`](dict-sqlite-host-2026-08-30.md) is still a **proposal, not a suite**. Useful residue:

- Wanted: VM runs Syntax; `This` is a dictionary; Store is authority; host ABI is tiny. That matches frozen core **as bind**, not as a second pipeline.
- P1 “Store is the authority” (kill live-object dual-write) is the **only** host slice still coherent with item 2 — and only if the ABI stays shipped jobs (`Create` / `CreateIn` / `EnsureUnique`), not `Insert`/`Link`.
- P2 “one action program” is largely the pipeline transformation + PR 57/63. Do not rebuild it as SQLite work.
- Out of scope then and now: deleting DEI in the same campaign, forcing emit onto `Insert`/`Link`, SQL in Interpretation, domain VM opcodes.

Pipeline transformation out of scope already named this: SQLite `:memory:` / EF Store is consumer 5a bind; parked dict-sqlite ABI is stale. Item 2 is that bind, narrowly. It is not a license to start the 2026-08-30 rewrite.

---

## Non-goals of this note

- Implementing C# or reviewing PR 62/64 to merge.
- Admitting PIPELINE-STATUS CURRENT.
- Treating scratch store, `Stay.Create`, or Store job names as frozen.
- Rewriting Hotel occupancy in DEI.
- A second CURRENT or a suite README for this ranking.
