namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class LambdaReturnTypeAnalyzer : INodeAnalyzer {
    public const string Id = "LambdaReturnType";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id];
    public void Analyze(AnalysisContext context, Node node) {
        // A Lambda value is a closure (heap ref / object). Do not overwrite
        // that with the body type — Invoke uses the body, the binding does not.

        if (node is Invoke invoke) {
            ITypeDefinition? bodyType = invoke.Delegate switch {
                Lambda invokedLambda => ResolveBodyType(context, invokedLambda.Body),
                Variable or Parameter when context.GetMetadata<StoredLambdaMetadata>(invoke.Delegate)
                    is { } stored => ResolveBodyType(context, stored.Lambda.Body),
                _ => null
            };
            if (bodyType is not null) {
                var currentType = context.GetResolvedType(invoke);
                var rt = currentType?.GetRuntimeType();
                if (rt is null || rt == typeof(object))
                    context.SetResolvedType(invoke, bodyType);
            }
        }

        this.AnalyzeChildren(context, node);
    }

    private static ITypeDefinition? ResolveBodyType(AnalysisContext context, Node body) {
        var yield = TypeAndMemberResolver.YieldNode(body);
        return context.GetResolvedType(yield) ?? context.GetResolvedType(body);
    }
}

public static class LambdaReturnTypeExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseLambdaReturnTypeResolution() {
            builder.AddAnalyzer(new LambdaReturnTypeAnalyzer());
            return builder;
        }
    }
}