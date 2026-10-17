# Analysis coverage audit — "what breaks if I change X?" (2026-10-16)

Spike: does analysis **detect enough** for the change-impact feature to be trustworthy?
Thesis under test: the guided upstream/downstream answer is entirely a function of analysis
coverage — every consequence of a change must yield a `Diagnostic` (`Poly/Analysis/Diagnostic.cs`)
or a **true** per-node bag (`Poly/DomainModeling/Analysis/*Metadata.cs`). Where analysis is
silent or wrong, the answer is missing or a lie.

Base: worktree `spike/analysis-coverage` at `origin/master` `ff83c598` (F30d #191).
Findings only — no product or test code touched. Probes authored in `/tmp/ac/*.poly`,
observed with the cheapest instrument (MCP `apply_dsl` / `get_domain_analysis` /
`create_instance` / `invoke_action`, `Poly.DslCompiler --mode all --http --dbms sqlite` +
host build). All snippets below are self-contained.

## Method / instruments

| Instrument | What it proves | Cost |
|---|---|---|
| `poly-local` `apply_dsl` | parse + evolution (fail-closed rollback) | seconds |
| `poly-local` `get_domain_analysis` | diagnostics-as-strings + facts (roots, aggregates, plans) | seconds |
| `poly-local` `create_instance` / `invoke_action` / `get_instance` | VM semantics (does the consequence actually happen) | seconds |
| `dotnet run … DslCompiler --mode all --http --dbms sqlite …` + `dotnet build` host | emitted C# compiles (routes, DTOs, DbContext, seed) | ~10s |

Diagnostic **codes are not visible in the MCP harness.** `get_domain_analysis` returns
`data.messages` as `"[{Severity}] {Message}"` strings (`Poly/DomainModeling/Queries/DomainQueries.cs:280-284`),
with **no `Code`, no node/`file:line` anchor, and `errors.Concat(warnings).Take(10)`** (warnings
after >10 errors are dropped). Codes below are from source, not from the observed surface.
This is itself a finding (§T1).

---

## 1. Coverage matrix

Legend: **D** detected (diagnostic/rollback) · **D-** detected but only as an unstructured apply-time
message · **S** silent (no diagnostic) · **W** wrong/missing fact · **F** fact correct.

### A. Reference integrity — retarget/rename a referenced element (H3 #129 territory)

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| A1 | nav target → unknown entity | D- | parse: `Property 'bar' is declared with type 'Missing', which is neither an entity … nor a value type (Binary, Boolean, …)` | `Foo: entity { Name: Text bar: Missing }` | none — fail-closed |
| A2 | `invoke` unknown action | D- | rollback: `InvokeAction effect references action 'Nope' which does not exist on entity 'Foo'.` | `Foo: entity { Go: action { invoke Nope } }` | none |
| A3 | `assign` unknown property | D- | rollback: `Assign effect targets property 'Nope' which does not exist on entity 'Foo'.` | `Go: action { assign Nope to "x" }` | none |
| A4 | `require` unknown policy | D- | parse: `Action 'Go' requires policy 'MissingPolicy' which is not defined on entity 'Foo'.` | `Go: action require MissingPolicy { … }` | none |
| A5 | `transition to` unknown stage | D- | rollback: `StageTransition effect targets stage 'Nowhere' which does not exist on entity 'Foo'.` | `A: stage { Go: action { transition to Nowhere } }` | none |
| A6 | subscription → unknown relationship | D- | rollback: `… Subscription relationship 'children' on entity 'Foo' could not be uniquely resolved …` | `Foo { when children Active { … } }` | none |
| A7 | subscription → unknown stage on target | D- | rollback + `Available stages: Fresh` | `Foo { kids: many Kid when kids Nowhere { … } }` | none |
| A8 | unknown enum member in `default(...)`/`assign` | D- | rollback: `'Elite' is not a member of enum 'Tier'` | `Level: Tier default(Elite)` | none |
| A9 | `create in` unknown nav | D- | rollback: `CreateIn effect references unknown relationship 'missing' …` | `Go: action { create in missing { N: "x" } }` | none |
| A10 | action `-> Unknown` | D- | rollback ×2: `Result member 'Result' references unknown type 'Missing'.` + `… no create or create-in effect produces … 'Missing'.` | `Go: action -> Missing { … }` | none |
| A11 | policy reads unknown property | D- | rollback: `Policy references property 'Size' which does not exist on entity 'Foo'.` | `IsBig: policy { Size > 18 }` | none |
| A12 | nav name == a type name (exact case) | D- | rollback DMSTR001: `Relationship name 'Start' collides with a type of the same name.` | `Foo { Start: Bar }` + `Start: entity` | none |
| A13 | nav named `any`/`all`/`none`/`count` | D- | rollback DMSTR001: `Relationship name 'any' is reserved (used by expression quantifiers) …` | `Foo { any: many Bar }` | none |
| A14 | type `DomainResult` / `{Entity}Stage` | D- | rollback DMSTR004 | `DomainResult: entity {…}` · `FooStage: enum {…}` + `Foo: entity { Active: stage {} }` | none |
| A15 | incremental `remove(property)` still referenced | D- | rollback: `CreateIn initializer references unknown property 'Qty' on entity 'Line'.` | `remove kind=property {entityName:Line,name:Qty}` | none |
| A16 | incremental `remove(entity)` still referenced | D- | rollback: `Relationship 'order' target references unknown type 'Order'.` | `remove kind=entity {name:Order}` | none |

**Row A is clean** — every rename/retarget of a referenced element fails closed with a message
that names the referrer and the referent. The only weakness is delivery: the answer is a bare
string with no code, no `Node`/`file:line`, and H3's structured `catalog-reference-report`
(`Poly/DomainModeling/Compile/DomainSession.cs:428-449`) is **not surfaced** to the MCP harness (§T1).

### B. Constraint change (`unique`/`range`/`length`/`pattern`/`equals`/`required`/`default`)

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| B1 | tighten `range`; a writer's derived expression can fall outside | D | **DMEFF008** / `Assigned expression value range [-100, 0] can fall outside constraint range(0, 100) on property 'Qty'.` (`EffectAnalyzer.cs:1386`) | `Qty: Number range(0,100)` + `assign Qty to Qty - 100` | none |
| B2 | writer entirely/literally outside | D- | rollback: `Assigned expression value range [200, 300] is entirely outside constraint range(0, 100) …` / `Assigned value '50' violates constraint range(0, 10) …` | `Set: action { assign Qty to Qty + 200 }` | none |
| B3 | declared range ∋ derived range | F | no diagnostic (correct) | `Qty range(0,80)` `assign Total to Qty + 10` | none |
| B4 | add `required`; a create omits it | D- | **DMEFF011** rollback: `Create effect on 'Line' does not provide required property 'Code'. …` (`EffectAnalyzer.cs:823-850`) | `Line { Qty: Number Code: Text required }` + `create in lines { Qty: 1 }` | none |
| B5 | add a **no-`default`, non-`required`** property omitted by a create-in | **S** | nothing | `Line { Qty: Number }` + `create in lines { }` | analysis says the create is fine; the emitted factory silently substitutes `0L` (Number) / `Tier.Basic` (enum, `Order.cs:39`). **MCP `create_instance` of an omitted no-default enum FAILS** — see G3 |
| B6 | change/remove `default(...)` | F | `default` governs creatability (ctor param + DTO membership); no diagnostic needed, but see S24 | — | fact correct, downstream consumers (DTO) drop the property |
| B7 | parameter flows into a `range` property → DTO attribute | F | `SetDto.amount` emits `[Range(0, 100)]` (from `DownstreamConstraints`) | `Item { Stock: Number range(0,100) Set: action (amount: Number) { assign Stock to amount } }` | fact correct |
| B8 | enum column type after a constraint/type change | F | `b.Property(x => x.Roast)…HasColumnType("INTEGER")` (S28 fixed) | `Roast: Roast` | none |

### C. Add/remove a navigation (collection vs singular vs `owned`)

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| C1 | add a collection back-ref to a root | F | `roots=[Member]`, `Member:[Booking]`; nested routes `/api/customers/{email}/orders` | `Customer { orders: many Order }` + `Order { customer: Customer }` | none (S18 fixed) |
| C2 | add a plain **singular** nav to an entity (keeping it root-intent) | **S/W** | nothing; entity silently demoted from root | `Patron{Name unique}` + `Loan{Ref unique Amount borrower: Patron}` | `roots=[Patron]`, `Loan` has **no routes at all** — only `/api/patrons*`; `demo.http`'s `Loans` section is empty. You cannot create/list/get a Loan over HTTP, and **no diagnostic** |
| C3 | two entities each hold a singular nav (mutual) | **S/W** | nothing; `aggregateRootCount: 0`, no `rootEntityNames`, no `aggregates` | `A { bs: many B  fav: B }` + `B { a: A }` | "what breaks" answer is *zero roots* with no explanation; routes become `/api/bs/{name}/a`, `/api/as/{name}/bs`, `/api/as/{name}/bs/{bName}` (both demoted, neither root) |
| C4 | `owned` nav | F/D | target not root, owner stays root; `DMAGG001` fires for the genuinely parentless shape | `Loan { borrower: Patron  notes: many Note }` → `[Warning] Entity 'Loan' is a non-root entity with no aggregate parent …` (`OwnershipAggregatePass.cs:29-34`) | correct |
| C5 | nav never written (no `create in`, no `link`) | **S** | only hints | `Policy { Name claims: many Claim }` + `Claim { Ref }` | the edge is dead; nothing says so; see F5 |
| C6 | collection nav whose PascalCase name == target type + a `create in` | **S** (host fails) | nothing at analysis; host build: `CS7036: … FileSystemAclExtensions.Create(FileInfo, …)` | `Claim { evidence: many Evidence  Attach: action -> Evidence { create in evidence { Kind: } } }` | analysis says the domain is fine; the emitted host does not compile (S11). Exact-case nav `Evidence` **is** caught (A12) — only the PascalCase collision is missed |
| C7 | naive pluralization | W | `Address` → `/api/addresss`, header `Addresss`; `A` → `as`, `B` → `bs` | `Address: entity { … }` | cosmetic but user-visible; no diagnostic (S19) |
| C8 | nav routes / nesting follows the aggregate | F | root routes + nested child routes correct | root_routes probe | none (fixed) |

### D. Policy retarget that actions `require`

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| D1 | rename/remove a policy an action `require`s | D- | parse: `Action 'Go' requires policy 'MissingPolicy' which is not defined on entity 'Foo'.` | `require MissingPolicy` | none |
| D2 | retarget a policy expression | F | re-evaluated; no diagnostic needed | — | none |

### E. Stage / transition graph

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| E1 | `transition to` unknown stage | D- | rollback (A5) | — | none |
| E2 | a subscription watches a stage **no action can reach** | **S** | nothing; the plan is still published | `Foo { kids: many Kid when kids Ghost { … } }` + `Kid { Fresh: stage {} Ghost: stage {} }` | subscription silently never fires; removing the last transition to `Ghost` breaks nothing visibly and warns nothing |
| E3 | stage gating (invoke from the wrong stage) | runtime-only | `'Complete' requires stage 'Done' on entity 'Job'.` | `Todo: stage {} Done: stage { Complete: action { transition to Done } }` then invoke `Complete` | analysis is silent; VM is honest (acceptable — runtime gate, not a static fact) |
| E4 | cross-entity dependency cycle | D | **DMDEP001**: `[Warning] Cross-entity dependency cycle detected involving: A, B` (`CrossReferencePass.cs:69-74`) | `A { beeList: many B  when beeList BActive { transition to AActive } }` + mirrored `B` | correct |
| E5 | stage-subscription causality cycle | D | **DMSS001** (`SubscriptionAnalyzer.cs:109-121`) | not isolated | correct |

### F. Subscription add/alter (`when … Stage`, quantifiers)

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| F1 | unknown relationship / stage | D- | rollback **DMSS003** (A6/A7) | — | none |
| F2 | `any`/`all` on a singular relationship | D- | rollback **DMSS003**: `… quantifier 'Any' on one-to-one relationship 'kid'. 'Any' is meaningless …` | `Foo { kid: Kid when any kid Active { … } }` | none |
| F3 | duplicate **stage-scoped** subscription key | D | **DMSS003** warning: `Duplicate stage subscription key on stage 'Pending' … Remove-all semantics apply.` (`SubscriptionAnalyzer.cs:45-56`) | two `when kids Active` inside one stage | correct |
| F4 | duplicate **entity-level** subscription key | **S** | nothing; the plan merges both into one `quantifiers:[Each]` entry | `Foo { when kids Active { … } when kids Active { … } }` | two subscriptions silently collapse; the fact cannot distinguish them |
| F5 | subscription reads a nav that is **never written** | **S** | nothing; plan published | `Policy { Counter: Number default(0) claims: many Claim when claims Filed { assign Counter to Counter + 1 } }` | subscription silently never fires; nothing ties "never written" to "never fires" |
| F6 | `when … as peer { … peer Prop … }` on a set (`all`) | **S/W** | nothing; runtime under-counts | `when all sheets Approved as sheet { assign Gross to Gross + sheet Hours * Rate }`, approve 8h/6h/4h one at a time → `Gross = 0, 0, 80` (intent 360) | the running total is silently wrong (S22); only the transitioning peer contributes |
| F7 | change the quantifier `Each` → `any` | **S/W** | nothing; plan says `Any` but behavior == `Each` | 3-job fan-out → `DoneCount=3, AnyCount=3, AllCount=1` | guide §7 says `any` "fires once"; runtime fires per transition; the published fact says `Any`, the behavior says `Each` (S25) |
| F8 | subscription fires when a child is **created into** the watched stage | F (fixed) | no diagnostic needed — it works | `Parent { kids: many Kid  when kids Fresh { assign Ticks to Ticks + 1 } }` + `Add … create in kids` → `Ticks = 1` | **S14 re-verified fixed** (#183 / `CreateInWatchedStageTests`); single-fire, not double |

### G. Enum member set change

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| G1 | add a member | F | — | — | none |
| G2 | remove a member still referenced (default/assign/policy) | D- | rollback: `'Elite' is not a member of enum 'Tier'` | `default(Elite)` | none |
| G3 | enum property with **no `default`** at create | **W** | no diagnostic; VM: `Failed to create instance: 'Level' is not a valid member of Tier.` for both **omitted** and **wrong** value; print instead emits `Tier.Basic` | `Member { Name: Text required Level: Tier }` + `create_instance {Name:"Ada"}` | the "what breaks" answer is missing; the error message never says the value was omitted or which value is bad (S2/S3); VM and print disagree on the omitted case |

### H. Storage projection

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| H1 | add `unique` | F | `HasIndex(x => x.Email).IsUnique()`; retargets the natural-key route `{id}`→`{email}` | `Customer { Email: Text unique }` vs `Email: Text` | fact correct; the route retarget is a real "what breaks" consequence with no warning |
| H2 | `range`/`length`/`pattern`/`equals` | F | CHECK only when every writer is analysis-verified; length max only; pattern/equals code-enforced | `range`, `length`, `pattern` | correct |
| H3 | enum column type | F | pack backing type `INTEGER` (S28 fixed) | `Roast: Roast` | none |
| H4 | a **value-typed** property (`value` type) | **S** (host fails) | compiler emits `b.Property(x => x.Bill).HasColumnType("varchar")` with no converter; host build: `CS1503: cannot convert from 'string' to 'Money?'` at the generated seed `Order.Create("Sample", "Sample", …)` (Program.cs:67) | `Money: value { Amount: Number Currency: Text }` + `Order { Bill: Money }` | `DslCompiler --mode all` reports success; the host never builds; the mapping is unpacks (S7 + a new seed-positional break) |

### I. HTTP / API surface

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| I1 | roots get create/list + nested child routes | F | `/api/customers`, `/api/customers/{email}/orders`, `/api/customers/{email}/place` | root_routes | correct (S18 fixed) |
| I2 | create DTO membership follows the ctor (drops `default`s) | **W** | `BeanDto { Name }` only; `POST /api/beans {"Roast":"Dark"}` is accepted and the value discarded | `Bean { Name unique Roast: Roast default(Medium) Stock: Number default(0) }` | a client cannot set a defaulted property and gets a silent `201` with the default (S24); `MinimalApiGenerator.cs:984-1011` |
| I3 | action parameter constraints → DTO | F | `SetDto.amount` `[Range(0,100)]` | param_dto | correct |
| I4 | singular-nav entity / cyclic aggregate lose routes | **S** | see C2/C3 | — | HTTP surface silently disappears |

### J. Name collisions

| # | Change | Detected? | Code / message | Repro | Cost if wrong |
|---|--------|-----------|----------------|-------|---------------|
| J1 | member named a DSL keyword (`policy`, `stage`, `when`, …) | D- (poor DX) | parse: `Expected property, stage, action, or policy, got 'policy'` — **no reserved-word hint** (`PolyDslParser.cs:315`) | `Claim { Code: Text unique policy: Text }` | author is told the *type* is bad, never that `policy` is reserved (S10) |
| J2 | entity named `Task` (BCL shadow) | F (fixed) | host builds `0 errors` | `Job { tasks: many Task }` + `Task { Label job: Job }` | none (S26 fixed) |
| J3 | nav name → same as its type, exact case | D- | rollback DMSTR001 | `Start: entity` + `Foo { Start: Bar }` | none |
| J4 | nav PascalCase name == target type (`evidence`/`Evidence`) | **S** (host fails) | see C6 | — | silent until the host build (S11) |
| J5 | type `DomainResult` / `{Entity}Stage` | D- | rollback DMSTR004 | — | none |

---

## 2. Silent / wrong classes (ranked, with repro + observed output)

### T1 (cross-cutting) The MCP answer surface drops the diagnostic anchor and the code

`get_domain_analysis` → `DomainQueries.GetAnalysisSummary` (`Poly/DomainModeling/Queries/DomainQueries.cs:280-284`):

```csharp
Messages: errors.Concat(warnings)
    .Take(10)
    .Concat(hints)
    .Select(d => $"[{d.Severity}] {d.Message}")
```

Observed: `"[Warning] Assigned expression value range [-100, 0] can fall outside constraint range(0, 100) on property 'Qty'."`
— no `DMEFF008`, no `file:line`, no node id. With >10 errors the tail warnings vanish.
The node anchoring that `Diagnostic(Node, Severity, Message, Code)` provides is discarded before it
reaches the agent. H3's structured `catalog-reference-report` is registered only on `Lower`/`Emit`
(`DomainSession.cs:428-449`) and is **not** read by `DomainTools.GetDomainAnalysis`
(`Poly.Mcp/Tools/DomainTools.cs:273-394`). **Cost:** the "guided" answer is a flat list of sentences;
an agent cannot link a finding to the changed element.

### T2 Aggregate root collapse (C2/C3) — silent, and deletes the HTTP surface

`EntityStructureAnalyzer.ComputeStructure` (`Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:57`):
`isRoot = !HasRequiredEntityRef(entity, lookup) && !IsOwnedTarget(entity, domain)` — **any** plain
singular nav makes the source non-root. `OwnershipAggregatePass.OnlyOutboundSingular`
(`OwnershipAggregatePass.cs:173-185`) then suppresses `DMAGG001` for exactly that shape, and
`ResolveChild`'s singular fallback (`:143-151`) can point "up" into a collection owner → a parent cycle.

Observed (C2), MCP: `roots: ["Patron"]`, `aggregates: [{Patron: []}]`, 0 warnings, message list only hints.
Observed (C2), emitted host: routes = `/api/patrons`, `/api/patrons/{name}`, `POST /api/patrons` — **no Loan routes**; `demo.http` has an empty `### Loans` section.
Observed (C3, mutual singular): MCP `aggregateRootCount: 0` and no `rootEntityNames`/`aggregates` at all;
host routes = `/api/bs/{name}/a`, `/api/as/{name}/bs`, `/api/as/{name}/bs/{bName}`.
**Cost:** "what breaks if I add a `borrower: Patron`?" → analysis says nothing, then the API loses
the entity. This is the S1b residual surviving as a product defect (S17/S18 family).

### T3 Dead navigation edge (C5/F5) — silent

A relationship that no `create in` and no explicit `link` ever writes is dead for path reads,
quantifiers and subscriptions. `EffectTopologyPass` already publishes `CreateInRelations`
(consumed by `OwnershipAggregatePass`), and `get_domain_analysis` exposes `createInCount`, but nothing
compares "relationships" to "relationships that can be written".
Observed: `Policy { claims: many Claim }` and `when claims Filed { … }` → only hints; the subscription
plan is published as if it will fire. **Cost:** removing/changing a dead nav breaks nothing (correct)
but the reverse — the fact that the subscription *can never fire* — is never stated.

### T4 PascalCase nav name == type name (C6/J4, S11) — silent until the host build

`StructuralDomainAnalyzer.AnalyzeDomain` (`Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:47-53`)
compares `nav.Name` **as written** to type names. The C# surface PascalCases it (`evidence` → property
`Evidence`), which then shadows the entity type `Evidence`.
Observed: analysis clean; host build `Claim.cs(24,39): CS7036: … FileSystemAclExtensions.Create(FileInfo, …)`.
Exact-case nav `Evidence` **is** rejected — only the PascalCase mismatch is missed.

### T5 Value-typed property (H4, S7) — silent at the compiler gate

`StorageAnalyzer.ClassifyProperties` (`Poly/DomainModeling/Analysis/StorageAnalyzer.cs:266-311`) emits a
flat column for a `value`-typed property; the emitted seed passes positional args
(`Program.cs:67`: `Order.Create("Sample", "Sample", …)`) into `Create(Money? bill, string reference, …)`.
Observed: `DslCompiler --mode all` → `Done: 6 file(s) written`; host build → `CS1503`.

### T6 No-`default` property / enum omission (B5/G3, S2/S3)

`DMEFF011` (`EffectAnalyzer.cs:823-850`) keys on **`required`**, not on "no `default`" — yet `default(...)`
is what governs creatability (guide §3). Observed: `create in lines { }` with `Qty: Number` (no default)
→ clean; print substitutes `0L`; MCP `create_instance(Line)` also succeeds. But an enum with no default
omitted at `create_instance` → `Failed to create instance: 'Level' is not a valid member of Tier.` —
identical text for an omitted value and a wrong value (S2/S3), while the printed factory picks `Tier.Basic`.

### T7 Create DTO drops `default` properties (I2, S24)

`MinimalApiGenerator.BuildDtoTypes` (`src/Poly.DslCompiler/MinimalApiGenerator.cs:984-1011`) mirrors the
ctor, which excludes default-valued props (`EntityStructureAnalyzer.cs:118-119`).
Observed: `BeanDto { public string Name { get; init; } }`; `Bean.Create(dto.Name, Enumerable.Empty<Batch>())`.
A client-supplied `Roast` is accepted and discarded.

### T8 Duplicate entity-level subscription key (F4) — silent

The duplicate-key check (`SubscriptionAnalyzer.cs:45-56`) runs only for `stage.Subscriptions`, not
`entity.Subscriptions` (validated at `:39-41`). Observed: two identical entity-level `when kids Active`
→ no warning; one plan entry `quantifiers:[Each]`.

### T9 `any` ≡ `Each`; `all` + peer binder under-counts (F6/F7, S22/S25)

The published plan says `All`/`Any`/`Each`, but runtime behavior does not match guide §7.
Observed: fan-out of 3 → `DoneCount=3, AnyCount=3, AllCount=1`; peer-binder `all` → `Gross = 0, 0, 80`.
The fact is not *false* (the quantifier is what was authored), but the meaning the guide attaches to it is.

### T10 Reserved-word member DX; naive pluralization (J1/C7, S10/S19)

`Parse error: … got 'policy'` never mentions "reserved". `Address` → `/api/addresss`.

---

## 3. Ranked recommendations (which diagnostic / fact to add, in order, at which seam)

Each is a named seam; the `argument`/`file:line` columns are the intended edit point.

| Rank | Add | Kind | Seam (`file:line`) | Why first |
|---|---|---|---|---|
| R1 | Report a parent-chain **cycle** and a **root-collapse** when a non-root entity has no aggregate parent and is not only-outbound-singular | Warning (DMAGG001 family, or new `DMAGG002`) | `OwnershipAggregatePass.cs:24-35` (after the resolve loop at `:79-93`) + `OnlyOutboundSingular:173-185` | Fixes T2/C2/C3: today → zero roots and vanished HTTP routes with **no** diagnostic. Requires the ownership-vs-reference decision for a plain singular nav (`EntityStructureAnalyzer.cs:57`) |
| R2 | Warn when a relationship is **never written** (no `create in`, no `link`) and is read by a subscription/quantifier | Warning, new code | `SubscriptionAnalyzer.cs:189-285` (consume `EffectTopology.CreateInRelations`) or `OwnershipAggregatePass` block | Fixes T3/F5/C5 (S5/S12): the dead edge becomes a stated consequence |
| R3 | Surface `Code` + node anchor + the H3 `catalog-reference-report`; remove the `Take(10)` | Harness | `DomainQueries.cs:280-284` · `Poly.Mcp/Tools/DomainTools.cs:381-394` | Fixes T1: every other row's answer becomes linkable to the changed element |
| R4 | Compare `PascalCase(nav.Name)` to type names in the collision check | Error DMSTR001 | `StructuralDomainAnalyzer.cs:47-53` | Fixes T4/C6/J4 (S11) |
| R5 | Refuse (or classify as nested) a `value`-typed property until storage maps it, and fix the seed positional call | Error (analysis) or emit | `StorageAnalyzer.cs:266-311` · `src/Poly.DslCompiler/MinimalApiGenerator.cs` seed | Fixes T5/H4 (S7) — currently a guaranteed host build failure |
| R6 | Extend `DMEFF011` to "no `default`" too, and make the enum omit/wrong message name the value | Error + message | `EffectAnalyzer.cs:823-850`; printer message (S3) | Fixes T6/B5/G3 (S2/S3/S29) |
| R7 | Include `default(...)` properties in the create DTO (apply default when omitted) | Fact/emitter | `MinimalApiGenerator.cs:984-1011` | Fixes T7/I2 (S24) |
| R8 | Check duplicate subscription keys at **entity** level too | Warning DMSS003 | `SubscriptionAnalyzer.cs:44` | Fixes T8/F4 |
| R9 | Make `any`/`all`/`Each` actually distinct, or correct guide §7 to the runtime | Runtime + guide | subscription dispatch / guide §7 | Fixes T9/F6/F7 (S22/S25) |
| R10 | Reserved-word hint in the parse error; fix of-aware pluralization (`Address` → `Addresses`) | Message / emitter | `PolyDslParser.cs:315` · `MinimalApiGenerator.cs` `Pluralize` | Fixes T10/C7/J1 (S10/S19) |

## 4. What contradicts the thesis

- **The answer surface is a lossy flattening, not node-anchored facts.** T1: `get_domain_analysis`
  emits `[Severity] Message` strings, no `Code`, no node path, errors+warnings capped at 10. Even
  where analysis detects (row A, B1–B4, E4, F1–F3), the harness cannot say *which element* changed.
- **Reference integrity is strong at the source layer but the structured report is unreachable.** All
  of row A fails closed with a clear message; H3's `catalog-reference-report` (`DomainSession.cs:428-449`)
  has no consumer in the authoring/MCP path, so the #129 work is not part of the "what breaks" answer.
- **Facts can be empty/wrong with no diagnostic.** T2/C3: `aggregateRootCount: 0`, no roots, no
  aggregates, no warning; T4/T5: analysis clean while the emitted host does not compile.
- **Some "facts" contradict the documented meaning.** T9/F7: the plan says `Any`, runtime == `Each`;
  T9/F6: `all` + peer binder silently under-counts.

## 5. Clean rows (a clean row is a result)

- Reference integrity (row A, all 16 cases) — fail-closed with a naming message.
- Constraint propagation B1–B4, B7–B8 — range/length/pattern/required enforcement and DTO attribute derivation.
- Ownership for the canonical and `owned` shapes (C1/C4/C8, ORPH2) — roots, members and routes correct.
- Subscription contract E4/F1–F3, cross-entity cycle `DMDEP001`.
- Enum column type (H3), storage keys/unique index (H1–H2).
- Name collisions J2/J3/J5, reserved nav (A13), generated-name (A14).
- **S14 re-verified fixed** on `ff83c598` (#183): a child created into the watched stage fires the
  subscription once (`Ticks = 1`), not twice.

## 6. Board/state note

`docs/plans/poly-eng/board.md:66-68` lists **F28** as a "now" swarm card, but F28 is merged
(`0d7c1b13` #183) and is in `ff83c598`. The board's swarm table is stale relative to master.

## Log

| Date | Who | Event | Findings |
|------|-----|-------|----------|
| 2026-10-16 | spike | Analysis-coverage audit vs `ff83c598`; 40+ probe rows (MCP + DslCompiler + host build); matrix + ranked recommendations | 3 cross-cutting (T1, T9), 2 silent-wrong aggregate classes (T2, T4), 6 silent/wrong consequence classes (T3, T5, T6, T7, T8, T10); 10 seed rows re-verified (S1b/S11/S14/S19/S22/S24/S25 confirmed, S14/S18/S26/S28 fixed); 1 board-staleness note |
