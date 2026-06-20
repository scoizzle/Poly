using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;
using Poly.Syntax.Nodes;

namespace Poly.Data.Modeling;

/// <summary>
/// Lowers Domain Modeling syntax clauses into executable interpretation AST nodes.
/// The lowering process is contextualized by analysis results.
/// </summary>
public sealed class DomainLoweringGenerator {
    private readonly AnalysisResult _analysis;

    public DomainLoweringGenerator(AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        _analysis = analysis;
    }

    public Node Lower(Node root) {
        ArgumentNullException.ThrowIfNull(root);

        var lowered = LowerCore(root);

        // Honor replacement metadata produced by analyzers for the lowered output.
        return lowered;
    }

    private Node LowerCore(Node expression) {
        if (_analysis.GetNodeReplacement(expression) is Node replacement) {
            expression = replacement;
        }

        return expression switch {
            And and => new And(LowerCore(and.LeftHandValue), LowerCore(and.RightHandValue)),
            Or or => new Or(LowerCore(or.LeftHandValue), LowerCore(or.RightHandValue)),
            Equal equal => new Equal(LowerCore(equal.LeftHandValue), LowerCore(equal.RightHandValue)),
            NotEqual notEqual => new NotEqual(LowerCore(notEqual.LeftHandValue), LowerCore(notEqual.RightHandValue)),
            GreaterThanOrEqual greaterThanOrEqual => new GreaterThanOrEqual(LowerCore(greaterThanOrEqual.LeftHandValue), LowerCore(greaterThanOrEqual.RightHandValue)),
            LessThanOrEqual lessThanOrEqual => new LessThanOrEqual(LowerCore(lessThanOrEqual.LeftHandValue), LowerCore(lessThanOrEqual.RightHandValue)),
            Member memberAccess => new Member(LowerCore(memberAccess.Value), memberAccess.MemberName),
            Constant constant => new Constant(constant.Value),
            _ => expression
        };
    }

    // ── Policy / Rule / Constraint lowering ──────────────────────────────────

