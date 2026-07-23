namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a single .cs file: usings, namespace, type definitions,
/// and optional top-level statements.
/// </summary>
/// <param name="Usings">Using directives for the file.</param>
/// <param name="Namespace">Optional namespace for the file.</param>
/// <param name="Types">Type definitions in the file.</param>
/// <param name="TopLevelStatements">Optional top-level statements (e.g. entry point).</param>
public sealed record CompilationUnitNode(
    IReadOnlyList<string> Usings,
    string? Namespace,
    IReadOnlyList<TypeDefinitionNode> Types,
    IReadOnlyList<Node>? TopLevelStatements
) : Node {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var t in Types) yield return t;
            if (TopLevelStatements != null)
                foreach (var s in TopLevelStatements) yield return s;
        }
    }

    public override string ToString() {
        var ns = Namespace != null ? $"namespace {Namespace}" : "<no namespace>";
        return $"CompilationUnit({ns}, {Types.Count} types)";
    }
}