# Poly Data Modeling

`Poly.Data.Modeling` provides a domain-oriented model for entities, relationships, events, rules, and effects, with transactional mutation support and semantic validation.

## Core Concepts

- `Domain`: root object containing `Types` and `Relationships`
- `DomainType`: type-level domain member base
- `Entity`: primary domain type with properties, stages, policies, actions, and events
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