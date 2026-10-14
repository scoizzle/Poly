using Poly.Grammar;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// The C# language surface: one grammar table drives both directions.  The
/// write half (<see cref="CSharpWriter"/>) prints AST nodes as C# through
/// <see cref="Printer"/>; the read half (<see cref="CSharpReader"/>) matches the
/// same patterns and folds the result tree back into AST nodes.
///
/// Roslyn is the external oracle for both directions: printed output must parse
/// with zero syntax diagnostics, and the reader's view of a file is compared
/// against Roslyn's syntax tree in the test suite.
/// </summary>
public static class CSharpLanguage {
    public static Grammar<CSharpToken, CSharpTokenKind> Grammar => CSharpGrammar.Table;

    public static Func<CSharpTokenKind, string> Canonical => CSharpGrammar.CanonicalText;

    public static Printer<CSharpToken, CSharpTokenKind> Printer { get; } =
        new(CSharpGrammar.Table, CSharpGrammar.CanonicalText, () => new CSharpTokenWriter());

    /// <summary>Prints one node as a C# statement/expression fragment.</summary>
    public static string Write(Node node) => new CSharpWriter().Write(node);

    /// <summary>Prints a compilation unit.</summary>
    public static string Write(CompilationUnitNode unit) => new CSharpWriter().Write(unit);

    /// <summary>Prints type definitions with an optional leading statement block.</summary>
    public static string Write(IReadOnlyList<TypeDefinitionNode> typeDefs, IReadOnlyList<Node>? testStatements = null) =>
        new CSharpWriter().Write(typeDefs, testStatements);

    /// <summary>Reads a C# compilation unit back into a Poly AST.</summary>
    public static CompilationUnitNode Read(string text) => CSharpReader.Read(text);
}