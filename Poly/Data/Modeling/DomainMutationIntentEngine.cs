using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class DomainMutationIntentEngine {
    internal static TNode ResolveNode<TNode>(Domain domain, DomainNodeReference reference) where TNode : DomainObject {
        ArgumentNullException.ThrowIfNull(reference);

        var segments = reference.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0) {
            throw new ArgumentException("Node path cannot be null or empty.", nameof(reference));
        }

        var current = domain as DomainObject;

        foreach (var segment in segments) {
            var next = current.ChildObjects.FirstOrDefault(child => child != null && string.Equals(child.Name, segment, StringComparison.Ordinal));

            if (next is null)
                throw new InvalidOperationException($"Could not resolve path '{reference.Path}' - segment '{segment}' not found under '{current.Name}'.");

            current = next;
        }

        if (current is not TNode typedNode) {
            throw new InvalidOperationException($"Could not resolve path '{reference.Path}' as {typeof(TNode).Name}.");
        }

        return typedNode;
    }

    public AnalysisResult Apply(Domain domain, DomainMutationIntent intent, DomainModelAnalyzer? analyzer = null, AnalysisResult? preMutationAnalysis = null) {
        ArgumentNullException.ThrowIfNull(intent);
        return Apply(domain, [intent], analyzer, preMutationAnalysis);
    }

    public AnalysisResult Apply(Domain domain, IEnumerable<DomainMutationIntent> intents, DomainModelAnalyzer? analyzer = null, AnalysisResult? preMutationAnalysis = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(intents);

        var mutation = domain.CreateMutation(analyzer);

        foreach (var intent in intents) {
            ArgumentNullException.ThrowIfNull(intent);

            switch (intent) {
                case SetDomainNameIntent setDomainName:
                    _ = mutation.SetDomainName(setDomainName.Name);
                    break;
                case AddPrimitiveTypeIntent addPrimitiveType:
                    _ = mutation.AddType(new Primitive(domain, addPrimitiveType.Name, addPrimitiveType.Category));
                    break;
                case AddEntityTypeIntent addEntityType:
                    _ = mutation.AddType(CreateEntity(domain, addEntityType));
                    break;
                case AddRelationshipIntent addRelationship:
                    _ = mutation.AddRelationship(CreateRelationship(domain, addRelationship));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported domain mutation intent '{intent.GetType().Name}'.");
            }
        }

        return mutation.Apply(preMutationAnalysis);
    }

    private static Entity CreateEntity(Domain domain, AddEntityTypeIntent intent) {
        var parent = intent.ParentEntity is null
            ? null
            : ResolveNode<Entity>(domain, intent.ParentEntity);

        return new Entity(domain, intent.Name, parent);
    }

    private static Relationship CreateRelationship(Domain domain, AddRelationshipIntent intent) {
        var source = ResolveNode<Entity>(domain, intent.SourceEntity);
        var target = ResolveNode<Entity>(domain, intent.TargetEntity);

        return new Relationship(domain, intent.Name, source, target, intent.Cardinality, intent.SourceOwnsTarget);
    }
}