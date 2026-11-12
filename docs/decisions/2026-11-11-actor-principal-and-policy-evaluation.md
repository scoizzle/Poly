# ADR: Actor, principal, and library-provided policy evaluation (#408)

## Status

**Accepted** — owner ruling 2026-11-11 (F-24 / SD12), transcribed. The
[ruling comment](https://github.com/scoizzle/Poly/issues/408#issuecomment-6813173307) on #408 accepts
F-24's four recommendations as binding for F-24, R-09, F-25 and F-26. This file records the ruling and
adds no decision of its own. Slice F-24, theme F ([#690](https://github.com/scoizzle/Poly/issues/690)),
epic [#677](https://github.com/scoizzle/Poly/issues/677).

## Context

#408 defers four decisions to an ADR: the principal contract, `ActorMetadata` contents, the evaluator
call site, and whether `policy external` exists. Until they are fixed, R-09 (the `Poly.Runtime` host
contract), F-25 (actor metadata) and F-26 (evaluation) would each implement a different shape.

On `origin/master` `32d6ecb8`:

- `actor` is refused as an unsupported keyword (`Poly/DomainModeling/Language/DomainResolver.cs:251`),
  the DSL guide says to use `entity` instead (`Poly.Mcp/Docs/poly-dsl-guide.md:1256`), and no
  `ActorMetadata` type exists.
- `require` resolves only against policies defined in the same entity body
  (`ResolvePendingRequires`, `Poly/DomainModeling/Language/DomainResolver.Entities.cs:229-264`). There is
  no `policy external`, no auth library, and no host principal.
- `Policy` carries a non-nullable expression (`Poly/DomainModeling/Ontology/Policy.cs:8-13`), and the only
  evaluation path is the lowered module's own body
  (`Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs:127`).
- `Poly.Runtime` ships the storage port and the failure contract (`Poly.Runtime/Poly.Runtime.csproj`),
  but neither `Principal` nor an evaluator.

The four rulings below are the owner's. The rejected alternatives record what was considered and what it
would have cost; they are not an invitation to re-open.

## Decision

### D1 — The principal is host-supplied and library-typed

**Ruling.** The principal is supplied by the host and typed by a library. It is not a domain type, not a
contract `value`, and not a DSL term. It is contributed once as
`ContributedParameter("principal", <Poly.Runtime type>, ThreadToCallers: true)` through
`MethodContribution`: core inserts the parameter into every caller in library-id order, before the
method's first optional parameter, so no library walks callers
(`Poly/DomainModeling/Compile/MethodContribution.cs:5-11`).

The type is `Principal` in `Poly.Runtime` (R-09), with members `Id`, `Roles` and `Claims`. It is in
namespace `Poly.Runtime`, like the R-02 types (`DomainFailure`, `DomainResult`). Only the moved
`ConstraintFailureException` keeps `Poly.DomainModeling.Runtime`, until R-07, because the emitted
C# surface derives names from the full name; `IStoragePort` moved to `Poly.Runtime` in R-05. Additive members need no new ADR.
Every host entry point that can invoke a guarded action or evaluate a policy carries it:
`ModuleInstanceExecutor.Create` (`Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Create.cs:12`),
`.Invoke` (`…/ModuleInstanceExecutor.Invoke.cs:12`), `.EvaluatePolicy`
(`…/ModuleInstanceExecutor.cs:127`), MCP `RuntimeTool.CreateInstance`
(`Poly.Mcp/Tools/RuntimeTool.cs:180`), `RuntimeTool.InvokeAction` (`:576`),
`DomainTools.EvaluatePolicy` (`Poly.Mcp/Tools/DomainTools.cs:1361`), and the surface `evaluate` call site
(`Poly.Mcp/Surface/PolySurfaceTools.cs:495`).

Fail closed: if `uses auth` is absent, an external policy is a positioned error that names `auth`. If
`uses auth` is present and the host supplies no principal, the evaluator receives null and must deny. An
evaluator that is absent at runtime is impossible — analysis refused it — and is never treated as allow.

**Rejected.** (a) A domain `value` type: every auth domain authors it and claims become domain surface,
a second source of truth next to the IdP; cost is re-typing the principal per domain with no runtime
check of its shape. (b) A contract `value` payload: one bind per action, and the evaluator becomes an
endpoint splice (F-18) instead of one host call; cost is a per-action bind and two divergent policy
paths. (c) Claims as action parameters: the principal becomes authorable domain data a policy can read;
cost is that a caller can forge claims in the domain language and deny-by-default is lost.

**Consumer.** R-09 fixes `Principal` and the request type in `Poly.Runtime`; F-26 threads the parameter
through the host entry points above. **Constrains today:** `MethodContribution.cs:11`;
`ModuleInstanceExecutor.cs:127`; `RuntimeTool.cs:180,:576`; `DomainTools.cs:1361`;
`PolySurfaceTools.cs:495`; `DomainResolver.cs:251`; guide:1256.

### D2 — `ActorMetadata` is an ontology record on an entity

**Ruling.** An actor is a specialised entity, as decided on 2026-11-06; that is recorded here, not
re-opened. `ActorMetadata(string PrincipalKeyProperty, IReadOnlyList<string> ClaimProperties)` is exposed
as `Entity.Actor : ActorMetadata?`. Like `Entity.KeyProperties`
(`Poly/DomainModeling/Ontology/Entity.cs:40-51`), it is metadata over existing members, not a syntax
node: it gets no `NodeFacts` row and no `CompileNodeInner` arm — neither `Entity` nor `Policy` has one
today (`Poly/Interpretation/NodeFacts.cs`).

`PrincipalKeyProperty` names one of the entity's own required properties; a navigation or optional
property is a positioned error. `ClaimProperties` names entity properties that mirror host claims and may
be empty. There is **no roles list**: roles are principal state, and a stored copy is a second source of
truth that drifts from the IdP. An actor record is not a principal record (it may be an IdP projection),
adds no inheritance and no new dispatch arm. It is an entity for every other purpose — it may declare
properties, policies, actions and stages; with no stages it has no stage gates.

**Rejected.** Claims supplied only by the evaluator: invisible to export and storage, so every consumer
re-derives them; cost is an unprintable, unstorable claim shape. A `roles` member on the metadata: a
second source of truth that drifts from the IdP. A `Facets` string: core meaning gets a typed property,
not a free-form annotation; cost is stringly-typed semantics every consumer must parse.

**Consumer.** F-25 declares `ActorMetadata`, the `Entity.Actor` slot, parsing/printing and analysis.
**Constrains today:** `Entity.cs:53-54` (`Children`, which today has no `Actor`).

### D3 — One synchronous evaluator call in the lowered policy body

**Ruling.** `IPolicyEvaluator.Evaluate(PolicyRequest)` with
`PolicyRequest(Principal, string Entity, object Instance, string Policy, IReadOnlyList<object?> Arguments)`,
in `Poly.Runtime` (R-09), synchronous, returning `DomainResult<bool>`. `Success(true)` allows and
`Success(false)` denies. `Failure` means it cannot decide, and the guard wraps that as `Policy`
(`Poly.Runtime/DomainFailureKind.cs:35`).

The call site is the lowered module's policy body, so the simulator and printed C# share one shape: the
VM's `ModuleInstanceExecutor.EvaluatePolicy`
(`Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs:127`) and the generated
`AddPolicyMethods`/`AddPolicyMethod`
(`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Invariants.cs:58,:69-82`), reached from the guard by
the `this.Name()` call (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.Guards.cs:24`).

**Rejected.** An async interface now: the runtime cannot await until F-31/F-32, so lowering would carry a
suspension point it cannot execute; cost is a second policy path. Per-policy adapter methods: a
registration per name, against "the evaluator is replaceable"; cost is host wiring per policy and a
name-typo difference in allow/deny. Returning `bool`: loses cannot-decide; cost is that a broken
evaluator is indistinguishable from a denial.

**Recorded supersession.** F-31/F-32's awaited host effect replaces this synchronous member. If that
slice lands first, this ADR is amended before code, not after.

**Consumer.** R-09 fixes the interface; F-26 wires the call site. **Constrains today:**
`ModuleInstanceExecutor.cs:127`; `DomainToCSharpExporter.Invariants.cs:58,:69-82`;
`DomainToCSharpExporter.Actions.Guards.cs:24`; `DomainFailureKind.cs:35`.

### D4 — `policy external` exists

**Ruling.** Yes. `Name: policy external` has no body, the printer emits it, and `policy { }` stays a
parse error — never "external". `Policy` gains an explicit body union,
`Policy(string Name, PolicyBody Body)` with `InlineBody(DomainExpression) | ExternalBody()`, and never a
nullable expression: a null body as meaning is the frozen core's null-lower hazard
(`Poly/DomainModeling/Ontology/Policy.cs:8-13` is today's non-nullable shape).

**Rejected.** A body-less `policy`: indistinguishable from a truncated document, and there is nothing to
print. A facet or annotation (`@external`): core meaning gets a typed property; cost is a second reading
of the same declaration. Inferring external from "no body plus an evaluator loaded": the same source
would mean different things per `uses`, and a typo silently becomes a host call. Reusing
`SessionBuilder.AddContract` (`Poly/DomainModeling/Compile/SessionBuilder.cs:211`): an evaluator call is
not an endpoint bind; cost is contract payload and exposure rules leaking into policy evaluation.

**Consumer.** F-25 (parser, ontology, lowering), F-26 (evaluation and the analysis migration).
**Constrains today:** `Policy.cs:8-13`; `DomainResolver.cs:24`; `DomainResolver.Entities.cs:229-264`;
`NameScopes.cs:93`; `DomainToCSharpExporter.Invariants.cs:85-88`.

## Consequences

- **D4's five consequences hold as stated.** (1) *Identity*: an inline body keeps expression instance
  identity — `ReferenceEquals(existing.Expression, policy.Expression)`
  (`Poly/DomainModeling/Analysis/NameScopes.cs:93`, `…/Lowering/DomainToCSharpExporter.Invariants.cs:85-88`)
  — while external policies dedupe by name only. (2) *Negation moves to lowering*: the parser today
  resolves `require not X` into `DomainExpression.Not(expr)`
  (`DomainResolver.Entities.cs:237-239`; stage requires `:259-261`); with the union, `not` wraps the
  policy-method call so it composes with both bodies, `_entityPolicies`
  (`DomainResolver.cs:24`, today `Dictionary<string, DomainExpression>`) becomes
  `Dictionary<string, PolicyBody>`, and `ResolvePendingRequires` (`Entities.cs:229-264`) keeps its key.
  (3) *Opaque analysis*: an external policy has no property walk, no constraint inference and no guard
  proof, and fails closed — it never clears a diagnostic. Today that is 25 `policy.Expression` reads and
  9 `new Policy(` sites under `Poly/` (e.g. `Analysis/PolicyConstraintAnalyzer.cs:30-47`,
  `Analysis/ExpressionTypeAnalyzer.cs:157-168`, `Analysis/ProvableGuards.cs:464`) plus 84 test sites;
  F-25/F-26 migrate them mechanically, each site choosing skip or walk. (4) *Parameterised*: an external
  policy may take parameters (Q18/F-09); `args` are the values the inline body would read, and a stage
  `require` of a parameterised policy stays F-09's positioned error. (5) *Library-error placement*:
  without `uses auth` the error is raised at the declaration and names `uses auth` (Q8), not at each use.
- The synchronous evaluator is a scheduled supersession: F-31/F-32's awaited host effect replaces it.
- `actor` stops being an unsupported-keyword error only when F-25 ships. Until then
  `DomainResolver.cs:251` and the guide's "use `entity`" row (guide:1256) stay truthful, and F-24 edits
  neither.
- No auth semantics enter the frozen core. Core contributes a parameter and types the request; allow,
  deny, claims and roles stay host state. `uses auth` stays opt-in, and a session without it cannot reach
  the evaluator at all.
- No new domain surface: no keyword, no value type, no contract payload. A policy reads only what it
  could read before, and the principal is not authorable.
- Principal threading is invasive: every entry point named in D1 and every C# adapter gains a parameter.
  F-26 budgets the simulator and MCP paths; transport-level RBAC stays out of #408's scope.
- `Policy` construction widens (nine production sites, 84 test sites). The union is compile-enforced, so
  no site is silently wrong, but F-25/F-26 are wide mechanical changes.
- Nothing here changes `docs/CORE.md`, `AGENTS.md`, the DSL guide, `NodeFacts`, corpus or golden output;
  those move with F-25/F-26 when the behaviour lands.

## Non-goals

- No `actor` parsing, printing, analysis or lowering — F-25.
- No `Principal`/`IPolicyEvaluator` code, evaluator registration or dependency injection — R-09.
- No `uses auth`, no auth library, no transport RBAC.
- No `policy external` parser, ontology or analysis work — F-25/F-26.
- No changes to `docs/CORE.md`, `AGENTS.md`, `Poly.Mcp/Docs/poly-dsl-guide.md`, `NodeFacts`, corpus or
  golden output.
- No design of F-25/F-26 internals beyond the shapes named above.

## Related links

- [#408](https://github.com/scoizzle/Poly/issues/408) — actor, authorization, and library-provided policy
  evaluation, and the [owner ruling](https://github.com/scoizzle/Poly/issues/408#issuecomment-6813173307)
  transcribed here.
- [#677](https://github.com/scoizzle/Poly/issues/677) — swarm remediation build order.
- [#690](https://github.com/scoizzle/Poly/issues/690) — theme F, which carries F-24, F-25 and F-26.
- [#659](https://github.com/scoizzle/Poly/issues/659) — the owner-rulings record this decision joins.
- R-09 (theme R) is the provider of D1/D3; F-25 and F-26 are the consumers (see each ruling).
