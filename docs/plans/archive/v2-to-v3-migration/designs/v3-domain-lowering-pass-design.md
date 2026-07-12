# V3 Domain Lowering Pass — Design Plan

## Directory Structure and File Organization

```
Poly/DomainModeling/LoweringPass/
  V3DomainLoweringPass.cs              # Top-level orchestrator (mirrors DomainImplementationLoweringPass)
  V3DomainImplementationModel.cs        # Output records (mirrors DomainImplementationModel, EntityImplementationModel, StageImplementationModel)
  V3DomainExpressionLoweringPass.cs     # DomainExpression → Syntax.Node expression tree
  V3EffectLoweringPass.cs               # Effect subtype → method body AST nodes
  V3ConstraintLoweringPass.cs           # Constraint → Syntax.Node guard expressions
  V3EntityLoweringPass.cs               # Entity → TypeDefinitionNode
  V3RelationshipLoweringPass.cs         # Relationship → TypeDefinitionNode
  V3EventLoweringPass.cs                # Event → TypeDefinitionNode
  V3ValueTypeLoweringPass.cs            # ValueType → TypeDefinitionNode
  V3PrimitiveTypeMapping.cs             # PrimitiveType/TypeCategory → Syntax type references
  V3StageLoweringPass.cs                # Stage → enum members, stage guards, on-entry/on-exit effects
  V3ContractIntegrationLoweringPass.cs  # ImportedContract/ContractBinding → interface types
  V3EventSubscriptionLoweringPass.cs    # EventSubscription → handler method definitions
  V3AnalysisContext.cs                  # Wraps AnalysisResult with strongly-typed V3 lookups
```

**Namespace:** `Poly.DomainModeling.LoweringPass`

---

## Output Records

### V3DomainImplementationModel

```csharp
public sealed record V3DomainImplementationModel(
    Domain Domain,
    IReadOnlyCollection<V3EntityImplementationModel> Entities,
    IReadOnlyCollection<Relationship> Relationships) : Node;
```

### V3EntityImplementationModel

```csharp
public sealed record V3EntityImplementationModel(
    Entity Entity,
    IReadOnlyCollection<Property> EffectiveProperties,
    IReadOnlyCollection<Action> EffectiveActions,
    IReadOnlyCollection<Policy> EffectivePolicies,
    IReadOnlyCollection<DomainTypeReference> EffectiveEvents,
    IReadOnlyCollection<Relationship> EffectiveRelationships,
    IReadOnlyCollection<V3StageImplementationModel> EffectiveStages) : Node;
```

### V3StageImplementationModel

```csharp
public sealed record V3StageImplementationModel(
    Stage Stage,
    IReadOnlyCollection<Action> EffectiveActions,
    IReadOnlyCollection<Policy> EffectivePolicies) : Node;
```

---

## V3AnalysisContext

Wraps `AnalysisResult` for V3 lookups:

```csharp
public sealed class V3AnalysisContext {
    public V3AnalysisContext(AnalysisResult analysis);
    public EffectiveMemberMetadata? GetEffectiveMemberMetadata(Entity entity);
    public StageLineageMetadata? GetStageLineageMetadata(Stage stage);
    public DomainType? GetResolvedTypeReference(DomainTypeReference reference);
    public Node? GetNodeReplacement(Node node);
    public bool HasErrors { get; }
}
```

---

## Expression Lowering: DomainExpression → Syntax.Node

### V3DomainExpressionLoweringPass

Maps each `DomainExpression` subtype to a `Syntax.Node`:

| DomainExpression node    | Syntax.Node output                                          |
|-------------------------|-------------------------------------------------------------|
| `PropertyAccess`        | `Member(entityInstance, name)`                               |
| `ParameterAccess`       | `Parameter(name, resolvedType)`                              |
| `Literal`               | `Constant(value)`                                            |
| `OwnedAccess`           | `Member(Lower(inner), ownedName)`                            |
| `Exists`                | `NotEqual(Lower(target), Null)`                              |
| `NotExists`             | `Equal(Lower(target), Null)`                                 |
| `Add`                   | `Add(Lower(left), Lower(right))`                             |
| `Subtract`              | `Subtract(Lower(left), Lower(right))`                        |
| `Multiply`              | `Multiply(Lower(left), Lower(right))`                        |
| `Divide`                | `Divide(Lower(left), Lower(right))`                          |
| `And`                   | `And(Lower(left), Lower(right))`                             |
| `Or`                    | `Or(Lower(left), Lower(right))`                              |
| `Not`                   | `Not(Lower(operand))`                                        |
| `DateOperation`         | Method calls (`AddDays`/`AddMonths`/`DiffDays`)               |
| `RelationshipNavigation`| `Member(Lower(targetProperty), relationshipName)`            |

