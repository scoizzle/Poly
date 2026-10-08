# Related-entity stage gates — research (2026-08-11)

**Question:** Should relationship member access, relationship action invocation, and
other cross-entity patterns support **specifying and enforcing stage gates on the
related entity**?

**Answer:** Yes — with one root primitive missing today. The engine already enforces
stage gates *implicitly* for cross-entity action invocation (via stage-scoped action
resolution on the target), but there is **no way to read a related entity's current
stage in any expression**, which blocks the declarative forms the DSL spec assumes
(`payment.stage is Captured`, `items.all(i => i.stage is Reserved)`). This document
inventories each pattern, records what is verified to work, and scopes the design
options. No code changes — research only.

---

## 1. The four cross-entity access patterns

| Pattern | DSL surface | Where it lives today |
|---|---|---|
| **Member read** | Path-prefix `Rel.Prop` (OneToOne), collection quantifiers `any/all/none/count Rel { … }`, peer-binder path-prefix `as name { name Prop }` | `RelationshipNavigation`, `AnyExpr`/`AllExpr`/`NoneExpr`/`CountExpr` in `Poly/DomainModeling/DomainExpression.cs` |
| **Action invocation** | `invoke Rel.Action`, `invoke any\|all Rel.Action [where expr]` | `InvokeActionEffect` (`Effects/InvokeActionEffect.cs`), runtime `DomainEntityInstance.ExecuteInvokeEffect` |
| **Subscription** | `when [any\|all] Rel Stage, Stage2 [as name] { effects }` | `StageSubscription` — stage-gated **by construction** (the target stage list *is* the gate) |
| **Guard/condition** | `require PolicyName`, `entry/exit require { expr }`, `if (expr)` | Policy expressions (`PolicyConstraintAnalyzer`), effect conditions (`ConditionalEffect`) |

The subscription pattern is already stage-gated by design — the remaining question
is whether the other three need it, and what the missing primitive is.

---

## 2. Verified current behavior

### 2.1 Member reads — no stage semantics at all

- Path-prefix reads validate `PropertyAccess` names against the **target entity's
  property map** (`PolicyConstraintAnalyzer.ValidateRelatedPropertyAccess`); there is
  no `Stage` property in any property map and no special case for one.
- `DomainExpression` has **21 record nodes and no stage-of node** — no `StageOf`,
  no `Stage` pseudo-property, nothing.
- At runtime, path-prefix leaves are compiled against the linked instance's **values
  bag** (`DomainEntityInstance.EvaluatePathPrefixChain` → `hop._values`).
  `CurrentStage` is a separate instance property, **not** in the bag — so even a
  hand-written `payment.Stage` would not resolve.
- Consequence: the spec's Phase 2 examples that read related stages —
  `payment.stage is Captured`, `items.all(i => i.stage is Reserved)`,
  `reservations.all(r => r.stage is Reserved)`, `this.order.stage is Draft` —
  **do not parse or analyze today**. They are aspirational.

### 2.2 Cross-entity invoke — implicit gate, three leaks

Verified in `DomainEntityInstance.ExecuteInvokeEffect` → `target.InvokeAction(...)` →
`InvokeActionInternal` → `TryResolveAction(Domain, Entity, CurrentStage, actionName)`:

1. **Stage-scoped actions are gated.** Resolution checks the target's `CurrentStage`
   stage-action map first; a stage-scoped action is only invokable when the target is
   in that stage. Otherwise → `Missing` → the parent action **fails loud**
   (`invoke '…' failed`). This is the "stage machine is the link generator" idea
   working at runtime today.
2. **Entity-level actions are ungated.** Fallthrough resolution
   (`arm.EntityActions`) makes any entity-level action invokable from **any** stage of
   the target. There is no way to declare "invokable only from these stages" on an
   entity-level action.
