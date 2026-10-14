using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.CSharp;

/// <summary>C# source code generator. Produces readable C# code from analyzed
/// AST nodes through the grammar-backed <see cref="CSharpWriter"/>.</summary>
/// <remarks>This is a thin compatibility façade: the implementation is the
/// Poly.Grammar C# language surface (<see cref="CSharpLanguage"/>), where the
/// pattern table drives both printing and reading. New call sites should use
/// <see cref="CSharpLanguage"/> directly.
///
/// The generated code uses modern C# features (nullable enable, switch
/// expressions, pattern matching) and is formatted with standard indentation.</remarks>
public sealed class CSharpGenerator {
    private readonly CSharpWriter _writer;

    /// <summary>Creates a generator without analysis metadata. Some type-aware
    /// features (e.g. resolved member names) may be unavailable.</summary>
    public CSharpGenerator() => _writer = new CSharpWriter();

    /// <summary>Creates a generator with semantic analysis results for
    /// type-aware code generation.</summary>
    /// <param name="analysisResult">The semantic analysis result.</param>
    public CSharpGenerator(AnalysisResult analysisResult) => _writer = new CSharpWriter(analysisResult);

    /// <summary>Generates C# source code for a single AST node.</summary>
    /// <param name="node">The node to generate (expression, statement, block,
    /// or type definition).</param>
    /// <returns>Formatted C# source code.</returns>
    public string Generate(Node node) => _writer.Write(node);

    /// <summary>Generates C# source code for a complete compilation unit.</summary>
    /// <param name="unit">The compilation unit to generate.</param>
    /// <returns>Formatted C# source code.</returns>
    public string Generate(CompilationUnitNode unit) => _writer.Write(unit);

    /// <summary>Generates C# source code for a collection of type definitions.</summary>
    /// <param name="typeDefs">The type definitions to emit.</param>
    /// <returns>Formatted C# source code with nullable enabled.</returns>
    public string Generate(IReadOnlyList<TypeDefinitionNode> typeDefs) => _writer.Write(typeDefs);

    /// <summary>Generates C# source code for type definitions preceded by
    /// optional test statements (e.g. usage examples or setup code).</summary>
    /// <param name="typeDefs">The type definitions to emit.</param>
    /// <param name="testStatements">Optional statements to emit before the
    /// type definitions (typically usage or setup code).</param>
    /// <returns>Formatted C# source code.</returns>
    public string Generate(IReadOnlyList<TypeDefinitionNode> typeDefs, IReadOnlyList<Node>? testStatements) =>
        _writer.Write(typeDefs, testStatements);
}