**Algorithm:** `Lower(expression, entityInstance, parameterNames)` recursively walks the DomainExpression tree. `PropertyAccess` resolves based on whether the name matches a parameter or entity property.

---

## Effect Lowering: Effect → Method Body AST

| Effect subtype              | Syntax.Node output                                                                    |
|----------------------------|--------------------------------------------------------------------------------------|
| `AssignEffect`             | `Assignment(Lower(Target), Lower(Value))`                                            |
| `CreateEntityInstance`     | `Invoke(Member(NamedTypeRef(type), "TryCreate"), [context, ..initializers])`         |
| `DeleteEntityInstance`     | `Invoke(entityInstance.GetMember("Remove"))`                                         |
| `InvokeActionEffect`       | `Invoke(entityInstance, actionName, [context, ..parameterBindings])`                 |
| `PublishEventEffect`       | `Invoke(Member(executionContext, "PublishEvent"), New(eventType, ..bindings))`       |
| `StageTransitionEffect`    | `Assignment(entityInstance.CurrentStage, Member(stageEnumType, targetStage))`        |
| `CompositeEffect`          | `Block(LowerSequential(children))`                                                   |
| `ConditionalEffect`        | `IfStatement(Lower(Condition), Block(Lower(ThenEffects)), Else: Block(Lower(Else)))` |
| `LinkRelationshipEffect`   | `Invoke(entityInstance.RelationshipName.Add, Lower(Target))`                         |
| `UnlinkRelationshipEffect` | `Invoke(entityInstance.RelationshipName.Remove, Lower(Target))`                      |
| `TransitionRelationshipEffect` | `Assignment(entityInstance.RelationshipName.CurrentStage, Member(stageEnum, target))` |

---

## Entity → TypeDefinitionNode

Each entity lowers to a `TypeDefinitionNode` with:

1. **Properties**: `Property` → `PropertyDefinitionNode(name, typeNode, Getter, Setter)`. Type resolved via `V3PrimitiveTypeMapping` (PrimitiveType → `PrimitiveTypeReference`, others → `NamedTypeReference`).
2. **Synthesized properties**: `CurrentStage` enum if entity has stages, relationship navigation properties (one-to-one as nullable ref, one-to-many as `IReadOnlyCollection<T>`).
3. **Actions → MethodDefinitionNode**: Method with `context` parameter + domain type parameters. Body: policy guards first, then effect block, then return `Result.Success()`/`Result.Failure()`.
4. **Constructor**: Private constructor taking all domain properties.
5. **`TryCreate` static method**: Validates entity-level policies, constructs instance, returns `Result<EntityType>`.
6. **Inheritance**: If `Entity.ParentEntityName` is set, base type is `NamedTypeReference(parentEntityName)`.

---

## Stage → Enum + Guards

- Produce a `TypeDefinitionNode` with `TypeCategory.Enumeration` containing one `FieldDefinitionNode` per stage.
- Stage ordering determined by `StageLineageMetadata.Ancestors` depth.
- On-entry/on-exit effects lowered to private methods per stage (`OnEnter{StageName}`, `OnExit{StageName}`), invoked by transition actions.
- Stage guards for actions: guard that current stage equals the source stage (ordered predecessor).

---

## Policies → Guard Expressions

Each `Policy.Expression` lowered via `V3DomainExpressionLoweringPass.Lower(expr, entityInstance, parameterNames)`. Multiple policies on the same scope are combined via AND at the method-body level.

---

## Event Subscriptions → Handler Methods

Per subscription, produce a `MethodDefinitionNode`:
- Name: `On{EventTypeName}`
- Parameters: `(context, eventParameter)`
- Body: invoke handler action with event property → parameter mappings
- Correlation bindings produce `Member(eventParam, eventPropertyName)` → filtering checks

---

## Relationships → Navigation Properties + Relationship Types

- Synthetic navigation properties on source/target entities (OneToOne/ManyToOne: nullable ref, OneToMany/ManyToMany: `IReadOnlyCollection<T>`).
- Relationship itself lowered to `TypeDefinitionNode` with `Source`, `Target`, payload `Properties`, `CurrentStage`, private constructor + `TryCreate`, relationship-level policies as guards.

