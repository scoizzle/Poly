namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class ThisReferenceContextAnalyzer : INodeAnalyzer {
    public const string Id = "ThisReference";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ThisReferenceContextAnalyzer>(node)) {
            return;
        }

        if (node is ThisReference thisReference && context.GetResolvedType(thisReference) is null) {
            context.ReportDiagnostic(thisReference, DiagnosticSeverity.Error, "'this' can only be used inside an instance member body.", "TH0002");
            this.AnalyzeChildren(context, node);
            return;
        }

        if (node is not TypeDefinitionNode typeDefinitionNode) {
            this.AnalyzeChildren(context, node);
            return;
        }

        var declaringType = context.GetMetadata<TypeDefinitionMetadata>(typeDefinitionNode)?.TypeDefinition;
        if (declaringType == null) {
            this.AnalyzeChildren(context, node);
            return;
        }

        if (typeDefinitionNode.Constructors != null) {
            foreach (var constructor in typeDefinitionNode.Constructors) {
                if (constructor.Body != null) {
                    AnnotateBody(context, constructor.Body, declaringType, isStatic: false);
                }
            }
        }

        if (typeDefinitionNode.Methods != null) {
            foreach (var method in typeDefinitionNode.Methods) {
                if (method.Body != null) {
                    AnnotateBody(context, method.Body, declaringType, method.IsStatic);
                }
            }
        }

        if (typeDefinitionNode.Properties != null) {
            foreach (var property in typeDefinitionNode.Properties) {
                if (property.Getter?.Body != null) {
                    AnnotateBody(context, property.Getter.Body, declaringType, property.IsStatic);
                }

                if (property.Setter?.Body != null) {
                    AnnotateBody(context, property.Setter.Body, declaringType, property.IsStatic);
                }

                if (property.Initializer?.Value != null) {
                    AnnotateBody(context, property.Initializer.Value, declaringType, property.IsStatic);
                }
            }
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnnotateBody(AnalysisContext context, Node body, ITypeDefinition declaringType, bool isStatic) {
        if (body is ThisReference thisReference) {
            ResolveThisReference(context, thisReference, declaringType, isStatic);
            return;
        }

        foreach (var child in body.Children.Where(static child => child is not null)) {
            AnnotateBody(context, child!, declaringType, isStatic);
        }
    }

    private static void ResolveThisReference(AnalysisContext context, ThisReference thisReference, ITypeDefinition declaringType, bool isStatic) {
        context.SetResolvedType(thisReference, declaringType);

        if (isStatic) {
            context.ReportDiagnostic(thisReference, DiagnosticSeverity.Error, "'this' cannot be used inside a static member body.", "TH0001");
        }
    }
}

public static class ThisReferenceContextMetadataExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseThisReferenceContext() {
            builder.AddAnalyzer(new ThisReferenceContextAnalyzer());
            return builder;
        }
    }
}