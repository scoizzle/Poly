using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
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
        var replacement = _analysis.GetNodeReplacement(lowered);
        return replacement ?? lowered;
    }

    private static Node LowerCore(Node expression) {
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
            PolicyAggregationStrategy.All => nodes.Aggregate((Node)True, static (acc, node) => new And(acc, node)),
            PolicyAggregationStrategy.Any => nodes.Aggregate((Node)False, static (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException($"Unknown aggregation strategy '{policy.AggregationStrategy}'.")
        };
    }

    public static Node LowerRule(Rule rule, Node subject, ActorEvaluationContext? actorContext = null) {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(subject);

        return rule switch {
            PropertyRule propertyRule => LowerConstraint(propertyRule.Constraints, subject.GetMember(propertyRule.Value.Name)),
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
                ? new Invoke(new Member(actorContext.ActorSubject, "IsInRole"), new Constant(actorRoleRule.Role))
                : throw new NotSupportedException($"'{nameof(ActorRoleRule)}' requires an {nameof(ActorEvaluationContext)}."),
            ActorPropertyRule actorPropertyRule => actorContext is not null
                ? LowerConstraint(actorPropertyRule.Constraints, new Member(actorContext.ActorSubject, actorPropertyRule.ActorProperty.Name))
                : throw new NotSupportedException($"'{nameof(ActorPropertyRule)}' requires an {nameof(ActorEvaluationContext)}."),
            _ => throw new NotSupportedException($"Unknown rule type '{rule.GetType().Name}'.")
        };
    }

    public static Node LowerConstraint(Constraint constraint, Node value) {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(value);

        return constraint switch {
            RequiredConstraint => new NotEqual(value, Null),
            EqualityConstraint eq => new Equal(value, Wrap(eq.Value)),
            RangeConstraint range => LowerRange(range, value),
            LengthConstraint length => LowerLength(length, value),
            EnumConstraint @enum => LowerEnum(@enum, value),
            ConstraintSet set => LowerConstraintSet(set, value),
            _ => throw new NotSupportedException($"Unknown constraint type '{constraint.GetType().Name}'.")
        };
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

    private static Node LowerConstraintSet(ConstraintSet set, Node value) {
        if (set.Constraints.Count == 0) {
            return True;
        }

        var nodes = set.Constraints.Select(c => LowerConstraint(c, value));

        return set.AggregationStrategy switch {
            ConstraintAggregationStrategy.All => nodes.Aggregate((Node)True, static (acc, node) => new And(acc, node)),
            ConstraintAggregationStrategy.Any => nodes.Aggregate((Node)False, static (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException($"Unknown aggregation strategy '{set.AggregationStrategy}'.")
        };
    }

    private static Node LowerCrossProperty(CrossPropertyRule rule, Node subject) {
        var left = subject.GetMember(rule.Left.Name);
        var right = subject.GetMember(rule.Right.Name);

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
    public DomainImplementationModel Lower(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.HasErrors) {
            throw new InvalidOperationException("Cannot lower domain model with analysis errors.");
        }

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
}