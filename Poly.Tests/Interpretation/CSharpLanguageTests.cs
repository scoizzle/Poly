using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The C# language surface read/write cycle validated against Roslyn:
/// printed output must parse with zero syntax diagnostics, and the reader's
/// view of a file is compared node-count-for-node-count against Roslyn's syntax
/// tree. Round-trips must be idempotent (write → read → write is stable).
/// </summary>
public class CSharpLanguageTests {
    // ── writer → Roslyn ──────────────────────────────────────────────────────

    [Test]
    public async Task Write_Expression_WritesExpectedText() {
        var node = new Add(new Constant(1), new Multiply(new Constant(2), new Constant(3)));
        await Assert.That(CSharpLanguage.Write(node)).IsEqualTo("1 + 2 * 3;");
    }

    [Test]
    public async Task Write_ParenthesizesLowerPrecedenceChild() {
        var node = new Multiply(new Add(new Constant(1), new Constant(2)), new Constant(3));
        await Assert.That(CSharpLanguage.Write(node)).IsEqualTo("(1 + 2) * 3;");
    }

    [Test]
    public async Task Write_TokenizerRoundTripsLiterals() {
        var node = new Block(
            new Assignment(new Variable("name"), new Constant("he said \"hi\"\n")),
            new Assignment(new Variable("count"), new Constant(42L)),
            new Assignment(new Variable("ratio"), new Constant(1.5)));
        var text = CSharpLanguage.Write(node);
        await Assert.That(text).Contains("\\\"hi\\\"");
        await Assert.That(text).Contains("42L");
        await Assert.That(text).Contains("1.5");

        var unit = CSharpLanguage.Read($"class C {{ void M() {text} }}");
        await Assert.That(SyntaxErrors(CSharpLanguage.Write(unit))).IsEmpty();
    }

    [Test]
    public async Task Write_GenericType_ClosesNestedTypeArguments() {
        var type = new NamedTypeReference("Dictionary", TypeArguments: [
            new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.String),
            new CollectionTypeReference(new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int64)),
        ]);
        var field = new FieldDefinitionNode("Prices", type);
        var text = CSharpLanguage.Write([new TypeDefinitionNode("Ledger", Fields: [field])]);
        await Assert.That(text).Contains("Dictionary<string, List<long>>");
        await Assert.That(SyntaxErrors(text)).IsEmpty();
    }

    // ── reader → Roslyn ──────────────────────────────────────────────────────

    [Test]
    [MethodDataSource(nameof(GoldenFiles))]
    public async Task Read_GoldenFile_AgreesWithRoslyn(string path) {
        var text = await File.ReadAllTextAsync(path);
        var unit = CSharpLanguage.Read(text);

        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetRoot();

        await Assert.That(unit.Types.Count(t => (t.TypeCategory & Poly.Introspection.TypeCategory.Enumeration) == Poly.Introspection.TypeCategory.None))
            .IsEqualTo(root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count())
            .Because($"{Path.GetFileName(path)} type declarations");
        await Assert.That(unit.Types.Count(t => (t.TypeCategory & Poly.Introspection.TypeCategory.Enumeration) != Poly.Introspection.TypeCategory.None))
            .IsEqualTo(root.DescendantNodes().OfType<EnumDeclarationSyntax>().Count())
            .Because($"{Path.GetFileName(path)} enum declarations");

        var poly = PolyCounts(unit);
        var mismatches = new List<string>();
        void Check(string category, int polyCount, int roslynCount) {
            if (polyCount != roslynCount)
                mismatches.Add($"{category}: poly={polyCount} roslyn={roslynCount}");
        }
        Check("methods", poly.Methods,
            root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count()
            + root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Count());
        Check("properties", poly.Properties, root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count());
        Check("fields", poly.Fields, root.DescendantNodes().OfType<FieldDeclarationSyntax>().Count());
        Check("enum-members", poly.EnumMembers, root.DescendantNodes().OfType<EnumMemberDeclarationSyntax>().Count());
        Check("invocations", poly.Invocations, root.DescendantNodes().OfType<InvocationExpressionSyntax>().Count());
        Check("object-creations", poly.ObjectCreations, root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Count());
        Check("lambdas", poly.Lambdas, root.DescendantNodes().OfType<LambdaExpressionSyntax>().Count());
        await Assert.That(mismatches).IsEmpty()
            .Because($"{Path.GetFileName(path)}: {string.Join("; ", mismatches)}");
    }

    [Test]
    [MethodDataSource(nameof(GoldenFiles))]
    public async Task Read_GoldenFile_RoundTripIsIdempotent(string path) {
        var text = await File.ReadAllTextAsync(path);
        var unit = CSharpLanguage.Read(text);
        var first = CSharpLanguage.Write(unit);
        var second = CSharpLanguage.Write(CSharpLanguage.Read(first));
        await Assert.That(second).IsEqualTo(first).Because(Path.GetFileName(path));
        await Assert.That(SyntaxErrors(first)).IsEmpty().Because($"{Path.GetFileName(path)} printed output");
    }

    [Test]
    [MethodDataSource(nameof(GoldenFiles))]
    public async Task Read_GoldenFile_PrintedOutputParsesWithRoslyn(string path) {
        var text = await File.ReadAllTextAsync(path);
        await Assert.That(SyntaxErrors(text)).IsEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> SyntaxErrors(string text) =>
        [.. CSharpSyntaxTree.ParseText(text)
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()}")];

    private sealed record Counts(int Methods, int Properties, int Fields, int EnumMembers, int Invocations, int ObjectCreations, int Lambdas);

    private static Counts PolyCounts(CompilationUnitNode unit) {
        var methods = 0;
        var properties = 0;
        var fields = 0;
        var enumMembers = 0;
        var invocations = 0;
        var objectCreations = 0;
        var lambdas = 0;
        foreach (var type in unit.Types) {
            if ((type.TypeCategory & Poly.Introspection.TypeCategory.Enumeration) != Poly.Introspection.TypeCategory.None) {
                enumMembers += type.Fields?.Count ?? 0;
                continue;
            }
            foreach (var member in type.Members)
                Walk(member);
        }
        if (unit.TopLevelStatements != null) {
            foreach (var statement in unit.TopLevelStatements)
                Walk(statement);
        }
        return new Counts(methods, properties, fields, enumMembers, invocations, objectCreations, lambdas);

        void Walk(Node current) {
            switch (current) {
                case MethodDefinitionNode: methods++; break;
                case PropertyDefinitionNode: properties++; break;
                case FieldDefinitionNode: fields++; break;
                case Invoke: invocations++; break;
                case New: objectCreations++; break;
                case Lambda: lambdas++; break;
            }
            foreach (var child in current.Children) {
                if (child is not null)
                    Walk(child);
            }
        }
    }

    public static IEnumerable<string> GoldenFiles() {
        var root = FindRepoRoot();
        return Directory
            .GetFiles(Path.Combine(root, "Poly.Tests", "DomainModeling", "Compile", "EmitGolden"), "*.cs.golden", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}