    public static Node LowerPolicy(Policy policy, Node subject, ActorEvaluationContext? actorContext = null) {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(subject);

        if (policy.Rules.Count == 0) {
            return True;
        }

        var nodes = policy.Rules.Select(rule => LowerRule(rule, subject, actorContext));

        return policy.AggregationStrategy switch {
            PolicyAggregationStrategy.All => nodes.Aggregate(static (acc, node) => new And(acc, node)),
            PolicyAggregationStrategy.Any => nodes.Aggregate(static (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException($"Unknown aggregation strategy '{policy.AggregationStrategy}'.")
        };
    }

    public static Node LowerRule(Rule rule, Node subject, ActorEvaluationContext? actorContext = null) {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(subject);

        return rule switch {
            PropertyRule propertyRule => LowerConstraint(propertyRule.Constraints, GetSubjectValueNode(subject, propertyRule.Value), MapDomainTypeToNode(propertyRule.Value.Type)),
            CrossPropertyRule crossRule => LowerCrossProperty(crossRule, subject),
            CompositeRule composite => composite.Operator switch {
                LogicalOperator.And => new And(LowerRule(composite.Left, subject, actorContext), LowerRule(composite.Right, subject, actorContext)),
                LogicalOperator.Or => new Or(LowerRule(composite.Left, subject, actorContext), LowerRule(composite.Right, subject, actorContext)),
                _ => throw new InvalidOperationException($"Unknown logical operator '{composite.Operator}'.")
            },
            ActorTypeRule actorTypeRule => actorContext is not null
                ? new TypeIs(actorContext.ActorSubject, new TypeReference(actorTypeRule.ActorType.Name))
                : throw new NotSupportedException($"'{nameof(ActorTypeRule)}' requires an {nameof(ActorEvaluationContext)}."),
            ActorRoleRule actorRoleRule => actorContext is not null
                ? new Invoke(new Member(new NullForgiving(actorContext.ActorSubject), "IsInRole"), new Constant(actorRoleRule.Role))
                : throw new NotSupportedException($"'{nameof(ActorRoleRule)}' requires an {nameof(ActorEvaluationContext)}."),
            ActorPropertyRule actorPropertyRule => actorContext is not null
                ? LowerActorPropertyRule(actorPropertyRule, actorContext)
                : throw new NotSupportedException($"'{nameof(ActorPropertyRule)}' requires an {nameof(ActorEvaluationContext)}."),
            _ => throw new NotSupportedException($"Unknown rule type '{rule.GetType().Name}'.")
        };
    }

    private static Node LowerActorPropertyRule(ActorPropertyRule actorPropertyRule, ActorEvaluationContext actorContext) {
        var actorProperty = actorPropertyRule.ActorProperty;
        var actorType = actorPropertyRule.Domain.Types
            .OfType<Actor>()
            .FirstOrDefault(actor => actor.Properties.Any(property =>
                ReferenceEquals(property, actorProperty) ||
                string.Equals(property.Name, actorProperty.Name, StringComparison.Ordinal)));

        if (actorType is null) {
            throw new NotSupportedException($"Could not resolve an owning actor type for actor property '{actorProperty.Name}'.");
        }

        var actorTypeRef = new NamedTypeReference(actorType.Name);
        var typedActor = new TypeCast(new NullForgiving(actorContext.ActorSubject), actorTypeRef);

        return new And(
            new TypeIs(actorContext.ActorSubject, actorTypeRef),
            LowerConstraint(
                actorPropertyRule.Constraints,
                new Member(typedActor, actorProperty.Name),
                MapDomainTypeToNode(actorProperty.Type)));
    }

    public static Node LowerConstraint(Constraint constraint, Node value, Node? valueType = null) {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(value);

        return constraint switch {
            RequiredConstraint => LowerRequiredConstraint(value, valueType),
            EqualityConstraint eq => new Equal(value, Wrap(eq.Value)),
            RangeConstraint range => LowerRange(range, value),
            LengthConstraint length => LowerLength(length, value),
            EnumConstraint @enum => LowerEnum(@enum, value),
            ConstraintSet set => LowerConstraintSet(set, value, valueType),
            _ => throw new NotSupportedException($"Unknown constraint type '{constraint.GetType().Name}'.")
        };
    }

    private static Node LowerRequiredConstraint(Node value, Node? valueType) {
        return CanTypeBeNull(valueType) ? new NotEqual(value, Null) : True;
    }

    private static Node LowerEnum(EnumConstraint @enum, Node value) {
        if (@enum.Members.Count == 0) {
            return False;
        }

        return @enum.Members
            .Select(member => (Node)new Equal(value, Wrap(member.EffectiveCanonicalValue)))
            .Aggregate((Node)False, static (acc, check) => new Or(acc, check));
    }

    private static Node LowerRange(RangeConstraint range, Node value) {
        Node? minCheck = range.MinValue is null ? null : new GreaterThanOrEqual(value, Wrap(range.MinValue));
        Node? maxCheck = range.MaxValue is null ? null : new LessThanOrEqual(value, Wrap(range.MaxValue));

        return (minCheck, maxCheck) switch {
            (Node min, Node max) => new And(min, max),
            (Node min, null) => min,
            (null, Node max) => max,
            _ => True
        };
    }

    private static Node LowerLength(LengthConstraint length, Node value) {
        var len = value.GetMember("Length");
        Node? minCheck = length.MinLength.HasValue ? new GreaterThanOrEqual(len, Wrap(length.MinLength.Value)) : null;
        Node? maxCheck = length.MaxLength.HasValue ? new LessThanOrEqual(len, Wrap(length.MaxLength.Value)) : null;

        return (minCheck, maxCheck) switch {
            (Node min, Node max) => new And(min, max),
            (Node min, null) => min,
            (null, Node max) => max,
            _ => Wrap(true)
        };
    }

    private static Node LowerConstraintSet(ConstraintSet set, Node value, Node? valueType) {
        if (set.Constraints.Count == 0) {
            return True;
        }

        var nodes = set.Constraints.Select(c => LowerConstraint(c, value, valueType));

        return set.AggregationStrategy switch {
            ConstraintAggregationStrategy.All => nodes.Aggregate(static (acc, node) => new And(acc, node)),
            ConstraintAggregationStrategy.Any => nodes.Aggregate(static (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException($"Unknown aggregation strategy '{set.AggregationStrategy}'.")
        };
    }

    private static Node LowerCrossProperty(CrossPropertyRule rule, Node subject) {
        var left = GetSubjectValueNode(subject, rule.Left);
        var right = GetSubjectValueNode(subject, rule.Right);

        return rule.Operator switch {
            DomainComparisonOperator.Equal => new Equal(left, right),
            DomainComparisonOperator.NotEqual => new NotEqual(left, right),
            DomainComparisonOperator.GreaterThan => new GreaterThan(left, right),
            DomainComparisonOperator.GreaterThanOrEqual => new GreaterThanOrEqual(left, right),
            DomainComparisonOperator.LessThan => new LessThan(left, right),
            DomainComparisonOperator.LessThanOrEqual => new LessThanOrEqual(left, right),
            _ => throw new InvalidOperationException($"Unknown comparison operator '{rule.Operator}'.")
        };
    }

    private static Node GetSubjectValueNode(Node subject, DomainValue value) {
        return subject is ParameterReference
            ? new Parameter(ToSynthesizedParameterName(value.Name), MapDomainTypeToNode(value.Type))
            : subject.GetMember(value.Name);
    }

    public static bool TryGetConstantBoolean(Node node, out bool value) {
        if (node is Constant { Value: bool boolValue }) {
            value = boolValue;
            return true;
        }

        value = false;
        return false;
    }

    // ── Effect lowering ─────────────────────────────────────────────────────

    public static Node LowerEffect(Effect effect, Node entityInstance,
        IReadOnlyCollection<EventSubscription>? availableSubscriptions = null,
        string? entityName = null,
        IReadOnlySet<string>? parameterNames = null,
        Node? executionContext = null) {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(entityInstance);

        return effect switch {
            Assign assign => LowerAssignEffect(assign, entityInstance, parameterNames),
            PublishEvent publishEvent => LowerPublishEventEffect(publishEvent, entityInstance, availableSubscriptions, parameterNames, executionContext),
            InvokeAction invokeAction => LowerInvokeActionEffect(invokeAction, entityInstance, parameterNames),
            CreateEntityInstance createEntity => new Invoke(
                new Member(new NamedTypeReference(createEntity.EntityType.Name), "TryCreate"),
                (executionContext is not null
                    ? new[] { executionContext }
                    : Array.Empty<Node>())
                .Concat(createEntity.EntityType.Properties
                    .Select(static property => GetDefaultNodeForType(MapDomainTypeToNode(property.Type))))
                .ToArray()),
            DeleteEntityInstance deleteEntity => new Invoke(entityInstance.Invoke("Remove"), new Member(entityInstance, deleteEntity.EntityType.Name)),
            StageTransition stageTransition => LowerStageTransition(stageTransition, entityInstance, entityName),
            LinkRelationship linkRel => new Invoke(entityInstance.GetMember(linkRel.Relationship.Name + ".Add"), MapDomainValueToNode(linkRel.Target, entityInstance, parameterNames)),
            UnlinkRelationship unlinkRel => new Invoke(entityInstance.GetMember(unlinkRel.Relationship.Name + ".Remove"), MapDomainValueToNode(unlinkRel.Target, entityInstance, parameterNames)),
            TransitionRelationship transitionRel => LowerTransitionRelationship(transitionRel, entityInstance, entityName),
            Effects.Conditional conditional => LowerConditionalEffect(conditional, entityInstance, entityName, parameterNames, executionContext),
            Composite composite => LowerCompositeEffect(composite, entityInstance, entityName, parameterNames, executionContext),
            _ => throw new NotSupportedException($"Unknown effect type '{effect.GetType().Name}'.")
        };
    }

    private static Node LowerStageTransition(StageTransition stageTransition, Node entityInstance, string? entityName) {
        Node value = entityName is not null
            ? new Member(new NamedTypeReference($"{entityName}Stage"), stageTransition.TargetStage.Name)
            : new Constant(stageTransition.TargetStage.Name);
        return new Assignment(entityInstance.GetMember("CurrentStage"), value);
    }

    private static Node LowerTransitionRelationship(TransitionRelationship transitionRel, Node entityInstance, string? entityName) {
        Node value = entityName is not null
            ? new Member(new NamedTypeReference($"{entityName}Stage"), transitionRel.TargetStage.Name)
            : new Constant(transitionRel.TargetStage.Name);
        return new Assignment(
            entityInstance.GetMember(transitionRel.Relationship.Name).GetMember("CurrentStage"),
            value);
    }

    private static Node LowerAssignEffect(Assign assign, Node entityInstance, IReadOnlySet<string>? parameterNames) {
        var target = assign.Target is null
            ? entityInstance
            : entityInstance.GetMember(assign.Target.Name);
        var value = assign.Value is null
            ? Null
            : MapDomainValueToNode(assign.Value, entityInstance, parameterNames);
        return new Assignment(target, value);
    }

    private static Node LowerPublishEventEffect(PublishEvent publishEvent, Node entityInstance,
        IReadOnlyCollection<EventSubscription>? availableSubscriptions,
        IReadOnlySet<string>? parameterNames,
        Node? executionContext) {
        if (executionContext is null) {
            throw new NotSupportedException($"'{nameof(PublishEvent)}' requires an execution context.");
        }

        var eventArguments = publishEvent.Event.Properties
            .Select(property => {
                var eventPropertyType = MapDomainTypeToNode(property.Type);
                if (publishEvent.PropertyBindings.TryGetValue(property.Name, out var source)) {
                    var value = source switch {
                        EventPropertyBindingSource.ActionParameter actionParam => parameterNames?.Contains(actionParam.ParameterName) == true
                            ? new Parameter(actionParam.ParameterName, MapDomainTypeToNode(property.Type))
                            : new Member(entityInstance, actionParam.ParameterName),
                        EventPropertyBindingSource.EntityProperty entityProp => new Member(entityInstance, entityProp.PropertyName),
                        _ => GetDefaultNodeForType(eventPropertyType)
                    };

                    return ApplyNullForgivingIfNeeded(value, eventPropertyType);
                }

                return GetDefaultNodeForType(eventPropertyType);
            })
            .ToArray();

        var eventInstance = new New(new NamedTypeReference(publishEvent.Event.Name), eventArguments);

        return new Invoke(
            new Member(executionContext, "Events"),
            eventInstance);
    }

    private static Node LowerInvokeActionEffect(InvokeAction invokeAction, Node entityInstance, IReadOnlySet<string>? parameterNames) {
        var args = invokeAction.TargetAction.Parameters
            .OfType<Property>()
            .Select(param => {
                if (invokeAction.ParameterBindings.TryGetValue(param.Name, out var binding)) {
                    return MapDomainValueToNode(binding, entityInstance, parameterNames);
                }
                return Null;
            })
            .ToArray();

        return entityInstance.Invoke(invokeAction.TargetAction.Name, args);
    }

    private static Node LowerConditionalEffect(Effects.Conditional conditional, Node entityInstance, string? entityName = null, IReadOnlySet<string>? parameterNames = null, Node? executionContext = null) {
        var effects = conditional.ChildEffects
            .Select(e => LowerEffect(e, entityInstance, entityName: entityName, parameterNames: parameterNames, executionContext: executionContext))
            .Where(n => !ReferenceEquals(n, True))
            .ToArray();

        if (effects.Length == 0) return True;

        var thenBlock = effects.Length == 1 ? effects[0] : new Block(effects);
        return new IfStatement(conditional.Condition, thenBlock);
    }

    private static Node LowerCompositeEffect(Composite composite, Node entityInstance, string? entityName = null, IReadOnlySet<string>? parameterNames = null, Node? executionContext = null) {
        var effects = composite.ChildEffects
            .Select(e => LowerEffect(e, entityInstance, entityName: entityName, parameterNames: parameterNames, executionContext: executionContext))
            .ToArray();

        if (effects.Length == 0) {
            return True;
        }

        if (effects.Length == 1) {
            return effects[0];
        }

        return new Block(effects);
    }

    // ── Type mapping utilities ───────────────────────────────────────────────

    public static Node MapDomainTypeToNode(DomainType type) {
        ArgumentNullException.ThrowIfNull(type);

        if (type is Primitive primitive) {
            return primitive.Name switch {
                "Boolean" or "bool" => new PrimitiveTypeReference(PrimitiveType.Boolean),
                "Number" or "int" or "Int64" => new PrimitiveTypeReference(PrimitiveType.Int64),
                "Text" or "string" => new PrimitiveTypeReference(PrimitiveType.String),
                "Date" or "date" => new PrimitiveTypeReference(PrimitiveType.DateOnly),
                "Time" or "time" => new PrimitiveTypeReference(PrimitiveType.TimeOnly),
                "DateTime" or "datetime" or "instant" => new PrimitiveTypeReference(PrimitiveType.DateTime),
                "Decimal" or "decimal" => new PrimitiveTypeReference(PrimitiveType.Decimal),
                "Duration" or "duration" => new PrimitiveTypeReference(PrimitiveType.TimeSpan),
                "Uuid" or "uuid" or "Guid" => new PrimitiveTypeReference(PrimitiveType.Guid),
                "Binary" or "binary" => new PrimitiveTypeReference(PrimitiveType.ByteArray),
                _ => MapPrimitiveByCategory(primitive)
            };
        }

        return new NamedTypeReference(type.Name);
    }

    private static Node MapPrimitiveByCategory(Primitive primitive) {
        var cat = primitive.Category;
        if (cat.Is(TypeCategory.DateTime)) return new PrimitiveTypeReference(PrimitiveType.DateTime);
        if (cat.Is(TypeCategory.DateOnly)) return new PrimitiveTypeReference(PrimitiveType.DateOnly);
        if (cat.Is(TypeCategory.TimeOfDay)) return new PrimitiveTypeReference(PrimitiveType.TimeOnly);
        if (cat.Is(TypeCategory.Instant)) return new PrimitiveTypeReference(PrimitiveType.DateTime);
        if (cat.Is(TypeCategory.Duration)) return new PrimitiveTypeReference(PrimitiveType.TimeSpan);
        if (cat.Is(TypeCategory.Temporal)) return new PrimitiveTypeReference(PrimitiveType.DateTime);
        if (cat.Is(TypeCategory.Boolean)) return new PrimitiveTypeReference(PrimitiveType.Boolean);
        if (cat.Is(TypeCategory.Integer)) return new PrimitiveTypeReference(PrimitiveType.Int64);
        if (cat.Is(TypeCategory.HighPrecision)) return new PrimitiveTypeReference(PrimitiveType.Decimal);
        if (cat.Is(TypeCategory.FloatingPoint)) return new PrimitiveTypeReference(PrimitiveType.Float64);
        if (cat.Is(TypeCategory.Numeric)) return new PrimitiveTypeReference(PrimitiveType.Int64);
        if (cat.Is(TypeCategory.Identifier)) return new PrimitiveTypeReference(PrimitiveType.Guid);
        if (cat.Is(TypeCategory.Binary)) return new PrimitiveTypeReference(PrimitiveType.ByteArray);
        if (cat.Is(TypeCategory.Text)) return new PrimitiveTypeReference(PrimitiveType.String);
        if (cat.Is(TypeCategory.Primitive)) return new PrimitiveTypeReference(PrimitiveType.Boolean);
        return new NamedTypeReference(primitive.Name);
    }

    public static Node GetDefaultNodeForType(Node typeNode) {
        return typeNode switch {
            PrimitiveTypeReference prim => prim.PrimitiveId switch {
                PrimitiveType.String => new Constant(""),
                PrimitiveType.Boolean => new Constant(false),
                PrimitiveType.Int64 => new Constant(0L),
                PrimitiveType.Int32 => new Constant(0),
                PrimitiveType.Decimal => new Constant(0m),
                PrimitiveType.Float64 => new Constant(0.0),
                PrimitiveType.ByteArray => new NullForgiving(new Default(typeNode)),
                _ => new Default()
            },
            OptionalTypeReference => new Default(typeNode),
            NamedTypeReference or CollectionTypeReference or MapTypeReference => new NullForgiving(new Default(typeNode)),
            _ => new Default()
        };
    }

    public static string ToSynthesizedParameterName(string name) {
        if (string.IsNullOrEmpty(name)) {
            return name;
        }

        if (!char.IsUpper(name[0])) {
            return name;
        }

        var uppercasePrefixLength = 1;
        while (uppercasePrefixLength < name.Length && char.IsUpper(name[uppercasePrefixLength])) {
            uppercasePrefixLength++;
        }

        if (uppercasePrefixLength == 1) {
            return $"{char.ToLowerInvariant(name[0])}{name[1..]}";
        }

        if (uppercasePrefixLength == name.Length) {
            return name.ToLowerInvariant();
        }

        var normalizedPrefixLength = uppercasePrefixLength - 1;
        return string.Concat(
            name[..normalizedPrefixLength].ToLowerInvariant(),
            name[normalizedPrefixLength..]);
    }

    private static bool CanTypeBeNull(Node? typeNode) {
        return typeNode switch {
            null => true,
            OptionalTypeReference => true,
            PrimitiveTypeReference { PrimitiveId: PrimitiveType.String or PrimitiveType.ByteArray } => true,
            PrimitiveTypeReference => false,
            _ => true
        };
    }

    public static Node ApplyNullForgivingIfNeeded(Node expression, Node typeNode) {
        return CanTypeBeNull(typeNode) ? new NullForgiving(expression) : expression;
    }

    private static Node MapDomainValueToNode(DomainValue value, Node entityInstance, IReadOnlySet<string>? parameterNames = null) {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(entityInstance);

        if (value is EffectValueRef effectRef) {
            return new Member(entityInstance, effectRef.OutputName);
        }

        if (parameterNames?.Contains(value.Name) == true) {
            return new Parameter(value.Name, MapDomainTypeToNode(value.Type));
        }

        if (value is Property property) {
            return entityInstance.GetMember(property.Name);
        }

        return entityInstance.GetMember(value.Name);
    }
}

public sealed record DomainImplementationModel(
    Domain Domain,
    IReadOnlyCollection<EntityImplementationModel> Entities,
    IReadOnlyCollection<Relationship> Relationships) : Node {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var entity in Entities) {
                yield return entity;
            }

            foreach (var relationship in Relationships) {
                yield return relationship;
            }
        }
    }
}

public sealed record EntityImplementationModel(
    Entity Entity,
    IReadOnlyCollection<Property> EffectiveProperties,
    IReadOnlyCollection<Action> EffectiveActions,
    IReadOnlyCollection<Policy> EffectivePolicies,
    IReadOnlyCollection<Event> EffectiveEvents,
    IReadOnlyCollection<Relationship> EffectiveRelationships,
    IReadOnlyCollection<StageImplementationModel> EffectiveStages) : Node {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var stage in EffectiveStages) {
                yield return stage;
            }
        }
    }
}

