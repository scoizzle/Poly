# Poly Data Modeling

`Poly.Data.Modeling` provides a domain-oriented model for entities, relationships, events, rules, and effects, with transactional mutation support and semantic validation.

## Core Concepts

- `Domain`: root object containing `Types` and `Relationships`
- `DomainType`: type-level domain member base
- `Entity`: primary domain type with properties, stages, policies, actions, and events
- `Actor`: specialization of `Entity` representing a principal (user, service) with identity metadata
- `Relationship`: specialized `Entity` linking source and target entities with cardinality
- `Property`, `Policy`, `Rule`, `Action`, `Event`, `Stage`: domain building blocks

Most model types derive from `TypeSystem/DomainObject` and `TypeSystem/DomainMember`.

## Mutation Architecture

Domain changes are performed through mutation commands:

- `DomainMutationCommand`: atomic apply/rollback unit
- `Domain.CreateMutation()`: starts a transactional mutation
- `Domain.Mutation`: fluent command accumulator for multi-step changes
- `DomainMutationExtensions`: one-step shorthand methods that create, apply, and complete a mutation

`Mutation.Apply()` executes all steps, runs domain analysis, and rolls back on error diagnostics.

## Analysis

`Data/Modeling/Analysis` contains analyzers for domain semantics and structure, including:

- `DomainModelAnalyzer`
- `SemanticDomainAnalyzer`
- `StructuralDomainAnalyzer`
- Policy/effect analyzers

These analyzers return `Syntax.Analysis.AnalysisResult` diagnostics and metadata.

## Effects

`Data/Modeling/Effects` models side effects triggered by actions, including entity creation/deletion, stage transitions, relationship linking, and composed effects.

## Minimal Example

```csharp
var domain = new Domain("Commerce");

var customer = new Entity(domain, "Customer");
var order = new Entity(domain, "Order");

domain.CreateMutation()
    .AddType(customer)
    .AddType(order)
    .AddRelationship(new Relationship(
        domain,
        "CustomerOrders",
        customer,
        order,
        RelationshipCardinality.OneToMany,
        sourceOwnsTarget: false))
    .Apply();
```

## Notes

- Use `Domain.CreateMutation()` for grouped, rollback-capable changes.
- Prefer mutation commands/extensions over direct collection manipulation.
- `ChildObjects` on domain members controls traversal for incremental analysis.

## Actors and Access Control

`Actor` is a domain-first model of a principal. It does not carry authentication logic — it describes **what** a principal is and how runtime claims map onto it. Evaluation is performed by the host at runtime.

### Identity Profile

Each `Actor` exposes an `IdentityProfile` (`ActorIdentityProfile`) with three fields:

| Field | Purpose |
|---|---|
| `SubjectProperty` | The actor property that holds the principal subject ID (e.g. `UserId`) |
| `RoleClaimType` | The claim type whose values are treated as role names (e.g. `"role"`) |
| `ClaimMappings` | `IReadOnlyCollection<ActorClaimMapping>` — maps claim types to actor properties |

Configure via mutation:

```csharp
domain.CreateMutation()
    .SetActorSubjectProperty(actor, userIdProperty)
    .SetActorRoleClaimType(actor, "role")
    .AddActorClaimMapping(actor, new ActorClaimMapping("department", deptProperty))
    .Apply();
```

### Policies and Rules

Access control uses the same `Policy` / `Rule` system as field-level validation. A `Policy` holds a list of `Rule`s and an `AggregationStrategy` (`All` or `Any`). Policies attach to `Entity`, `Stage`, or `Property`.

Actor-aware rule types:

| Type | Meaning |
|---|---|
| `ActorTypeRule(actor, ActorType)` | Principal must be an instance of the given `Actor` type |
| `ActorRoleRule(actor, Role)` | Principal must have the given role value |
| `ActorPropertyRule(actor, ActorProperty, Constraints)` | A constraint on one of the actor's own properties |
| `CompositeRule(actor, Left, Right, LogicalOperator)` | Combines two rules with `And` or `Or` |

Non-actor rule types (`PropertyRule`, `CrossPropertyRule`) continue to work on the entity subject as before.

### Implementing UAC

The recommended pattern for access control is:

1. Define `Actor` types for each principal category (e.g. `AdminUser`, `Reviewer`).
2. Configure identity profile so the host can hydrate actor instances from runtime claims.
3. Create `Policy` objects on the `Entity`, `Stage`, or `Property` being protected.
4. Add `ActorTypeRule` / `ActorRoleRule` / `CompositeRule` to those policies.
5. Attach the policy to the target via mutation.

```csharp
// 1. Actor type
var admin = new Actor(domain, "AdminUser");

// 2. Policy on a stage
var reviewPolicy = new Policy(domain, "CanEnterReview");

// 3. Rules
var mustBeAdmin = new ActorTypeRule(domain, "MustBeAdmin", admin);
var mustHaveApproveRole = new ActorRoleRule(domain, "MustHaveApproveRole", "Approve");
var combined = new CompositeRule(domain, "AdminWithApproveRole", mustBeAdmin, mustHaveApproveRole, LogicalOperator.And);

// 4. Wire up
domain.CreateMutation()
    .AddType(admin)
    .AddPolicy(reviewStage, reviewPolicy)
    .AddRule(reviewPolicy, combined)
    .Apply();
```

Actor rules currently produce `NotSupportedException` in `DomainLoweringGenerator.LowerRule` — they require actor evaluation context that the host supplies at runtime. Structural `CompositeRule` nodes lower normally by recursing into their left/right children.

### Intent-Based Authoring

All UAC operations are available as `DomainMutationIntent` subtypes for MCP / serialized authoring:

```
AddPolicyToEntityIntent, RemovePolicyFromEntityIntent
AddPolicyToStageIntent,  RemovePolicyFromStageIntent
AddPolicyToPropertyIntent, RemovePolicyFromPropertyIntent
AddActorTypeRuleToPolicyIntent
AddActorRoleRuleToPolicyIntent
AddCompositeRuleToPolicyIntent
RemoveRuleFromPolicyIntent
```

### Adding a New Rule Type

1. Declare a `sealed record MyRule(...) : Rule(Domain, Name)` (alongside similar rules).
2. Add a case in `DomainLoweringGenerator.LowerRule` — throw `NotSupportedException` if runtime context is required.
3. Add an intent record to `DomainMutationIntent.cs` with a `[JsonDerivedType]` registration.
4. Add an engine case in `DomainMutationIntentEngine.cs`.
5. Add an MCP tool in `DomainTools.cs` and an affordance in `DomainAffordances.SessionRoot()`.