3. **`when Stage` on actions is parsed and not enforced.** The parser consumes
   `when Draft, Submitted` silently
   (`PolyDslParser.ParseActionBody` — "Stage gates are not runtime-enforced in Phase
   1a (BR.3.2)"; honesty note in `DomainTools.cs`). So the spec's multi-stage action
   form (`action Cancel when Draft, Submitted, Confirmed`) is **stored but has zero
   runtime effect** today.
4. **`where` filters cannot reference stage.** Invoke filters are restricted to
   target-local properties/literals/comparisons/bool/arithmetic
   (`EffectAnalyzer.ValidateInvokeFilterExpression`) — no stage access. Singular
   OneToOne invoke (`invoke Rel.Action`) allows **no filter at all**, so a stage
   precondition can't be expressed on the invoke itself.

### 2.3 Subscriptions — already the stage-gate feature

`when Rel Stage` (with `Each`/`any`/`all`, comma-separated target stages = OR
membership, peer binder) is inherently gated on the *target's* stage. One gap vs. the
spec: compound cross-relationship conditions (`when all reservations Reserved and
payment Captured { transition to ReadyToShip }`) are **not** in the shipped grammar —
the subscription takes one relationship and a flat OR stage list.

### 2.4 Guards — can't see related stages

`entry require { … }` / `exit require { … }` / `if (…)` conditions use the same
`DomainExpression` surface: no stage access. "Order can only transition to Shipped
while its payment is Captured" is not expressible today — the state must be derived
from data (e.g. a `paidAt` null-check) or modeled with an extra subscription hop.

---

## 3. The gaps (hypothesis confirmed, sharpened)

**G1 — No stage-of access on related entities (the root gap).** One missing
expression primitive blocks: entry/exit guards on related state, `if` conditions in
action bodies, invoke `where` stage filters, and policy expressions over related
entities. Everything else follows from this.

**G2 — Declarative target-stage gate on invoke.** Today the gate is *implicit*
(stage-scoped resolution) and *incomplete* (entity-level actions ungated, `when`
gates ignored). There is no way to write "invoke `order.Cancel` only while the order
is in Draft/Submitted" and have it enforced as a gate rather than a resolution side
effect.

**G3 — Stage-gated member *reads*** — the user's other hunch. Splits into two
flavors:
- *Availability:* "`shippedAt` is meaningless until Shipped" — already handled by
  data (nullability) + entry `require`. The spec's stance is that stage-specific data
  constraints belong in entry `require`, not on properties. No new primitive needed.
- *Authorization:* "only role X may read `Order.shippingAddress` when Shipped" — this
  is actor/policy territory (Phase 2+), which the spec defers (RBAC-constrained
  links are a *lowering* concern). No new primitive needed; it composes from G1 once
  policies can read related stages.

**Conclusion:** the DSL needs G1 (stage-of access). G2 needs a small, existing-surface
change (enforce what's already parsed) plus G1. G3 needs neither — it's data +
authorization, both already designed for.

---

## 4. Design options for the missing primitive (G1)

### Option A — `.Stage` pseudo-property on entity types

`payment.stage is Captured`, `items.all(i => i.stage is Reserved)`.

- Analyzer injects a synthetic `Stage` member into target property maps
  (path-prefix + quantifier bodies + policies all resolve it); printer emits it
  canonically; runtime lowers it to `CurrentStage` on the related instance.
- Maps cleanly to the exported C# record, which **already exposes `CurrentStage`**
  (`DomainToCSharpExporter`), and to the runtime instance's `CurrentStage`.
- Type: stage names are entity-specific — analysis must validate the referenced stage
  name against the **target entity's** stages, mirroring subscription target-stage
  validation (fail-closed; unknown stage = error).
- Only wrinkle: `Stage` could collide with a real property named `stage` — resolve by
  reserving the name on entities (or requiring `CurrentStage`-style casing).

### Option B — `Rel is Stage` noun form

`payment is Captured`, `this.order is Draft`.

- Reads naturally as a domain assertion, matches the "target natural language
  fragments" syntax principle.
- Requires a new comparison form (sugar over `Equal(Rel.CurrentStage, X)`), plus the
  same target-stage validation. More grammar surface than A for the same power.

### Option C — `StageOf(Rel)` functional form

- Matches the existing collection-op style (`items.all(...)`) but is the least
  readable for the common "is the payment captured" case.

**Recommendation:** Option A first — lowest friction, one synthetic member, reuses
existing path-prefix/quantifier machinery end-to-end. Option B can be added later as
sugar if dogfood prefers it.

---

## 5. Design options for invoke gates (G2)

**Layering:**
1. **Enforce the already-parsed `when` on actions** — make `when Draft, Submitted` on
   entity-level actions a real runtime gate (AND with placement-based gating). This
   closes the documented honesty gap (`DomainTools.cs` HONESTY NOTES) with **no
   grammar change** — the parser already stores it. Note the SA stage-copy fallthrough
   (`TryResolveAction`): an empty stage copy falls through to the entity action; that
   interplay must be preserved or made explicit.
2. **Extend invoke `where` with stage access** (needs G1): `invoke all items.Tag()
   where stage is Reserved` — lets any/all collection invokes filter by target stage,
   consistent with the existing "target-local" filter contract.
3. **Call-site `when`** (`invoke order.Cancel when order is Submitted`) — *defer*.
   Once (1) and (2) exist, the remaining case is singular OneToOne invoke with a
   stage precondition; that can be expressed with `if (order.stage is Submitted) { … }`
   once G1 lands. Add a dedicated keyword only if dogfood shows the `if` wrapper is a
   real ergonomic pain.

**Fail-closed posture (keep):** a stage-mismatched invoke must **fail loud**, matching
today's `Missing` throw and the "zero matches fail (no vacuous `all`)" rule. No silent
skip. Empty-set and stage-mismatch are the same class of bug.

---

## 6. Adjacent patterns (tracked, not in scope)

| Pattern | Status |
|---|---|
| Compound subscription gates (`when all reservations Reserved and payment Captured`) | Spec Phase 2; needs multi-rel subscription grammar + stage-of |
| Cross-entity `require Rel.Policy` (qualified policy refs, e.g. `require Employee.Warehouse`) | Spec Phase 2/3; a *policy* gate on related entities, not a stage gate — compose later |
| Stage-scoped child creation (`create in Rel` bound to the owning stage) | Derived fact already noted in `docs/plans/relationship-domain-model-synthesis-2026-08-10.md`; creation always starts children in their initial stage |
| HATEOAS / `_links` filtering by stage | Lowering-time; already stage-driven via stage-scoped actions — no DSL change needed |

---

## 7. Implementation obligations if pursued

- **Thread the new node through all four raw-switch sites** on `DomainExpression`
  (per `docs/plans/domain-modeling-abstraction-gaps.md` Finding 1):
  `DomainExpressionLoweringPass`, `DomainDslPrinter`, `DomainExpressionJsonParser`,
  `PolicyConstraintAnalyzer` — plus the runtime quantifier/path-prefix preprocessor
  (`DomainEntityInstance.QuantifierPreprocessRewrite`) which must learn to resolve
  `Stage` against `CurrentStage` instead of the values bag.
- **Three-layer defense:** parse-time rejects (stage name is an identifier, not a
  literal), analyze-time catches (stage exists on target entity — mirror subscription
  target-stage validation), runtime fails loud (unknown/mismatched stage).
- **Round-trip:** printer must emit the canonical form (idempotence:
  parse → print → parse → print converges).
- **Docs:** update `DOMAIN-DSL-SPEC.md` (Collection operations + Stage Gates
  sections) **and** `Poly.Mcp/Docs/poly-dsl-guide.md` in the same change
  (`GetDslGuide_ReturnsProductSurface` guards the latter).

## 8. Suggested next step

Treat G1 (`.Stage` pseudo-property) as the design-lock candidate: it is the smallest
change that unlocks every spec example that reads a related entity's stage, and it
makes G2's `where`-filter extension a two-line follow-up. Write the design as an ADR
or phase-2 plan entry before touching the parser.
