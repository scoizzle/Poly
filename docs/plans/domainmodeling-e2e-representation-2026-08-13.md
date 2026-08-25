# DomainModeling end-to-end representation

**Date:** 2026-08-13 (revised same day against [`fleet-eval-fixes-2026-08-12.md`](fleet-eval-fixes-2026-08-12.md))  
**Kind:** Parent plan (parked). Not CURRENT. Solidify a suite only when admitted.  
**Status:** Sequenced from the 2026-08-12 deep-research pass, then folded with fleet-eval probe findings where those findings are the same capability (author → analyze → runtime → generate).  
**Source:** workflow `deep-research` + `probes/findings/fleet-eval/` (coordinator + 15 slices).  
**Admission:** [`README.md`](README.md) · CURRENT truth [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md). Do **not** start production work from this doc while Agent pick is `(none)` or another suite. **Do not admit this plan and fleet-eval as two CURRENT queues of the same bugs.**

---

## Goal

Every **product** DomainModeling concept that we keep has one coherent path:

```text
author (apply_dsl / MCP add) → analyze → store/VM runtime → C# export / DslCompiler
```

Concepts that stay IR/evolution-only are named as such in the DSL guide and capability inventory. No second silent path.

**Customer outcome:** time-to-value (generated app actually runs), correctness (unique / policies / params / child APIs), operability (honest claims).

## Non-goals