---

## Contract Integration → Interface Implementations

Mirrors V2 `LowerToContractInterfaces()`:
- Entity contract interfaces: `I{EntityName}` with property getters
- Stage contract interfaces: `I{StageName}{EntityName}` with stage-effective action signatures
- `ImportedContract` → empty interface with `ContractEndpoint` method signatures
- `ContractBinding` → method implementations delegating to target actions with `ContractFieldMap` field mappings

---

## PrimitiveType/ValueType → Simple Type Definitions

- **PrimitiveType**: No `TypeDefinitionNode`. `V3PrimitiveTypeMapping` provides runtime mapping via `TypeCategory` → `PrimitiveTypeReference` switch.
- **ValueType**: Lowered to `TypeDefinitionNode` with `TypeDefinitionSemantics.ImmutableValue`, primary constructor from properties, no methods/stages/policies.
- **Event**: Same as ValueType — primary constructor, `ImmutableValue` semantics.

---

## Analysis Metadata Integration

`V3AnalysisContext` wraps `AnalysisResult` and provides:
- `EffectiveMemberMetadata` (from `SemanticDomainAnalyzer`): effective properties/actions/policies/events per entity
- `StageLineageMetadata` (from `SemanticDomainAnalyzer`): depth, ancestors for stage ordering and guards
- `ResolvedTypeReferenceMetadata` (from `StructuralDomainAnalyzer`): attached to each `DomainTypeReference`
- `NodeReplacement`: `AnalysisResult.GetNodeReplacement(node)` — allows analyzers to rewrite pre-lowering

**Pipeline flow:**

```
Domain → DomainModelAnalyzer → AnalysisResult (with metadata)
                                    ↓
         V3DomainLoweringPass.Lower() → V3DomainImplementationModel
                                    ↓
         V3DomainLoweringPass.LowerToTypeDefinitions() → TypeDefinitionNode[]
                                    ↓
         CSharpGenerator.Generate(typeDefs) → C# source string
```

---

## Pipeline Wiring

The new pass should be a drop-in replacement for `DomainImplementationLoweringPass`:

```csharp
var loweringPass = new V3DomainLoweringPass();
var typeDefs = loweringPass.LowerToTypeDefinitions(domain, analysis);
var contractIfs = loweringPass.LowerToContractInterfaces(domain, analysis);
```

No changes to V2 code. Detection: if `domain` is `Poly.DomainModeling.Domain`, use V3 pass.

---

## Gaps in V3 DomainModeling Types

1. **Stage-level required property analysis**: The V3 `PolicyConstraintAnalyzer.AnalyzeStage` passes `null` entity/propMap, so stage-level `RequiredPropertiesMetadata` is never populated. Either fix `AnalyzeStage` to receive entity context, or defer entity-level `RequiredPropertiesMetadata` fallback (as done in EffectAnalyzer).

2. **No domain type resolution lookup**: The lowering pass needs to resolve `DomainTypeReference` to actual `DomainType`. Currently provided by `SemanticDomainAnalyzer` via `ResolvedTypeReferenceMetadata` on each `DomainTypeReference` node.

3. **Stage ordering**: V3 stages form a tree via `Stage.Parent: StageReference?`. The `StageLineageMetadata` provides `Depth` and `Ancestors` for topological sort.

4. **Actor detection**: Currently no `IsActor` flag on `Entity`. The mutation API has `AddActor` vs `AddEntity` but the resulting `Entity` record is the same type. Options: add `IsActor: bool` to `Entity`, or store in analysis metadata.

---

## Implementation Order

1. `V3PrimitiveTypeMapping` — foundational
2. `V3DomainImplementationModel` — output records
3. `V3AnalysisContext` — analysis wrapper
4. `V3DomainExpressionLoweringPass` — expression tree lowering
5. `V3ConstraintLoweringPass` — constraint lowering
6. `V3EffectLoweringPass` — effect lowering
7. `V3EventLoweringPass` + `V3ValueTypeLoweringPass` — simple types
8. `V3StageLoweringPass` — stage enum + guards
9. `V3EntityLoweringPass` — entity lowering (depends on everything above)
10. `V3RelationshipLoweringPass` — relationship lowering
11. `V3EventSubscriptionLoweringPass` — handler methods
12. `V3ContractIntegrationLoweringPass` — contract interfaces
13. `V3DomainLoweringPass` — orchestrator (wires everything)
14. Pipeline integration — update calling code to use V3 pass for V3 domains
