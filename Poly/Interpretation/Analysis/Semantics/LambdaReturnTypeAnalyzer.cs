using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class LambdaReturnTypeAnalyzer : INodeAnalyzer {
    public const string Id = "LambdaReturnType";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (node is Lambda lambda && lambda.Body is not null) {
            // The TypeResolver already resolved the body's type.
            // If the Lambda's own type is still unresolved (typeof(object) fallback),
            // try to get a more precise type from the body.
            var lambdaType = context.GetResolvedType(lambda);
            if (lambdaType?.GetRuntimeType() == typeof(object)) {
                var bodyType = ResolveBodyType(context, lambda.Body);
                if (bodyType is not null && bodyType.GetRuntimeType() != typeof(object))
                    context.SetResolvedType(lambda, bodyType);
            }
        }

        if (node is Invoke invoke && invoke.Delegate is Lambda invokedLambda) {
            var lambdaReturnType = context.GetResolvedType(invokedLambda);
            if (lambdaReturnType is not null) {
                var currentType = context.GetResolvedType(invoke);
                var rt = currentType?.GetRuntimeType();
                if (rt is null || rt == typeof(object)) {
                    context.SetResolvedType(invoke, lambdaReturnType);
                }
            }
        }

        this.AnalyzeChildren(context, node);
    }

    private static ITypeDefinition? ResolveBodyType(AnalysisContext context, Node body) {
        if (body is Block block) {
            if (block.Nodes.Count == 0) return null;
            return context.GetResolvedType(block.Nodes[^1]);
        }
        return context.GetResolvedType(body);
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