- Completing the DomainModeling type catalog for its own sake.
- New grammar for IR nodes that already have a product spelling (`OwnedAccess`; bare identifiers for action params — see L3).
- Forking temporal authoring — that is [`p1-temporal-design-lock.md`](p1-temporal-design-lock.md) / `p1-*`. Decimal literals (`0.9`) are **not** temporal and are in this plan.
- Re-adding deleted 2026-08-10 effects (`Link`/`Unlink`/`DeleteEntityInstance`/`TransitionRelationship`).
- Splitting `DomainEntityInstance` / `DomainToCSharpExporter` (complexity pass #10).
- Admitting this as CURRENT alongside grammar wrap-up, mut-safety, p1, **or** a live fleet-eval suite that already owns the same files.
- MCP session ownership, parser stack-depth / unterminated-string hardening, tautological-test / JSON-assert / benchmark README hygiene — those stay in fleet-eval (see [§ Relationship](#relationship-to-fleet-eval)).

## Locks (do not re-litigate in a slice)

| # | Lock |
|---|------|
| L1 | Product authoring surface is `apply_dsl` / `export_dsl` + unified MCP `add`/`remove`. Evolution-only construction is not “represented.” |
| L2 | **`OwnedAccess` stays IR-only.** Product parse emits path-prefix → `RelationshipNavigation`. Do not invent a second nested-value-doc syntax. |
| L3 | **No distinct parameter DSL.** `DslExpressionParser.ParsePrimary` emits `PropertyAccess` for bare identifiers (verified). That is the product spelling. **Revised:** analysis, lowering, invoke bindings, and runtime **must** treat an in-scope action-parameter name as a parameter (normalize to `ParameterAccess` at parse **or** consult `paramEnv` on matching `PropertyAccess`). Leaving `ParameterAccess` as a dead IR that `paramEnv` never sees on the product path is a representation hole, not a lock. Action-arg injection into the instance bag does not license unknown keys or missing declared params (fleet P1-1). |
| L4 | **`DateOperation` authoring is p1**, not this plan. Generic `DueDate + 14` already lowers to CLR `AddDays` without building the node. When p1 lands, print must emit the pack form, not `Default()`. Date **parameter** arithmetic (`d + 30` where `d: Date`) is this plan (L3 + existing AddDays lowering), not p1. |
| L5 | Store-aware Q3′ (`any`/`all`/`none`/`count`) is the **supported eval surface today**. Export that throws is only acceptable if those policies are **not** prepended as action guards that make generated actions un-runnable. |
| L6 | `unique` as “storage metadata only” is an honesty claim, not a destination. This plan makes uniqueness real (store + Create + EF index) or the guide stays explicit until that slice ships. |
| L7 | `ValueType` and contract IR are **kept product roadmap** ([complexity pass](domainmodeling-complexity-pass-2026-08-10.md) #3/#6). Missing piece is authoring → analyze → export → runtime, not deletion. |
| L8 | Fail-closed. Empty uniqueness, missing matches, unknown invoke args, and invalid configs fail loud. Tests first; smallest production change; guide + CORE updated in the same change when the shipped surface moves. |
| L9 | Generation slices are **not done** while `scripts/run-probe.sh` compiles entities-only. Full-solution compile (entities + `Program.cs` + `DbContext`, 0 warnings) is the acceptance gate for slices 3–4 and entity-export. Fleet-eval P0-0 is that gate; do not invent a second probe runner. |
| L10 | Every shipped construct that `export_dsl` prints must parse again (`apply_dsl`). Printer comments and comma-vs-whitespace drift are bugs, not documentation. |

## Why this order

Research listed coverage gaps. Fleet-eval listed probe-proven breaks of the **same** path. Execution order is still **make the already-claimed surface true**, then **open remaining authoring** — with printer + parameter identity pulled forward because they falsify “authored and analyzed.”

```text
0 honesty
  → P printer / round-trip          (export_dsl must apply_dsl)
  → R parameter identity + invoke   (L3 revised; P1-1/P1-3/P2-1)
  → 1 uniqueness
  → 2 quantifier export / action-guard honesty
  → S subscription fidelity
  → 3 EF relationship + pack emit   (FK + enum/key/CHECK SQL)
  → 4 child-entity / host API
  → X entity-export remaining 🔴s   (create-in, for-invoke, reserved names, …)
  → 5 ValueType authoring
  → 6 contract authoring
```

p1 temporal stays on the existing pipeline (`grammar wrap-up → mut-safety → p1`). Slice 0 only stops claiming DateOperation as a silent hole if p1 is the owner.

Full-solution gate (L9 / fleet P0-0) lands **before or with** the first generation slice (3 / 4 / X), not before honesty or printer.

---

## Relationship to fleet-eval

[`fleet-eval-fixes-2026-08-12.md`](fleet-eval-fixes-2026-08-12.md) is the probe-finding execution checklist (IDs P0–P7, repro `.poly` paths). This doc is the representation sequence. Same bugs, two roles.

**Absorbed here** (agreed; work is named in slices below): P0-0 (as L9, not a second script), P1-1, P1-2, P1-3, P2-1…P2-10, P3-1…P3-8, P3-10…P3-20, P4-1…P4-5, P6-1…P6-4, P7-1, P7-4, P7-5, P7-6, P7-7.

**Agreed, stays in fleet-eval** (real, not a representation slice): P5 envelope soundness (null range bounds, unknown-writer CHECKs, binder-rooted `Eval`); P7-2 nesting-depth; P7-3 unterminated string; P7-8 policy-gate dedupe; P7-9…P7-11 test/benchmark hygiene.

**Not absorbed** (out of this plan’s goal): 12-F10 MCP session ownership; 05-F6 dead-store false error (needs its own product decision); 11-F3 VM truncation of non-long numerics (latent until the numeric surface widens); P3-9 implicit `[Range]` on arithmetic DTO params (🟡 — optional follow-on in slice 4, not a lock).

**Product decisions still open** (fleet §3; decide in the named slice, do not “fix silent”):

| Fleet | Decision owner |
|-------|----------------|
| 05-F5 entity-level policies gate every action (unsatisfiable pair rejects the entity) | Slice 2 |
| 07-F9 action named `Create` overloads the factory | Slice X / P3-10 reserved-name policy |

---

## Slice 0 — Honesty (docs + claim lock)

**Outcome:** Operators and agents stop treating IR-only / deleted / export-stubbed concepts as shipped.

**Do**

- Guide: keep L2 wording; rewrite L3 so agents do not treat `ParameterAccess` as a second syntax **or** as unused IR. Fix the store-dependent export bullet so it matches `DomainExpressionLoweringPass` (path-prefix / `Rel exists` now lower; **only** Q3′ still throws).
- Guide sweep (fleet P4-5): §8 invoke `any/all` + decimal example, §11 inline `enum(...)`, §0.4 `;` create-in, §6 dotted binder args, §0.3 DMEFF011 / to-one claims, §9 `unlink_instances` (MCP `link_instances` is the shipped linker; no Unlink Effect IR), duplicate-annotation last-wins vs “parse error,” agent-vs-product guide divergence.
- [`docs/interpretation/domain-execution-model.md`](../interpretation/domain-execution-model.md) and [`docs/domainmodeling-capability-inventory.md`](../domainmodeling-capability-inventory.md): remove `DeleteEntityInstance`, `Link`/`Unlink` Effect IR, `TransitionRelationship`. Linking existing instances = `store.Link` / MCP `link_instances` only.
- `Domain.cs` XML: stop listing an `Event` DomainType that does not exist.
- `CompileMode.All` XML: stop saying “not implemented” if `DslCompiler.GenerateAllFiles` already emits `Program.cs` + `demo.http`.
- Leftover delete-effect **grammar** pattern with no `ParseEffect` arm (fleet P7-1): delete the pattern or fail parse with a dedicated diagnostic (do not leave “Unhandled effect pattern”).
- Document or analysis-reject reserved expression words as nav names (`any`/`all`/`none`/`count`) — fleet P7-4.
- Capability inventory / execution-model: `DateOperation` authoring owned by p1; VM `AddDays` is not a gap.

**Files (expected):** `Poly.Mcp/Docs/poly-dsl-guide.md`; the two docs above; `Poly/DomainModeling/Domain.cs`; DslCompiler `CompileMode`; `Poly/Grammar` / `PolyDslParser` delete-pattern only if still present.

**Exit:** No shipped doc claims a deleted effect or an Event type. Guide Q3′ bullet matches the throw arms. Every corrected guide example is either a probe or a named test. No product behavior change except leftover grammar / reserved-nav decision.

**Not this slice:** New syntax. Runtime uniqueness. Export lowering. Printer paren/require fixes (slice P).

---

## Slice P — Printer round-trip

**Outcome:** `export_dsl` of shipped constructs is `apply_dsl`-able (L10).

**Today (fleet P4, verified printer `Not` arm):** `not (Total > 0)` prints `not Total > 0` (And/Or parenthesize; Not does not). Mixed `require A` + `require not B` prints `require A, not B`. Create-in prints `,` separators the parser rejects. `EqualityConstraint` prints `/* equals */`.

**Do**

1. Round-trip tests first: `not (…)` comparison; mixed require; multi-initializer `create in`; any live `EqualityConstraint`.
2. `ExpressionPrinter.Not` parenthesizes a non-atomic operand.
3. Printer splits positive vs negated `require` gates into parseable form.
4. Create-in initializers use the parser’s whitespace separators.
5. `EqualityConstraint` either emits a re-parseable form or is dropped from the product model (do not keep a comment-only print).

**Files:** `Poly/DomainModeling/Parsing/DomainDslPrinter.cs`; round-trip tests under `Poly.Tests/DomainModeling/Parsing/`.

**Exit:** The four repros parse after print. No new constraint kind.

**Not this slice:** DateOperation print (p1). Guide prose (slice 0).

---

## Slice R — Parameter identity and invoke-arg boundary

**Outcome:** An in-scope action parameter is a parameter on the product path. Unknown or mistyped invoke/create args fail closed.

**Today**

- Bare identifiers parse as `PropertyAccess` (`DslExpressionParser` ~154). `paramEnv` is consulted only for `ParameterAccess` → call-chain binding analysis is dead (fleet P2-1).
- `InvokeAction` writes every arg key into `_values` (verified ~386–404). Unknown keys clobber properties and bypass guards; missing declared params become `Missing.Value` (P1-1).
- `invoke invoice.Settle(amount: amount)` evaluates the callee binding with an entity-only provider; the caller param becomes the instance handle (P1-3).
- `assign DueDate to d + 30` (`d: Date`) passes analysis; AddDays lowering keys on `PropertyAccess` only → CS0019 (P2-4).
- MCP `create_instance({"Qty":"not-a-number"})` succeeds; `29.99` truncates to `29` (P1-2).

**Do**

1. Fail-closed tests: unknown invoke key rejected; missing declared param rejected; `{"Age":40}` cannot satisfy an `Age>=18` gate when stored Age is 15; `invoke Rel.Action(param: param)` passes the caller param value; `d + 30` on a Date param compiles; MCP wrong-typed create rejected; fractional Number preserved.
2. One implementation of L3: either emit `ParameterAccess` when the identifier is in the current action `paramEnv`, or treat matching `PropertyAccess` as a parameter everywhere `paramEnv` is consulted (`Eval`, `ValidateAssign`, `BuildPostconditionConstraints`, `ConstraintPropagationAnalyzer`, invoke-arg inference, AddDays lowering, `EvaluateParameterBindings`). Do not do both.
3. Runtime `InvokeAction`: accept only declared parameter names; require every declared param (DMEFF007-mirror).
4. MCP / instance JSON: coerce-or-reject against the target property/param CLR type (same types the export factories use).

**Also in this slice (expression type-check escapes — same fail-closed envelope, fleet P2-2…P2-10):**

| ID | Hole |
|----|------|
| P2-2 | `Unknown`-skip in `ExpressionTypeAnalyzer` (invoke-arg inference, if-condition, binder-root, unresolvable assign RHS) |
| P2-3 | `assign Qty to now` — runtime keywords type-checked on assign RHS |
| P2-5 | non-boolean `if (Qty)` rejected |
| P2-6 | bare enum member in action-body `if` agrees with policy resolution |
| P2-7 | decimal literals (`0.9`, `default(0.5)`) parse as Number — **not** deferred to p1 |
| P2-8 | `default(Bogus)` on enum property rejected |
| P2-9 | enum-member invoke args membership-checked and qualified |
| P2-10 | `null` only for reference/nullable targets |

One failing test per form. Repros: `probes/fleet-eval/03-analysis/`, `05-analysis/chainparam.poly`, `10-runtime/library.poly`, `11-vm/vm-crossinvoke.poly`.

**Files:** `DslExpressionParser.cs` **or** analysis `paramEnv` consumers (not both); `DomainEntityInstance.InvokeAction`; MCP create/invoke tools; `ExpressionTypeAnalyzer`; `DomainExpressionLoweringPass` AddDays arm; `poly-dsl-guide.md` L3 wording if slice 0 has not already landed it.

**Exit:** Product-path params participate in analysis and bindings. Unknown/missing/wrong-typed args fail at the runtime or MCP boundary. Decimal example in guide §8 parses.

**Not this slice:** Distinct `param` keyword. DatePack units / `Now`.

---

## Slice 1 — Uniqueness is an invariant

**Outcome:** `unique` rejects duplicates on the store path and in generated Create; secondary unique columns get an EF unique index.

**Today:** DSL + analysis + `StorageColumn.IsUnique`. Not enforced on `DomainEntityInstance` / `DomainInstanceStore`. Exporter skips unique in Create. `DbContextGenerator` never emits unique indexes for secondary columns.

**Do**

1. Failing tests: store `Create`/`assign` of a colliding unique value fails loud; export `Create` checks unique; `OnModelCreating` emits unique index for non-PK unique columns.
2. Enforce on write in the instance store (same fail-closed posture as `required` / `pattern`).
3. Emit the check in `DomainToCSharpExporter` Create factories (stop the skip at `:1052`).
4. Emit unique index from storage metadata in `DbContextGenerator` (do not invent a second uniqueness model).

**Files:** `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`; `DomainInstanceStore`; `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`; `src/Poly.DslCompiler/DbContextGenerator.cs`; `Poly.Mcp/Docs/poly-dsl-guide.md` (remove “storage-projection only” once true).

**Exit:** Guide no longer caveats unique. Three-layer: parse already accepts; analysis already flags; runtime + export + EF fail on collision.

**Not this slice:** Composite multi-column unique (only if a test proves the IR already has it — do not invent). SqlServer `nvarchar(max)` keys (slice 3 / P7-6).

---

## Slice 2 — Quantifier policies do not poison generated actions

**Outcome:** A domain with entity-level `any`/`all`/`none`/`count` policies either (a) exports runnable actions, or (b) refuses export/analysis of the unexportable combination. Vacuous `NotSupportedException` on every action is not a product outcome.

**Today:** Store preprocess rewrites Q3′ to literals. `DomainExpressionLoweringPass` throws if those nodes reach VM compile. C# export compiles Q3′ policy methods that throw. **Every entity-level policy is prepended as an action guard**, so those policies make exported actions un-runnable (fleet 12-F10 / `mcp-orders.poly`: `Pay` throws because `AllLinesShipped` is a guard).

**Do (pick one in the first failing test; do not do both)**

| Option | When | Work |
|--------|------|------|
| **A — Lower Q3′ in export** | Generated C# must evaluate collection policies against in-memory navs | Replace throw arms with LINQ/`Count` against the emitted navigation; stop requiring store for local collections |
| **B — Fail closed at export/analysis** | Collection policies stay store-only | Do **not** prepend store-only policies as action guards; export of a domain that *requires* those guards for action meaning fails loud (or omits the guard and documents that actions are unguarded in C#) |

Default recommendation: **A for policies whose quantifier target is an owned/in-graph navigation already on the entity**; **B for store-only graph walks** (unloaded `many` that is not a collection field). One test that names the case decides.

**Also decide 05-F5 here (do not silent-fix):** entity-level policies gating every action, including unsatisfiable pairs that reject the whole entity. Either keep (document as modeling) or move to per-action `require`. Same change as the guard-prepend question — do not invent a third rule.

Also: if Q3′ still must not reach the shared VM compiler, keep the throw there — do not silently change store preprocess.

**Files:** `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`; `DomainToCSharpExporter.cs`; action-guard emit site (same exporter); guide § shipped-surface boundaries.

**Exit:** At least one golden domain with `all Rel { … }` on an entity-level policy exports an action that runs without `NotSupportedException`, **or** analysis/export fails with a diagnostic that names the policy. No domain silently ships un-callable actions. 05-F5 written into the guide.

**Not this slice:** New quantifier grammar. Multi-hop Q4 aggregates. Envelope CHECK SQL (fleet P5).

---

## Slice S — Subscription fidelity

**Outcome:** Stage subscriptions that the model already authors run and export in the same order, and `create` inside a notify does not crash the store.

**Today (fleet P6)**

- `Store.Add(child)` during notify mutates `_instances` while `NotifyTransition` foreach-iterates it (`DomainInstanceStore` ~153) → `Collection was modified`.
- `when all orders Ready, Delivered` with the set spread across those stages never fires (runtime + export require one shared stage).
- Export fires entity-level handlers before stage-scoped; runtime and guide §7 are stage-first.
- Missing subscriber/peer property is a warning; DslCompiler surfaces only errors → late CS1061.

**Do**

1. Snapshot the notify sweep list (`_instances.ToArray()` or equivalent) before dispatch.
2. Evaluate `all` against the union of declared `StageNames` on runtime **and** export.
3. Export handler order matches runtime (stage-scoped, then entity-level).
4. Escalate missing subscriber/peer property to an authoring **error**.

**Files:** `Poly/DomainModeling/Runtime/DomainInstanceStore.cs`; subscription export in `DomainToCSharpExporter`; `SubscriptionAnalyzer`; guide §7. Repros: fleet `06-subscriptions/`, guide §0.4 create-on-notify.

**Exit:** Subscription-create through `create_instance → invoke_action` does not throw. Spread-set `all` fires. Export write-order matches runtime. Missing peer property fails at analysis.

**Not this slice:** Compound `when all reservations Reserved and payment Captured` (related-entity stage-gates research — not implemented IR).

---

## Slice 3 — Emit the relationship mapping we already analyzed

**Outcome:** Generated `OnModelCreating` configures relationships from `StorageAnalyzer` output, and pack column/CHECK SQL is valid for the declared provider.

**Today:** Storage analysis already builds FKs, reference navigations, and subscription lists. `DbContextGenerator` never emits relationship mapping. Packs also: enum `.HasColumnType("<EnumName>")` dies at `EnsureCreated` (P7-5); SqlServer natural keys as `nvarchar(max)` (P7-6); column names interpolated raw into CHECK SQL, including `--` comment injection (P7-7).

**Do**

1. Golden: one-to-many + many-to-one pair compiles and `OnModelCreating` contains the FK + nav configuration implied by storage metadata.
2. Drive emit **only** from existing storage IR. No parallel mapping types.
3. Owned navigations: match whatever storage already records; do not invent EF owned-type configuration unless storage already says so.
4. Enum columns: provider-valid store type (or drop `HasColumnType` for enums).
5. Key/unique text columns: bounded type honoring `length` (SqlServer).
6. CHECK / constraint SQL: provider-quoted identifiers; reject or quote names that would comment-out the CHECK.

**Files:** `src/Poly.DslCompiler/DbContextGenerator.cs`; pack column mappers; `Poly/DomainModeling/Analysis/StorageAnalyzer.cs` (read-only unless a real hole). Repros: `probes/fleet-eval/13-packs/{warehouse,booking,library}.poly`.

**Exit:** Generated DbContext maps every analyzed FK. `EnsureCreated` succeeds for sqlite/sqlserver/generic on the 13-packs probes. Missing storage metadata fails closed (no empty `HasOne` stubs).

**Depends on:** L9 / fleet P0-0 (entities-only gate cannot see these defects).

**Not this slice:** Unique indexes (slice 1). Minimal API routes (slice 4). RestApiSurface / StorageAccess packs.

---

## Slice 4 — Child-entity and host API generation

**Outcome:** Generated Minimal API matches the domain: parameterized child actions compile; create exists where the model can create; schema exists on every shipped host; child routes are not silently wrong.

**Today (broken)** — original research + fleet P3-1…P3-8

- Parameterized actions on **non-root** entities: handler has no `dto` parameter; shared body still reads `dto` (P3-1, CS0103).
- Parent+child both shadow-keyed → duplicate `id` (P3-2).
- To-one nav emitted as `.Collection()`; Doctor loses root CRUD (P3-3).
- Child detail ignores `{id}` — `FirstOrDefault()` (P3-4).
- CS8602 on child-detail back-ref `e.Warehouse.Code` (P3-5).
- Grandchildren get no CRUD; their actions float to root scope (P3-6).
- Seed `MakeSampleValue` ignores `pattern` → silent empty seed (P3-7).
- `demo.http` bodies fail emitted DTO `[Range]`/`[Required]`/`[RegularExpression]` (P3-8).
- Root entity with an entity-typed property: POST only returns `BadRequest`.
- Non-root entities: list/detail GET only — no create (unless parent `create in` is the authored path).
- `Program.cs` calls `Database.EnsureCreatedAsync` only for `DbmsPack.Sqlite`.

**Do**

1. Failing full-solution compiles of `probes/fleet-eval/09-transport/{warehouse,orders,clinic}.poly` (via P0-0), plus one test per remaining fact.
2. Shared action-body helper and handler signature stay in lockstep — parent-ctx branch in `AppendActionEndpointStatements` declares `{Action}Dto`.
3. Disambiguate child route key tokens; filter child detail by child key; branch `ReferenceNavigations` vs `CollectionNavigations`.
4. Grandchildren: route through the aggregate **or** fail loud at analysis. Do not emit root-scoped grandchild actions. Prefer fail-loud if the smaller path.
5. Create route policy: emit POST create when `Entity.Create` is generable; `BadRequest`-only POST is a bug. Non-roots only created via `create in Rel` stay without a standalone POST **if** the parent create-in path exists — document that.
6. Sample/seed values honor the same constraints the DTO emitter applies (`pattern`, range, required); seed failure is loud.
7. Schema create: every shipped DBMS pack that seeds also ensures schema (or fails startup loud).

**Optional follow-on (not required to close the slice):** P3-9 conservative `[Range]` on arithmetic-flow DTO params.

**Files:** `src/Poly.DslCompiler/MinimalApiGenerator.cs`; `HttpFileGenerator.cs`; pack seed/setup.

**Exit:** 09-transport probes compile 0/0 full-solution. Child parameterized action compiles. Matching `{id}` is returned. No `BadRequest`-only create for a creatable root. Non-SQLite host can start against an empty database.

**Depends on:** L9 / fleet P0-0.

**Not this slice:** PUT/DELETE. Entity-export CS* inside `DomainToCSharpExporter` (slice X).

---

## Slice X — Entity-export remaining breaks

**Outcome:** `export_domain_to_csharp` / entities mode of the 07/04/10/12 probes compiles and agrees with runtime/guide on the listed forms.

**Do** (one failing compile or behavior test per ID; fleet P3-10…P3-20)

| ID | Hole |
|----|------|
| P3-10 | Reserved generated names (`CurrentStage`, `DomainResult`, `Create`, C# keywords, `namespace`/`event`) rejected at analysis. Decide 07-F9 (`Create` action) here. |
| P3-11 | `for`-invoke inside `-> EntityType` lowers to `DomainResult<T>` or is rejected (CS0029). |
| P3-12 | Two `create` of the same type get distinct unwrap locals (CS0128). |
| P3-13 | Singular-nav subscription registration does not deref a nullable nav. |
| P3-14 | User policy named `not_X` is not stripped into the synthetic-negation gate (store a negated flag, do not mangle names). |
| P3-15 | `default("A")` on an enum param qualifies like assign/create-in. |
| P3-16 | `create in` on a **self-relationship** does not treat `IsBackReference` as “self-rel” (CS1503). |
| P3-17 | Backing-field name is one function (`aRefs` → `_aRefs`, not `_arefs`). |
| P3-18 | `create` / `create in` in entry effects rejected at authoring (guide §9: create is action-only). |
| P3-19 | `-> T` whose producer is a final `if` branch returns the created entity or is rejected (no `NotSupportedException` stub). |
| P3-20 | `create`/`create in` binding a defaulted prop + `default(now/today/guid)` does not call `LowerDefaultConstantNode` on a runtime keyword. |

**Files:** `DomainToCSharpExporter.cs`; `EffectLoweringPass.cs`; analysis reserved-name / entry-effect create; `EntityStructureAnalyzer` back-ref vs self-rel. Repros under `probes/fleet-eval/07-export/`, `04-analysis-pipeline/`, `10-runtime/`, `12-mcp/`.

**Exit:** Named probes compile 0/0 (entities, and full-solution where they emit Program). Runtime + export agree on `not_Paid` and entry-effect create rejection.

**Not this slice:** Q3′ policy methods (slice 2). Minimal API (slice 4).

---

## Slice 5 — ValueType authoring

**Outcome:** `value { }` is a product type: parse → print → MCP add/remove → analyze → export → runtime field use.

**Today:** `ValueType` + `AddValueTypeChange` exist. Parser rejects `value { }`. Printer walks only `EnumType` and `Entity`. MCP add has no value-type kind. No expression/effect lowering, no instance/store execution.

**Do**

1. Guide first: one vertical — named value type with properties, used as an entity field, assign + read.
2. Parse/print/MCP `add` kind in the same change as the first test.
3. Analysis: type ref resolves; constraints on value-type fields fail closed.
4. Runtime + export: store the value as the same shape export emits (prefer flattened properties or a nested record — pick from the **first working** path, extract only if a second use appears).

**Files:** `Poly/DomainModeling/Parsing/PolyDslParser.cs`; `DomainDslPrinter.cs`; MCP add/remove kinds; `ValueType.cs`; lowering/exporter; `poly-dsl-guide.md`.

**Exit:** Round-trip golden. Instance assign/read. Exported C# compiles with the field.

**Not this slice:** Contracts. Value-type inheritance. Treating `OwnedAccess` as the value-type authoring syntax (L2).

---

## Slice 6 — Contract authoring

**Outcome:** `ImportedContract` / `ContractBinding` can be authored and inspected on the DSL/MCP surface; analyzer diagnostics have a producer.

**Today:** Evolution applies into Domain fields. `ContractIntegrationAnalyzer` lints. No parse/print, no MCP add/remove kind.

**Do**

1. Design the **thinnest** DSL that matches existing records (`InternalDomain`/`ExternalProvider`, endpoint Operation|Event Inbound|Outbound, `ContractFieldMap`). Write it in the guide **before** parser work.
2. Parse/print/MCP add/remove kinds.
3. Wire existing analyzer to authored trees (add message tests — complexity pass #8).
4. Export/runtime only if a named consumer needs a generated adapter in this slice. Otherwise stop at author → analyze → inspect, and say so in the guide.

**Files:** `ImportedContract.cs`; `ContractBinding.cs`; evolution changes already present; parser/printer; MCP kinds; `ContractIntegrationAnalyzer.cs`; guide.

**Exit:** Round-trip + analyzer diagnostic test. No claim of generated provider clients unless that code ships.

**Not this slice:** New endpoint runtimes. External HTTP client framework.

---

## Out of this plan (already owned elsewhere)

| Item | Owner |
|------|--------|
| `Now - 12 days`, units, clock, DateOperation parse/print | `p1-*` / [p1-temporal-design-lock](p1-temporal-design-lock.md) |
| Session write lock / idempotent add | `mut-safety-*` |
| Grammar LeftAssoc / span-vs-fold | grammar wrap-up (PIPELINE-STATUS ADMIT) |
| Probe runner `--mode all`, warning-fail, 09/13 fixtures | [fleet-eval P0-0](fleet-eval-fixes-2026-08-12.md) (L9) |
| Range-envelope / unknown-writer CHECK / binder `Eval` | fleet-eval P5 |
| Parser nesting-depth, unterminated string, test/benchmark hygiene | fleet-eval P7-2/3/8–11 |
| MCP session ownership | fleet-eval deferred 12-F10 |
| Broader EF/API productization | [ef-and-api-codegen.md](ef-and-api-codegen.md) (this plan takes slices 3–4) |
| Related-entity **stage-of** reads | [related-entity-stage-gates-research-2026-08-11.md](related-entity-stage-gates-research-2026-08-11.md) — not implemented IR |
| Relationship.Stages / Relationship.Policies consumption | Confirm unreachable, then complexity pass #9 |

## Stale text that is **not** a work item

- `Link`/`Unlink`/`Delete` Effect IR — deleted 2026-08-10; slice 0 docs only.
- `EnumConstraint` type — does not exist; enum-typed properties are type refs.
- DateOperation VM execution — instance VM already runs `DateOnly`/`DateTime.AddDays`.
- `CompileMode.All` “not implemented” — slice 0 comment only.

---

## Implementation tasking (fleet)

Solidified 2026-08-13. **Handoff:** [`simple-agent-tasks/e2e-README.md`](simple-agent-tasks/e2e-README.md) — wave DAG, hot-file owners, one agent per slice.

| Slice README | Tasks |
|--------------|--------|
| [`e2e-0-README.md`](simple-agent-tasks/e2e-0-README.md) | 0-1…0-5 + gate |
| [`e2e-p-README.md`](simple-agent-tasks/e2e-p-README.md) | p-1…p-4 + gate |
| [`e2e-g0-README.md`](simple-agent-tasks/e2e-g0-README.md) | g0-1…g0-3 + gate |
| [`e2e-r-README.md`](simple-agent-tasks/e2e-r-README.md) | r-0…r-9 + gate |
| [`e2e-1-README.md`](simple-agent-tasks/e2e-1-README.md) | 1-1…1-3 + gate |
| [`e2e-s-README.md`](simple-agent-tasks/e2e-s-README.md) | s-1…s-4 + gate |
| [`e2e-4-README.md`](simple-agent-tasks/e2e-4-README.md) | 4-1…4-8 + gate |
| [`e2e-2-README.md`](simple-agent-tasks/e2e-2-README.md) | 2-0…2-2 + gate |
| [`e2e-x-README.md`](simple-agent-tasks/e2e-x-README.md) | x-1…x-11 + gate |
| [`e2e-3-README.md`](simple-agent-tasks/e2e-3-README.md) | 3-1…3-5 + gate |
| [`e2e-5-README.md`](simple-agent-tasks/e2e-5-README.md) | 5-0…5-3 + gate |
| [`e2e-6-README.md`](simple-agent-tasks/e2e-6-README.md) | 6-0…6-2 + gate |

## Suggested admit shapes (when unparking)

One suite at a time. Do not admit 0–X–6 as a single mega-suite. If fleet-eval is CURRENT, do not also admit an overlapping `e2e-*` row. For a **fleet**, admit **one wave** from `e2e-README.md` (wave 1 = 0 + p + g0).

| Admit as | Slices | Size | Why that cut |
|----------|--------|------|--------------|
| `e2e-honest` | 0 | S | Claim lock |
| `e2e-print` | P | S | L10; four printer bugs |
| `e2e-params` | R | M | L3 revised + P2 type-check + invoke/MCP args |
| `e2e-unique` | 1 | S–M | Already-authored invariant |
| `e2e-q3export` | 2 | M | Trust bar on generated actions + 05-F5 |
| `e2e-subs` | S | S–M | Subscription runtime/export |
| `e2e-relmap` | 3 | S–M | Storage IR + pack SQL; needs P0-0 |
| `e2e-api` | 4 | M | Transport probes; needs P0-0 |
| `e2e-export` | X | M | Entity C# 🔴s |
| `e2e-valuetype` | 5 | M | New product type |
| `e2e-contracts` | 6 | M | New product surface |

Solidify with `*-README.md` + numbered tasks + gate + pr1 only when that row is CURRENT. Prefer fleet-eval task IDs as the micro-task checklist when the admitted slice maps 1:1 (do not rewrite P3-1 as a new ID).

## Admit checklist

1. [ ] Finish or park the live Agent pick (including any live fleet-eval batch).
2. [ ] Name **one** suite id from the table above.
3. [ ] Update PIPELINE-STATUS + READY-TO-TASK + master-roadmap Agent pick in the same change.
4. [ ] Write `simple-agent-tasks/<suite>-README.md` with exact steps, file ownership, and fleet IDs.
5. [ ] Pre-ship via `pr1` before `[x]` on the suite gate.