public sealed record StageImplementationModel(
    Stage Stage,
    IReadOnlyCollection<Action> EffectiveActions,
    IReadOnlyCollection<Policy> EffectivePolicies) : Node;

public sealed class DomainImplementationLoweringPass {
    private static readonly Node VoidType = new NamedTypeReference("void");
    private static readonly NamedTypeReference ActionExecutionContextType = new("ActionExecutionContext");
    private static readonly NamedTypeReference ActorInterfaceType = new("IActor");
    private AnalysisResult? _analysis;

    public DomainImplementationModel Lower(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.HasErrors) {
            throw new InvalidOperationException("Cannot lower domain model with analysis errors.");
        }

        _analysis = analysis;

        var entities = domain.Types
            .OfType<Entity>()
            .Select(entity => LowerEntity(entity, analysis))
            .ToArray();

        return new DomainImplementationModel(
            domain,
            entities,
            domain.Relationships.ToArray());
    }

    private static EntityImplementationModel LowerEntity(Entity entity, AnalysisResult analysis) {
        var metadata = analysis.GetMetadata<EffectiveMemberMetadata>(entity);
        if (metadata is null) {
            throw new InvalidOperationException($"No EffectiveMemberMetadata found for entity '{entity.Name}'.");
        }

        var effectiveStages = metadata.EffectiveStages
            .Select(stage => {
                var stageMetadata = analysis.GetMetadata<EffectiveStageMetadata>(stage);
                return new StageImplementationModel(
                    stage,
                    stageMetadata?.EffectiveActions.ToArray() ?? [],
                    stageMetadata?.EffectivePolicies.ToArray() ?? []);
            })
            .ToArray();

        return new EntityImplementationModel(
            entity,
            metadata.EffectiveProperties,
            metadata.EffectiveActions,
            metadata.EffectivePolicies,
            metadata.EffectiveEvents,
            metadata.EffectiveRelationships,
            effectiveStages);
    }

    // ── Type definition lowering ────────────────────────────────────────────

    public IReadOnlyList<TypeDefinitionNode> LowerToTypeDefinitions(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.HasErrors) {
            throw new InvalidOperationException("Cannot lower domain model with analysis errors.");
        }

        _analysis = analysis;
        var implModel = Lower(domain, analysis);
        var types = new List<TypeDefinitionNode>();

        var relationshipNames = implModel.Relationships
            .Select(r => r.Name)
            .ToHashSet();

        var allRelationships = implModel.Relationships;
        foreach (var entity in implModel.Entities) {
            if (relationshipNames.Contains(entity.Entity.Name)) continue;
            types.Add(LowerEntityToTypeDefinition(entity, allRelationships));
            if (entity.EffectiveStages.Count > 0) {
                types.Add(LowerStageEnum(entity.Entity, entity.EffectiveStages));
            }
        }

        types.Add(BuildActorInterfaceTypeDefinition());
        types.Add(BuildActionExecutionContextTypeDefinition());
        types.Add(BuildResultTypeDefinition());
        types.Add(BuildActionResultTypeDefinition());

        foreach (var rel in implModel.Relationships) {
            var relImpl = implModel.Entities.FirstOrDefault(e => e.Entity.Name == rel.Name);
            types.Add(LowerRelationshipToTypeDefinition(rel, relImpl));
            if (relImpl?.EffectiveStages.Count > 0) {
                types.Add(LowerStageEnum(rel, relImpl.EffectiveStages));
            }
        }

        var loweredEventNames = new HashSet<string>();

        foreach (var entity in implModel.Entities) {
            foreach (var ev in entity.EffectiveEvents) {
                if (loweredEventNames.Add(ev.Name)) {
                    types.Add(LowerEventToTypeDefinition(ev));
                }
            }
        }

        foreach (var ev in domain.Types.OfType<Event>()) {
            if (loweredEventNames.Add(ev.Name)) {
                types.Add(LowerEventToTypeDefinition(ev));
            }
        }

        return types;
    }

    public IReadOnlyList<TypeDefinitionNode> LowerToContractInterfaces(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.HasErrors) {
            throw new InvalidOperationException("Cannot lower domain model with analysis errors.");
        }

        var types = new List<TypeDefinitionNode>();

        foreach (var entity in domain.Types.OfType<Entity>()) {
            var entityModel = LowerEntity(entity, analysis);
            types.Add(BuildEntityContractInterface(entityModel));
            types.AddRange(BuildStageContractInterfaces(entityModel));
        }

        return types;
    }

    private TypeDefinitionNode BuildEntityContractInterface(EntityImplementationModel entityModel) {
        var entity = entityModel.Entity;
        var properties = entityModel.EffectiveProperties
            .Select(static p => new PropertyDefinitionNode(
                p.Name,
                DomainLoweringGenerator.MapDomainTypeToNode(p.Type),
                Getter: new PropertyGetterDefinitionNode()))
            .ToArray();

        var interfaces = entity.ParentEntity is not null
            ? new[] { (Node)new NamedTypeReference($"I{entity.ParentEntity.Name}") }
            : null;

        return new TypeDefinitionNode(
            $"I{entity.Name}",
            Properties: properties.Length > 0 ? properties : null,
            Interfaces: interfaces,
            IsInterface: true);
    }

    private IReadOnlyList<TypeDefinitionNode> BuildStageContractInterfaces(EntityImplementationModel entityModel) {
        var interfaces = new List<TypeDefinitionNode>();
        var entity = entityModel.Entity;

        foreach (var stageImpl in entityModel.EffectiveStages) {
            var stage = stageImpl.Stage;
            var hasParentStage = stage.Parent is not null && entity.ParentEntity is not null;

            // When the stage interface inherits from a parent stage interface,
            // only declare actions directly on this stage (parent actions inherited).
            var actions = hasParentStage ? stage.Actions : stageImpl.EffectiveActions;

            var methods = actions
                .Select(action => new MethodDefinitionNode(
                    action.Name,
                    new NamedTypeReference("Result"),
                    Parameters: new[] { BuildExecutionContextParameter() }
                        .Concat(action.Parameters.OfType<Property>().Select(p =>
                            new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name),
                                DomainLoweringGenerator.MapDomainTypeToNode(p.Type))))
                        .ToArray()))
                .ToArray();

            var inheritedInterfaces = new List<Node> { new NamedTypeReference($"I{entity.Name}") };
            if (hasParentStage && stage.Parent is not null && entity.ParentEntity is not null) {
                inheritedInterfaces.Add(new NamedTypeReference($"I{stage.Parent.Name}{entity.ParentEntity.Name}"));
            }

            interfaces.Add(new TypeDefinitionNode(
                $"I{stage.Name}{entity.Name}",
                Methods: methods.Length > 0 ? methods : null,
                Interfaces: inheritedInterfaces,
                IsInterface: true));
        }

        return interfaces;
    }

    public IReadOnlyList<Node>? GenerateTestStatements(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.HasErrors) return null;

        var implModel = Lower(domain, analysis);
        var relationshipNames = implModel.Relationships.Select(r => r.Name).ToHashSet();
        var statements = new List<Node>();
        var varIndex = 0;
        var contextVar = new Variable(
            "_context",
            new New(
                ActionExecutionContextType,
                new Constant(null),
                new Member(ActionExecutionContextType, "IgnoreEvent")));

        statements.Add(contextVar);
        statements.Add(new Invoke(
            new Member(new Variable("Console"), "WriteLine"),
            new Constant("")));

        foreach (var entity in implModel.Entities) {
            if (relationshipNames.Contains(entity.Entity.Name)) continue;
            if (entity.EffectiveActions.Count == 0) continue;

            var entityName = entity.Entity.Name;
            var varName = $"_{char.ToLower(entityName[0])}{entityName[1..]}";
            var hasStages = entity.EffectiveStages.Count > 0;

            var properties = entity.EffectiveProperties.ToArray();
            var ctorArgs = properties.Select(p => {
                var propTypeNode = DomainLoweringGenerator.MapDomainTypeToNode(p.Type);
                return DomainLoweringGenerator.GetDefaultNodeForType(propTypeNode);
            }).ToArray();

            var tryCreateCall = new Invoke(
                new Member(new NamedTypeReference(entityName), "TryCreate"),
                [contextVar, .. ctorArgs]);
            var createResultName = $"{varName}Result";
            var createResultVar = new Variable(createResultName);
            var entityVar = new Variable(varName);

            statements.Add(new Variable(createResultName, tryCreateCall));
            statements.Add(new Invoke(
                new Member(new Variable("Console"), "WriteLine"),
                new Constant($"Testing {entityName}...")
            ));

            var successStatements = new List<Node> {
                new Variable(varName, new NullForgiving(new Member(createResultVar, "Value")))
            };

            foreach (var action in entity.EffectiveActions) {
                varIndex++;
                var actionParams = action.Parameters.OfType<Property>().ToArray();
                var actionArgs = actionParams.Select(p => {
                    var paramTypeNode = DomainLoweringGenerator.MapDomainTypeToNode(p.Type);
                    return DomainLoweringGenerator.GetDefaultNodeForType(paramTypeNode);
                }).Cast<Node>().ToArray();

                var actionCall = new Invoke(new Member(entityVar, action.Name), [contextVar, .. actionArgs]);
                var okVar = new Variable($"_ok{varIndex}", new Member(actionCall, "IsSuccess"));
                successStatements.Add(okVar);

                successStatements.Add(new Invoke(
                    new Member(new Variable("Console"), "WriteLine"),
                    new Add(new Constant($"  {action.Name}: "),
                        new Syntax.Nodes.Conditional(okVar, new Constant("OK"), new Constant("FAILED")))
                ));
            }

            if (hasStages) {
                successStatements.Add(new Invoke(
                    new Member(new Variable("Console"), "WriteLine"),
                    new Add(new Constant($"  CurrentStage: "), new Member(entityVar, "CurrentStage"))
                ));
            }

            successStatements.Add(new Invoke(
                new Member(new Variable("Console"), "WriteLine"),
                new Constant("")
            ));

            statements.Add(new IfStatement(
                new Not(new Member(createResultVar, "IsSuccess")),
                new Block(
                    new Invoke(
                        new Member(new Variable("Console"), "WriteLine"),
                        new Add(
                            new Constant("  TryCreate: FAILED - "),
                            new Invoke(
                                new Member(new TypeReference("string"), "Join"),
                                new Constant(", "),
                                new Member(createResultVar, "ErrorCodes")))),
                    new Invoke(
                        new Member(new Variable("Console"), "WriteLine"),
                        new Constant(""))
                ),
                new Block(successStatements.ToArray())
            ));
        }

        return statements.Count > 0 ? statements : null;
    }

    private TypeDefinitionNode LowerEntityToTypeDefinition(EntityImplementationModel entityModel, IReadOnlyCollection<Relationship>? allRelationships = null) {
        var entity = entityModel.Entity;
        var executionContextParameter = BuildExecutionContextParameter();
        var actorContext = new ActorEvaluationContext(
            new Member(executionContextParameter, "Actor"),
            executionContextParameter);

        var domainProperties = entityModel.EffectiveProperties
            .Select(LowerPropertyToPropertyDefinition)
            .ToArray();

        var actions = entityModel.EffectiveActions
            .Select(action => LowerActionToMethodDefinition(action, entityModel))
            .ToArray();

        var subscriptions = entity.EventSubscriptions
            .Select(sub => LowerEventSubscriptionToMethodDefinition(sub, entityModel))
            .ToArray();

        var fields = new List<FieldDefinitionNode>();

        var synthProperties = new List<PropertyDefinitionNode>();
        if (entityModel.EffectiveStages.Count > 0) {
            var stageEnumName = GetStageEnumName(entity);
            synthProperties.Add(new PropertyDefinitionNode(
                "CurrentStage",
                new NamedTypeReference(stageEnumName),
                Getter: new PropertyGetterDefinitionNode(),
                Setter: new PropertySetterDefinitionNode(AccessModifier: AccessModifier.Protected)));
        }
        var rels = allRelationships ?? entityModel.EffectiveRelationships;

        foreach (var rel in rels) {
            if (rel.Source.Name == entity.Name) {
                AddSynthRelationshipMembers(rel.Target.Name,
                    rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany,
                    fields, synthProperties);
            }
            if (rel.Target.Name == entity.Name) {
                AddSynthRelationshipMembers(rel.Source.Name,
                    rel.Cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany,
                    fields, synthProperties);
            }
        }

        var allProperties = domainProperties.Concat(synthProperties).ToArray();

        var policyChecks = entityModel.EffectivePolicies
            .Select<Policy, (Node Check, string Name)?>(policy => {
                try {
                    var check = DomainLoweringGenerator.LowerPolicy(policy, new ParameterReference(), actorContext);
                    return (Check: check, Name: policy.Name);
                }
                catch (NotSupportedException) {
                    return null;
                }
            })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToArray();

        var ctorBody = domainProperties
            .Select(p => (Node)new Assignment(
                new ThisReference().GetMember(p.Name),
                new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name), p.MemberType)))
            .ToList();

        if (ctorBody.Count == 0) {
            ctorBody.Add(Return.Void);
        }

        var constructor = new ConstructorDefinitionNode(
            Parameters: domainProperties
                .Select(p => new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name), p.MemberType))
                .ToArray(),
            Body: new Block(ctorBody.ToArray()),
            AccessModifier: AccessModifier.Private);

        var tryCreate = BuildTryCreateMethod(entity.Name, domainProperties, policyChecks);

        var allMethods = new List<MethodDefinitionNode>(actions.Length + subscriptions.Length + 1);
        allMethods.Add(tryCreate);
        allMethods.AddRange(actions);
        allMethods.AddRange(subscriptions);
        if (entity is Actor) {
            allMethods.Add(new MethodDefinitionNode(
                "IsInRole",
                new PrimitiveTypeReference(PrimitiveType.Boolean),
                Parameters: [new Parameter("role", new PrimitiveTypeReference(PrimitiveType.String))],
                Body: new Block(new Return(new Constant(false)))));
        }

        return new TypeDefinitionNode(
            entity.Name,
            Constructors: [constructor],
            Properties: allProperties.Length > 0 ? allProperties : null,
            Methods: allMethods.Count > 0 ? allMethods : null,
            Fields: fields.Count > 0 ? fields : null,
            Interfaces: entity is Actor ? [ActorInterfaceType] : null);
    }

    private void AddSynthRelationshipMembers(string otherName, bool isCollection,
        List<FieldDefinitionNode> fields, List<PropertyDefinitionNode> synthProperties) {
        var propName = isCollection ? Pluralize(otherName) : otherName;
        var fieldName = "_" + char.ToLower(propName[0]) + propName[1..];

        if (isCollection) {
            fields.Add(new FieldDefinitionNode(fieldName,
                new CollectionTypeReference(new NamedTypeReference(otherName)),
                DefaultValue: new New(new CollectionTypeReference(new NamedTypeReference(otherName))),
                IsReadOnly: true,
                AccessModifier: AccessModifier.Private));

            synthProperties.Add(new PropertyDefinitionNode(propName,
                new NamedTypeReference("IReadOnlyCollection", TypeArguments: [new NamedTypeReference(otherName)]),
                Getter: new PropertyGetterDefinitionNode(Body: new Member(new ThisReference(), fieldName))));
        }
        else {
            fields.Add(new FieldDefinitionNode(fieldName,
                new NamedTypeReference(otherName),
                DefaultValue: new NullForgiving(new Constant(null)),
                AccessModifier: AccessModifier.Private));

            synthProperties.Add(new PropertyDefinitionNode(propName,
                new NamedTypeReference(otherName),
                Getter: new PropertyGetterDefinitionNode(Body: new Member(new ThisReference(), fieldName))));
        }
    }

    private TypeDefinitionNode LowerRelationshipToTypeDefinition(Relationship relationship, EntityImplementationModel? entityModel) {
        var executionContextParameter = BuildExecutionContextParameter();
        var actorContext = new ActorEvaluationContext(
            new Member(executionContextParameter, "Actor"),
            executionContextParameter);
        var payloadProperties = relationship.Properties
            .Select(LowerPropertyToPropertyDefinition)
            .ToList();

        var sourceProperty = new PropertyDefinitionNode(
            "Source",
            new NamedTypeReference(relationship.Source.Name),
            DefaultValue: new NullForgiving(new Constant(null)),
            Getter: new PropertyGetterDefinitionNode(),
            Setter: new PropertySetterDefinitionNode(AccessModifier: AccessModifier.Protected));
        var targetProperty = new PropertyDefinitionNode(
            "Target",
            new NamedTypeReference(relationship.Target.Name),
            DefaultValue: new NullForgiving(new Constant(null)),
            Getter: new PropertyGetterDefinitionNode(),
            Setter: new PropertySetterDefinitionNode(AccessModifier: AccessModifier.Protected));

        var properties = new List<PropertyDefinitionNode>(payloadProperties) {
            sourceProperty,
            targetProperty
        };

        var constructibleProperties = payloadProperties
            .Append(sourceProperty)
            .Append(targetProperty)
            .ToArray();

        var effectiveStages = entityModel?.EffectiveStages;
        if (effectiveStages is { Count: > 0 }) {
            var stageEnumName = GetStageEnumName(relationship);
            properties.Add(new PropertyDefinitionNode(
                "CurrentStage",
                new NamedTypeReference(stageEnumName),
                Getter: new PropertyGetterDefinitionNode(),
                Setter: new PropertySetterDefinitionNode(AccessModifier: AccessModifier.Protected)));
        }

        if (entityModel is not null) {
            foreach (var action in entityModel.EffectiveActions) {
                properties.AddRange(
                    action.Parameters
                        .OfType<Property>()
                        .Select(p => LowerPropertyToPropertyDefinition(p)));
            }
        }

        var ctorBody = constructibleProperties
            .Select(p => (Node)new Assignment(
                new ThisReference().GetMember(p.Name),
                new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name), p.MemberType)))
            .ToArray();

        var constructor = new ConstructorDefinitionNode(
            Parameters: constructibleProperties
                .Select(p => new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name), p.MemberType))
                .ToArray(),
            Body: new Block(ctorBody),
            AccessModifier: AccessModifier.Private);

        var policyChecks = entityModel?.EffectivePolicies
            .Select<Policy, (Node Check, string Name)?>(policy => {
                try {
                    var check = DomainLoweringGenerator.LowerPolicy(policy, new ParameterReference(), actorContext);
                    return (Check: check, Name: policy.Name);
                }
                catch (NotSupportedException) {
                    return null;
                }
            })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToArray() ?? [];

        var methods = new List<MethodDefinitionNode> {
            BuildTryCreateMethod(relationship.Name, constructibleProperties, policyChecks)
        };

        if (entityModel is not null) {
            methods.AddRange(entityModel.EffectiveActions.Select(a => LowerActionToMethodDefinition(a, entityModel)));
        }

        return new TypeDefinitionNode(
            relationship.Name,
            Constructors: [constructor],
            Properties: properties,
            Methods: methods);
    }

    private static TypeDefinitionNode LowerEventToTypeDefinition(Event @event) {
        var parameters = @event.Properties
            .Select(static property => new Parameter(
                DomainLoweringGenerator.ToSynthesizedParameterName(property.Name),
                DomainLoweringGenerator.MapDomainTypeToNode(property.Type)))
            .ToArray();

        return new TypeDefinitionNode(
            @event.Name,
            PrimaryConstructorParameters: parameters.Length > 0 ? parameters : null,
            Semantics: TypeDefinitionSemantics.ImmutableValue);
    }

    private static TypeDefinitionNode LowerStageEnum(Entity entity, IReadOnlyCollection<StageImplementationModel> stages) {
        var stageFields = stages
            .Select((stage, index) => new FieldDefinitionNode(
                stage.Stage.Name,
                new PrimitiveTypeReference(PrimitiveType.Int32),
                new Constant(index)))
            .ToArray();

        return new TypeDefinitionNode(
            GetStageEnumName(entity),
            Fields: stageFields,
            TypeCategory: TypeCategory.Enumeration);
    }

    private static PropertyDefinitionNode LowerPropertyToPropertyDefinition(Property property) {
        var typeNode = DomainLoweringGenerator.MapDomainTypeToNode(property.Type);
        var constraints = property.EffectiveConstraints
            .Select<Constraint, Node>(constraint => constraint switch {
                RequiredConstraint => TypeReference.To<System.ComponentModel.DataAnnotations.RequiredAttribute>(),
                RangeConstraint range => new New(
                    TypeReference.To<System.ComponentModel.DataAnnotations.RangeAttribute>(),
                    new Constant(range.MinValue ?? 0),
                    new Constant(range.MaxValue ?? 0)),
                LengthConstraint length => new New(
                    TypeReference.To<System.ComponentModel.DataAnnotations.StringLengthAttribute>(),
                    new Constant(length.MaxLength ?? -1)),
                _ => new Constant(constraint.ToString())
            })
            .ToArray();

        return new PropertyDefinitionNode(
            property.Name,
            typeNode,
            Getter: new PropertyGetterDefinitionNode(),
            Setter: new PropertySetterDefinitionNode(AccessModifier: AccessModifier.Protected),
            Constraints: constraints.Length > 0 ? constraints : null);
    }

    private MethodDefinitionNode LowerActionToMethodDefinition(Action action, EntityImplementationModel entityModel) {
        var executionContextParameter = BuildExecutionContextParameter();
        var domainParameters = action.Parameters
            .OfType<Property>()
            .Select(p => new Parameter(p.Name, DomainLoweringGenerator.MapDomainTypeToNode(p.Type)))
            .ToArray();
        var parameters = (new[] { executionContextParameter })
            .Concat(domainParameters)
            .ToArray();
        var parameterNames = action.Parameters
            .OfType<Property>()
            .Select(static p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var actorContext = new ActorEvaluationContext(
            new Member(executionContextParameter, "Actor"),
            executionContextParameter);

        var thisRef = new ThisReference();
        var eventSubscriptions = entityModel.Entity.EventSubscriptions;
        var effectNodes = action.Effects
            .Select(e => DomainLoweringGenerator.LowerEffect(e, thisRef, eventSubscriptions, entityModel.Entity.Name, parameterNames, executionContextParameter))
            .ToArray();

        var activeEffects = effectNodes.Where(n => !ReferenceEquals(n, True)).ToArray();
        Node? effectBlock = activeEffects.Length switch {
            0 => null,
            1 => activeEffects[0],
            _ => new Block(activeEffects)
        };

        var allPolicies = action.Policies.Concat(entityModel.EffectivePolicies).ToArray();
        var policyChecks = allPolicies
            .Select<Policy, (Node Check, string Name)?>(policy => {
                try {
                    var check = DomainLoweringGenerator.LowerPolicy(policy, thisRef, actorContext);
                    return (Check: check, Name: policy.Name);
                }
                catch (NotSupportedException) {
                    return null;
                }
            })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToArray();

        var paramGuards = CollectParameterConstraintGuards(action);
        var stageGuards = CollectStageTransitionGuards(action, entityModel);

        var returnType = new NamedTypeReference("Result");
        var returnSuccessNode = new Return(new Invoke(new Member(returnType, "Success")));


        if (policyChecks.Length == 0 && paramGuards.Count == 0 && stageGuards.Count == 0) {
            Node body = new Block([.. activeEffects, returnSuccessNode]);

            return new MethodDefinitionNode(
                action.Name, returnType,
                Parameters: parameters,
                Body: body);
        }

        var errors = new Variable("_errors", new New(new CollectionTypeReference(new NamedTypeReference("string"))));
        var returnFailureNode = new Return(new Invoke(new Member(returnType, "Failure"), new Invoke(new Member(errors, "ToArray"))));
        var bodyStatements = new List<Node>();

        foreach (var (check, name) in policyChecks) {
            if (DomainLoweringGenerator.TryGetConstantBoolean(check, out var result)) {
                if (!result) {
                    bodyStatements.Add(new Invoke(new Member(errors, "Add"), new Constant(name)));
                }
                continue;
            }

            bodyStatements.Add(new IfStatement(
                new Not(check),
                new Invoke(new Member(errors, "Add"), new Constant(name))));
        }

        foreach (var (guard, code) in paramGuards) {
            if (DomainLoweringGenerator.TryGetConstantBoolean(guard, out var result)) {
                if (!result) {
                    bodyStatements.Add(new Invoke(new Member(errors, "Add"), new Constant(code)));
                }
                continue;
            }

            bodyStatements.Add(new IfStatement(
                new Not(guard),
                new Invoke(new Member(errors, "Add"), new Constant(code))));
        }

        foreach (var (guard, code) in stageGuards) {
            bodyStatements.Add(new IfStatement(
                new Not(guard),
                new Invoke(new Member(errors, "Add"), new Constant(code))));
        }

        bodyStatements.Add(new IfStatement(
            new NotEqual(new Member(errors, "Count"), new Constant(0)),
            returnFailureNode));

        bodyStatements.AddRange(effectNodes);
        bodyStatements.Add(returnSuccessNode);

        return new MethodDefinitionNode(
            action.Name, returnType,
            Parameters: parameters,
            Body: new Block(bodyStatements, [errors]));
    }

    private IReadOnlyList<(Node Guard, string Code)> CollectParameterConstraintGuards(Action action) {
        if (_analysis is null) return [];

        return action.Parameters
            .OfType<Property>()
            .Select(param => {
                var metadata = _analysis.GetMetadata<DownstreamConstraintsMetadata>(param);
                return (Param: param, Metadata: metadata);
            })
            .Where(x => x.Metadata?.Constraints.Count > 0)
            .SelectMany(x => x.Metadata!.Constraints
                .Select(c => (
                    Guard: DomainLoweringGenerator.LowerConstraint(c, new Parameter(x.Param.Name), DomainLoweringGenerator.MapDomainTypeToNode(x.Param.Type)),
                    Code: $"{x.Param.Name}ConstraintFailed")))
            .ToArray();
    }

    private IReadOnlyList<(Node Guard, string Code)> CollectStageTransitionGuards(Action action, EntityImplementationModel entityModel) {
        var guards = new List<(Node Guard, string Code)>();
        var effectiveStages = entityModel.EffectiveStages;
        if (effectiveStages.Count == 0) return [];

        var orderedStages = effectiveStages.Select(s => s.Stage).ToList();

        foreach (var effect in action.Effects) {
            if (effect is StageTransition stageTransition) {
                var targetIndex = orderedStages.FindIndex(s => s.Name == stageTransition.TargetStage.Name);
                if (targetIndex > 0) {
                    var sourceStage = orderedStages[targetIndex - 1];
                    var guard = BuildStageSourceGuard(entityModel, sourceStage);
                    guards.Add((guard, $"{action.Name}Requires{sourceStage.Name}Stage"));
                }
            }
        }

        if (guards.Count == 0) {
            var assignedStages = effectiveStages
                .Where(s => s.EffectiveActions.Any(a => ReferenceEquals(a, action)))
                .Select(s => s.Stage)
                .ToArray();

            if (assignedStages.Length > 0) {
                var guardStage = assignedStages[0];
                Node combinedGuard = BuildStageSourceGuard(entityModel, guardStage);
                for (var i = 1; i < assignedStages.Length; i++) {
                    combinedGuard = new Or(combinedGuard, BuildStageSourceGuard(entityModel, assignedStages[i]));
                }
                guards.Add((combinedGuard, $"{action.Name}Requires{guardStage.Name}Stage"));
            }
        }

        return guards;
    }

    private Node BuildStageSourceGuard(EntityImplementationModel entityModel, Stage sourceStage) {
        var currentStage = new Member(new ThisReference(), "CurrentStage");
        var stageEnumType = new NamedTypeReference($"{entityModel.Entity.Name}Stage");
        var sourceRef = new Member(stageEnumType, sourceStage.Name);

        var descendants = new List<Stage>();
        if (_analysis is not null) {
            var sourceLineage = _analysis.GetMetadata<StageLineageMetadata>(sourceStage);
            if (sourceLineage is not null) {
                foreach (var stageImpl in entityModel.EffectiveStages) {
                    var stage = stageImpl.Stage;
                    if (ReferenceEquals(stage, sourceStage)) continue;
                    var stageLineage = _analysis.GetMetadata<StageLineageMetadata>(stage);
                    if (stageLineage?.Ancestors.Contains(sourceStage) == true) {
                        descendants.Add(stage);
                    }
                }
            }
        }

        Node guard = new Equal(currentStage, sourceRef);
        foreach (var desc in descendants) {
            guard = new Or(guard, new Equal(currentStage, new Member(stageEnumType, desc.Name)));
        }
        return guard;
    }

    private MethodDefinitionNode LowerEventSubscriptionToMethodDefinition(
        EventSubscription subscription, EntityImplementationModel entityModel) {
        var executionContextParameter = BuildExecutionContextParameter();
        var eventParam = new Parameter(
            subscription.EventParameterName,
            DomainLoweringGenerator.MapDomainTypeToNode(subscription.EventType));

        var thisRef = new ThisReference();
        var handlerArgs = subscription.HandlerAction.Parameters
            .OfType<Property>()
            .Select(p => {
                // Map event properties to action parameters via correlation bindings
                var correlation = subscription.Correlations
                    .FirstOrDefault(c => string.Equals(c.ConsumerPropertyName, p.Name, StringComparison.Ordinal));
                if (correlation is not null) {
                    return new Member(
                        new Parameter(subscription.EventParameterName),
                        correlation.EventPropertyName);
                }
                return new Member(eventParam, p.Name);
            })
            .ToArray();

        var body = new Invoke(
            thisRef.Invoke(subscription.HandlerAction.Name),
            [executionContextParameter, .. handlerArgs]);

        return new MethodDefinitionNode(
            $"On{subscription.EventType.Name}",
            new NamedTypeReference("Result"),
            Parameters: [executionContextParameter, eventParam],
            Body: new Block(new Return(body)));
    }

    private static string GetStageEnumName(Entity entity) => $"{entity.Name}Stage";

    private static string Pluralize(string name) {
        if (name.EndsWith("s") || name.EndsWith("sh") || name.EndsWith("ch") || name.EndsWith("x") || name.EndsWith("z")) return name + "es";
        if (name.EndsWith("y") && name.Length > 1 && !"aeiou".Contains(name[^2])) return name[..^1] + "ies";
        return name + "s";
    }

    private static Parameter BuildExecutionContextParameter() => new("context", ActionExecutionContextType);

    private static TypeDefinitionNode BuildActorInterfaceTypeDefinition() {
        return new TypeDefinitionNode(
            "IActor",
            IsInterface: true,
            Methods: [
                new MethodDefinitionNode(
                    "IsInRole",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Parameters: [new Parameter("role", new PrimitiveTypeReference(PrimitiveType.String))])
            ]);
    }

    private static TypeDefinitionNode BuildActionExecutionContextTypeDefinition() {
        var stringType = new PrimitiveTypeReference(PrimitiveType.String);
        var objectType = new NamedTypeReference("object");
        var eventPublisherType = new NamedTypeReference("List", TypeArguments: [objectType]);

        return new TypeDefinitionNode(
            "ActionExecutionContext",
            Fields: [
                new FieldDefinitionNode("_actor", new OptionalTypeReference(ActorInterfaceType), AccessModifier: AccessModifier.Private),
                new FieldDefinitionNode("_events", eventPublisherType, AccessModifier: AccessModifier.Private, IsReadOnly: true)
            ],
            Properties: [
                new PropertyDefinitionNode("Actor", new OptionalTypeReference(ActorInterfaceType), Getter: new PropertyGetterDefinitionNode(Body: new Member(new ThisReference(), "_actor"))),
                new PropertyDefinitionNode("Events", eventPublisherType, Getter: new PropertyGetterDefinitionNode(Body: new Member(new ThisReference(), "_events"))),
            ],
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [
                        new Parameter("actor", new OptionalTypeReference(ActorInterfaceType)),
                        new Parameter("events", eventPublisherType)
                    ],
                    Body: new Block(
                        new Assignment(new ThisReference().GetMember("Actor"), new Parameter("actor", new OptionalTypeReference(ActorInterfaceType))),
                        new Assignment(new ThisReference().GetMember("Events"), new Parameter("events", eventPublisherType))))
            ],
            Methods: [
                new MethodDefinitionNode(
                    "IgnoreEvent",
                    VoidType,
                    Parameters: [new Parameter("@event", objectType)],
                    Body: new Block(Return.Void),
                    IsStatic: true),
                new MethodDefinitionNode(
                    "PublishEvent",
                    VoidType,
                    Parameters: [new Parameter("@event", objectType)],
                    Body: new Block(
                        new Invoke(new Member(new Member(new ThisReference(), "_events"), "Add"), new Parameter("@event", objectType)))),
            ]);
    }

    private static TypeDefinitionNode BuildResultTypeDefinition() {
        var genericT = new Parameter("T", null!);
        var errorCodesType = new NamedTypeReference("IReadOnlyCollection", TypeArguments: [new PrimitiveTypeReference(PrimitiveType.String)]);
        var listOfString = new CollectionTypeReference(new NamedTypeReference("string"));

        return new TypeDefinitionNode(
            "Result",
            GenericParameters: [genericT],
            Properties: [
                new PropertyDefinitionNode("IsSuccess", new PrimitiveTypeReference(PrimitiveType.Boolean), Getter: new PropertyGetterDefinitionNode()),
                new PropertyDefinitionNode("Value", new OptionalTypeReference(new NamedTypeReference("T")), Getter: new PropertyGetterDefinitionNode()),
                new PropertyDefinitionNode("ErrorCodes", errorCodesType, Getter: new PropertyGetterDefinitionNode()),
            ],
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [new Parameter("value", new NamedTypeReference("T"))],
                    Body: new Block(
                        new Assignment(new ThisReference().GetMember("Value"), new Parameter("value", new NamedTypeReference("T"))),
                        new Assignment(new ThisReference().GetMember("ErrorCodes"), new New(listOfString)),
                        new Assignment(new ThisReference().GetMember("IsSuccess"), new Constant(true))),
                    AccessModifier: AccessModifier.Private),
                new ConstructorDefinitionNode(
                    Parameters: [new Parameter("errorCodes", errorCodesType)],
                    Body: new Block(
                        new Assignment(new ThisReference().GetMember("ErrorCodes"), new Parameter("errorCodes", errorCodesType)),
                        new Assignment(new ThisReference().GetMember("IsSuccess"), new Constant(false))),
                    AccessModifier: AccessModifier.Private),
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Success",
                    new NamedTypeReference("Result<T>"),
                    Parameters: [new Parameter("value", new NamedTypeReference("T"))],
                    Body: new Block(new Return(new New(new NamedTypeReference("Result<T>"), new Parameter("value", new NamedTypeReference("T"))))),
                    IsStatic: true),
                new MethodDefinitionNode(
                    "Failure",
                    new NamedTypeReference("Result<T>"),
                    Parameters: [new Parameter("errorCodes", errorCodesType)],
                    Body: new Block(new Return(new New(new NamedTypeReference("Result<T>"), new Parameter("errorCodes", errorCodesType)))),
                    IsStatic: true),
            ]);
    }

    private static TypeDefinitionNode BuildActionResultTypeDefinition() {
        var errorCodesType = new NamedTypeReference("IReadOnlyCollection", TypeArguments: [new PrimitiveTypeReference(PrimitiveType.String)]);
        var listOfString = new CollectionTypeReference(new NamedTypeReference("string"));

        return new TypeDefinitionNode(
            "Result",
            Properties: [
                new PropertyDefinitionNode("IsSuccess", new PrimitiveTypeReference(PrimitiveType.Boolean), Getter: new PropertyGetterDefinitionNode()),
                new PropertyDefinitionNode("ErrorCodes", errorCodesType, Getter: new PropertyGetterDefinitionNode()),
            ],
            Constructors: [
                new ConstructorDefinitionNode(
                    Body: new Block(
                        new Assignment(new ThisReference().GetMember("ErrorCodes"), new New(listOfString)),
                        new Assignment(new ThisReference().GetMember("IsSuccess"), new Constant(true))),
                    AccessModifier: AccessModifier.Private),
                new ConstructorDefinitionNode(
                    Parameters: [new Parameter("errorCodes", errorCodesType)],
                    Body: new Block(
                        new Assignment(new ThisReference().GetMember("ErrorCodes"), new Parameter("errorCodes", errorCodesType)),
                        new Assignment(new ThisReference().GetMember("IsSuccess"), new Constant(false))),
                    AccessModifier: AccessModifier.Private),
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Success",
                    new NamedTypeReference("Result"),
                    Body: new Block(new Return(new New(new NamedTypeReference("Result")))),
                    IsStatic: true),
                new MethodDefinitionNode(
                    "Failure",
                    new NamedTypeReference("Result"),
                    Parameters: [new Parameter("errorCodes", errorCodesType)],
                    Body: new Block(new Return(new New(new NamedTypeReference("Result"), new Parameter("errorCodes", errorCodesType)))),
                    IsStatic: true),
            ]);
    }

    private static MethodDefinitionNode BuildTryCreateMethod(string typeName, IReadOnlyList<PropertyDefinitionNode> constructibleProperties, (Node Check, string Name)[] policyChecks) {
        var returnType = new NamedTypeReference($"Result<{typeName}>");
        var constructibleParameters = constructibleProperties
            .Select(p => new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name), p.MemberType))
            .ToArray();
        var parameters = (new[] { BuildExecutionContextParameter() })
            .Concat(constructibleParameters)
            .ToArray();
        var ctorArgs = constructibleProperties
            .Select(p => {
                var typeNode = p.MemberType;
                return DomainLoweringGenerator.ApplyNullForgivingIfNeeded(new Parameter(DomainLoweringGenerator.ToSynthesizedParameterName(p.Name), typeNode), typeNode);
            })
            .ToArray();

        if (policyChecks.Length == 0) {
            return new MethodDefinitionNode(
                "TryCreate",
                returnType,
                Parameters: parameters,
                Body: new Block(
                    new Return(new Invoke(
                        new Member(returnType, "Success"),
                        new New(new NamedTypeReference(typeName),
                            ctorArgs)))),
                IsStatic: true);
        }

        var errors = new Variable("_errors", new New(new CollectionTypeReference(new NamedTypeReference("string"))));
        var bodyStatements = new List<Node>();

        foreach (var (check, name) in policyChecks) {
            if (DomainLoweringGenerator.TryGetConstantBoolean(check, out var result)) {
                if (!result) {
                    bodyStatements.Add(new Invoke(new Member(errors, "Add"), new Constant(name)));
                }
                continue;
            }

            bodyStatements.Add(new IfStatement(
                new Not(check),
                new Invoke(new Member(errors, "Add"), new Constant(name))));
        }

        bodyStatements.Add(new IfStatement(
            new Equal(new Member(errors, "Count"), new Constant(0)),
            new Return(new Invoke(
                new Member(returnType, "Success"),
                new New(new NamedTypeReference(typeName),
                    ctorArgs))),
            new Return(new Invoke(
                new Member(returnType, "Failure"),
                errors))));

        return new MethodDefinitionNode(
            "TryCreate",
            returnType,
            Parameters: parameters,
            Body: new Block(bodyStatements, [errors]),
            IsStatic: true);
    }
}

public static class DomainLoweringGeneratorExtensions {
    /// <summary>
    /// Lowers a collection of policies into a conjunction of guard conditions.
    /// </summary>
    public static Node LowerPoliciesAsGuard(this IReadOnlyCollection<Policy> policies, Node subject, ActorEvaluationContext? actorContext = null) {
        if (policies.Count == 0) {
            return True;
        }

        return policies
            .Select(p => DomainLoweringGenerator.LowerPolicy(p, subject, actorContext))
            .Aggregate(static (acc, guard) => new And(acc, guard));
    }
}