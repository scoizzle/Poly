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
            .Select(LowerEntity)
            .ToArray();

        return new DomainImplementationModel(
            domain,
            entities,
            domain.Relationships.ToArray());
    }

    private static EntityImplementationModel LowerEntity(Entity entity) {
        var lineage = EnumerateEntityLineageRootToLeaf(entity).ToArray();

        var effectiveProperties = MergeByName(lineage.SelectMany(static current => current.Properties), static property => property.Name);
        var effectiveActions = MergeByName(lineage.SelectMany(static current => current.Actions), static action => action.Name);
        var effectivePolicies = MergeByName(lineage.SelectMany(static current => current.Policies), static policy => policy.Name);
        var effectiveEvents = MergeByName(lineage.SelectMany(static current => current.Events), static @event => @event.Name);
        var effectiveRelationships = MergeByName(lineage.SelectMany(static current => current.Relationships), static relationship => relationship.Name);

        var stagesByName = MergeByName(lineage.SelectMany(static current => current.Stages), static stage => stage.Name);
        var effectiveStages = stagesByName
            .Select(static stage => new StageImplementationModel(
                stage,
                stage.GetEffectiveActions().ToArray(),
                stage.GetEffectivePolicies().ToArray()))
            .ToArray();

        return new EntityImplementationModel(
            entity,
            effectiveProperties,
            effectiveActions,
            effectivePolicies,
            effectiveEvents,
            effectiveRelationships,
            effectiveStages);
    }

    private static IEnumerable<Entity> EnumerateEntityLineageRootToLeaf(Entity entity) {
        var stack = new Stack<Entity>();

        for (var current = entity; current is not null; current = current.ParentEntity) {
            stack.Push(current);
        }

        while (stack.Count > 0) {
            yield return stack.Pop();
        }
    }

    private static IReadOnlyCollection<TNode> MergeByName<TNode>(IEnumerable<TNode> nodes, Func<TNode, string> nameSelector) {
        var byName = new Dictionary<string, TNode>(StringComparer.Ordinal);

        foreach (var node in nodes) {
            byName[nameSelector(node)] = node;
        }

        return byName.Values.ToArray();
    }
}