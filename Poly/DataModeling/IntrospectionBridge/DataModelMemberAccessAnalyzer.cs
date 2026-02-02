using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.DataModeling.IntrospectionBridge;

/// <summary>
/// Metadata marking a node as needing transformation to DataModelPropertyAccessor.
/// </summary>
internal sealed record DataModelPropertyAccessMetadata(DataModelPropertyAccessor Replacement) : IAnalysisMetadata;

/// <summary>
/// Analyzes MemberAccess nodes and transforms them to DataModelPropertyAccessor 
/// when accessing properties of DataModel types (DataTypeDefinition).
/// </summary>
/// <remarks>
/// This analyzer runs after semantic analysis and identifies member accesses on
/// dictionary-backed DataModel types, storing replacement nodes as metadata.
/// Works with both legacy DataTypeDefinition and new AstTypeDefinition.
/// </remarks>
public sealed class DataModelMemberAccessAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node)
    {
        // Post-order: analyze children first
        this.AnalyzeChildren(context, node);

        // Only handle MemberAccess nodes
        if (node is not MemberAccess memberAccess)
            return;

        // Check if the instance type is dictionary-backed (DataModel type)
        var instanceType = context.GetResolvedType(memberAccess.Value);
        if (instanceType == null)
            return;

        // Both DataTypeDefinition and AstTypeDefinition use IDictionary<string, object>
        if (instanceType.ReflectedType != typeof(IDictionary<string, object>))
            return;

        // Find the property being accessed
        var property = instanceType.Members
            .OfType<ITypeProperty>()
            .FirstOrDefault(p => p.Name == memberAccess.MemberName);

        if (property == null) {
            context.ReportDiagnostic(
                memberAccess,
                DiagnosticSeverity.Error,
                $"Property '{memberAccess.MemberName}' not found on type '{instanceType.Name}'",
                "DATAMODEL001"
            );
            return;
        }

        // Create the replacement node
        var replacement = new DataModelPropertyAccessor(
            memberAccess.Value,
            memberAccess.MemberName,
            property.MemberTypeDefinition
        );

        // Store as metadata (LinqExpressionGenerator will use the replacement)
        context.SetMetadata(memberAccess, new DataModelPropertyAccessMetadata(replacement));
    }
}

/// <summary>
/// Extension methods for DataModel integration with the analysis system.
/// </summary>
public static class DataModelAnalyzerExtensions {
    /// <summary>
    /// Adds DataModel member access transformation to the analyzer.
    /// </summary>
    public static AnalyzerBuilder UseDataModelTransforms(this AnalyzerBuilder builder)
    {
        builder.AddAnalyzer(new DataModelMemberAccessAnalyzer());
        return builder;
    }

    /// <summary>
    /// Gets the DataModel property accessor replacement for a node, if available.
    /// </summary>
    public static DataModelPropertyAccessor? GetDataModelReplacement(this AnalysisResult result, Node node)
    {
        return result.GetMetadata<DataModelPropertyAccessMetadata>(node)?.Replacement;
    }
}