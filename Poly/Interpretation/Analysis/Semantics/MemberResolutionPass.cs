namespace Poly.Interpretation.Analysis.Semantics;

internal static class MethodInvocationSemanticResolver {
    public static ITypeMethod? ResolveMethod(AnalysisContext context, Invoke methodInv) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(methodInv);

        if (methodInv.Delegate is not Member memberAccess)
            return null;

        var targetType = context.GetResolvedType(memberAccess.Value);
        if (targetType == null)
            return null;

        var argumentTypes = new ITypeDefinition[methodInv.Arguments.Length];
        for (var i = 0; i < methodInv.Arguments.Length; i++) {
            var argumentType = context.GetResolvedType(methodInv.Arguments[i]);
            if (argumentType == null)
                return null;

            argumentTypes[i] = argumentType;
        }

        return targetType
            .FindMatchingMethodOverloads(memberAccess.MemberName, argumentTypes)
            .FirstOrDefault();
    }
}

internal static class ConstructorInvocationSemanticResolver {
    public static ITypeConstructor? ResolveConstructor(AnalysisContext context, New @new) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(@new);

        var targetType = context.GetResolvedType(@new.Type);
        if (targetType == null)
            return null;

        var argumentTypes = new ITypeDefinition[@new.Arguments.Length];
        for (var i = 0; i < @new.Arguments.Length; i++) {
            var argumentType = context.GetResolvedType(@new.Arguments[i]);
            if (argumentType == null)
                return null;

            argumentTypes[i] = argumentType;
        }

        return targetType
            .FindMatchingConstructors(argumentTypes)
            .FirstOrDefault();
    }
}


internal sealed class MemberResolver : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<MemberResolver>(node)) {
            return;
        }

        var resolvedMember = node switch {
            // Member access - resolve the member being accessed
            Member memberAccess => ResolveMemberAccessMember(context, memberAccess),

            // Method invocation - resolve the method being called
            Invoke methodInv => ResolveMethodInvocationMember(context, methodInv),

            // Constructor invocation - resolve the constructor being called
            New @new => ResolveConstructorInvocationMember(context, @new),

            // Index access - resolve the indexer property
            IndexAccess indexAccess => ResolveIndexAccessMember(context, indexAccess),

            _ => null
        };

        if (resolvedMember != null) {
            context.SetResolvedMember(node, resolvedMember);
        }

        this.AnalyzeChildren(context, node);
    }

    private static ITypeMember? ResolveMemberAccessMember(AnalysisContext context, Member memberAccess) {
        var instanceType = context.GetResolvedType(memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        return member;
    }

    private static ITypeMember? ResolveMethodInvocationMember(AnalysisContext context, Invoke methodInv) {
        return MethodInvocationSemanticResolver.ResolveMethod(context, methodInv);
    }

    private static ITypeMember? ResolveConstructorInvocationMember(AnalysisContext context, New @new) {
        return ConstructorInvocationSemanticResolver.ResolveConstructor(context, @new);
    }

    private static ITypeMember? ResolveIndexAccessMember(AnalysisContext context, IndexAccess indexAccess) {
        var instanceType = context.GetResolvedType(indexAccess.Value);
        if (instanceType == null)
            return null;

        var argumentTypes = indexAccess.Arguments
            .Select(context.GetResolvedType)
            .ToArray();

        if (argumentTypes.All(static type => type is not null)) {
            var matchedIndexer = instanceType.FindMatchingIndexers(argumentTypes!).FirstOrDefault();
            if (matchedIndexer != null) {
                return matchedIndexer;
            }
        }

        var indexer = instanceType.Properties
            .FirstOrDefault(static property => property.Parameters is { } parameters && parameters.Any());

        return indexer;
    }
}


public static class MemberResolutionMetadataExtensions {
    private sealed class MemberResolutionMetadata : IAnalysisMetadata {
        public ITypeMember? ResolvedMember { get; set; }
    };

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseMemberResolver() {
            builder.AddAnalyzer(new MemberResolver());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public void SetResolvedMember(Node node, ITypeMember member) {
            ArgumentNullException.ThrowIfNull(member);

            var metadata = context.GetOrAddMetadata(node, static () => new MemberResolutionMetadata());
            metadata.ResolvedMember = member;

            context.SetResolvedType(node, member.MemberTypeDefinition);
        }
    }

    extension(INodeMetadataProvider typedMetadataProvider) {
        public ITypeMember? GetResolvedMember(Node node) {
            return typedMetadataProvider.GetMetadata<MemberResolutionMetadata>(node)?.ResolvedMember;
        }
    }
}