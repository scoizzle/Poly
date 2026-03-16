# V1 to V2 DomainModeling Cutover Plan

This document inventories V1 DomainModeling artifacts and maps each to a V2 outcome: Replace, Remove, or Defer.

## Scope
- In scope: `Poly/DomainModeling` (excluding `V2/`).
- Out of scope: `Poly/Interpretation`, `Poly/Validation`, and external consumers until cutover commit.

## Inventory and Mapping

| V1 Artifact | V2 Outcome | Target / Rationale |
|---|---|---|
| `DataModel.cs` | Replace | `V2/Core/DomainModel` immutable contract. |
| `DataModelBuilder.cs` | Replace | `V2/Construction` build session and commit-only valid output. |
| `DataModelingContext.cs` | Replace | `V2/Runtime` + `V2/Serving` execution context split. |
| `DataType.cs` | Replace | `V2/Core/DomainType` immutable contract. |
| `DataTypeBuilder.cs` | Replace | `V2/Construction` type builder APIs. |
| `DataProperty.cs` | Replace | `V2/Core/DomainProperty` with `TypeExpression` validation. |
| `DataPropertyPath.cs` | Replace | `V2/Validation` diagnostic path model. |
| `Relationship.cs` | Replace | `V2/Core` relationship contract with explicit invariants. |
| `PropertyFacets.cs` | Replace | `V2/Core` property metadata/facets model. |
| `Lifecycle.cs` | Replace | `V2/Core/LifecycleModel` + state/transition contracts. |
| `Identity.cs` | Replace | `V2/Core/SemanticId` + version value objects. |
| `DataTypeValidator.cs` | Replace | `V2/Validation` deterministic validation pipeline. |
| `Rules/*` | Replace | `V2/Runtime` + `V2/Validation` rule evaluation contracts. |
| `TypeExpressions/*` | Replace | `V2/Core` TypeExpression vocabulary + parser helper. |
| `Builders/PropertyBuilder.cs` | Replace | `V2/Construction` property builder. |
| `Builders/RelationshipBuilder.cs` | Replace | `V2/Construction` relationship builder. |
| `Builders/LifecycleBuilder.cs` | Replace | `V2/Construction` lifecycle builder. |
| `Builders/MutationBuilder.cs` | Replace | `V2/Construction` mutation builder. |
| `Builders/MutationConditionBuilder.cs` | Replace | `V2/Construction` mutation preconditions/guards. |
| `Builders/PreconditionBuilder.cs` | Replace | `V2/Construction` precondition builder API. |
| `Mutations/*` | Replace | `V2/Core` command/mutation/event contracts + runtime execution path. |
| `Events/DomainEvent.cs` | Replace | `V2/Core/DomainEvent` immutable contract. |
| `DataModelTypeDefinitionProvider.cs` | Replace | `V2/Projection` interpretation projection adapters. |
| `DataModelAstExtensions.cs` | Replace | `V2/Projection` explicit projection pipeline. |
| `DataModelPropertyPolymorphicJsonTypeResolver.cs` | Defer | Keep for compatibility bridge during transition; remove after V2 JSON shape stabilizes. |
| `IntrospectionBridge/*` | Defer | Keep bridge until V2 interpretation/read-model projection parity is accepted. |

## Defer List (Must Survive Until Gate)
- V1 polymorphic JSON resolver and adapters needed to read legacy payloads.
- Introspection bridge shims required by current consumers.

## Planned Removal List (On Cutover Commit)
- Delete V1 mutable builders and V1 contracts once V2 equivalents are live and tests pass.
- Delete compatibility shims once no call-sites reference V1 shapes.

## Single-Commit Hard Cutover Checklist
1. V2 contracts compile and all constructor/build-session invariants are enforced.
2. Unit tests pass for V2 value objects, contracts, and builders.
3. Integration tests pass for REST surfaces using V2 evaluator path.
4. Conformance tests pass for REST/MCP diagnostic parity.
5. `Poly/DomainModeling` V1 call-sites are replaced with V2 equivalents.
6. Delete V1 files listed for removal and remove stale references from project files.
7. Run full CI (build + tests + image artifact) on rewrite branch.
8. Produce one migration note summarizing removed APIs and replacements.

## Consumer Update Checklist
- Update internal call-sites from V1 builder types to V2 construction APIs.
- Update serialization callers to V2 contracts/projections.
- Validate no external package exports V1 DomainModeling types.

## Rollback Plan
- Revert the single cutover commit.
- Restore V1 references and run CI.
