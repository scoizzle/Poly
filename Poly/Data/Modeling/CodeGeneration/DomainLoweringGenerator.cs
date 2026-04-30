using Poly.Syntax.Nodes;

namespace Poly.Data.Modeling;

/// <summary>
/// Lowers Domain Modeling syntax clauses into executable interpretation AST nodes.
/// The lowering process is contextualized by analysis results and a root subject node.
/// </summary>
public sealed class DomainLoweringGenerator {
    private readonly AnalysisResult _analysis;

    public DomainLoweringGenerator(AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        _analysis = analysis;
    }

    public Node Lower(Node root, Node subjectRoot) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(subjectRoot);

        var lowered = LowerCore(root, subjectRoot);

        // Honor replacement metadata produced by analyzers for the lowered output.
        var replacement = _analysis.GetNodeReplacement(lowered);
        return replacement ?? lowered;
    }

    private static Node LowerCore(Node expression, Node subjectRoot) {
        _ = subjectRoot;

        return expression switch {
            And and => new And(LowerCore(and.LeftHandValue, subjectRoot), LowerCore(and.RightHandValue, subjectRoot)),
            Or or => new Or(LowerCore(or.LeftHandValue, subjectRoot), LowerCore(or.RightHandValue, subjectRoot)),
            Equal equal => new Equal(LowerCore(equal.LeftHandValue, subjectRoot), LowerCore(equal.RightHandValue, subjectRoot)),
            NotEqual notEqual => new NotEqual(LowerCore(notEqual.LeftHandValue, subjectRoot), LowerCore(notEqual.RightHandValue, subjectRoot)),
            GreaterThanOrEqual greaterThanOrEqual => new GreaterThanOrEqual(LowerCore(greaterThanOrEqual.LeftHandValue, subjectRoot), LowerCore(greaterThanOrEqual.RightHandValue, subjectRoot)),
            LessThanOrEqual lessThanOrEqual => new LessThanOrEqual(LowerCore(lessThanOrEqual.LeftHandValue, subjectRoot), LowerCore(lessThanOrEqual.RightHandValue, subjectRoot)),
            Member memberAccess => new Member(LowerCore(memberAccess.Value, subjectRoot), memberAccess.MemberName),
            Constant constant => new Constant(constant.Value),
            _ => expression
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
            .Select(stage => new StageImplementationModel(
                stage,
                stage.GetEffectiveActions().ToArray(),
                stage.GetEffectivePolicies().ToArray()))
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