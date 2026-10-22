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

    [Test]
    public async Task Write_InterpolatedString_ParsesWithRoslynAndRoundTrips() {
        var unit = CSharpLanguage.Read(
            "class C { void M() { var s = $\"x = {a}, y = {{lit}}\"; } }");
        var text = CSharpLanguage.Write(unit);
        await Assert.That(text).Contains("$\"x = {a}, y = {{lit}}\"");
        await Assert.That(SyntaxErrors(text)).IsEmpty();
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(text))).IsEqualTo(text);
    }

    // ── switch cases ─────────────────────────────────────────────────────────

    [Test]
    public async Task SwitchCase_PrintsTerminatingBreak() {
        var text = CSharpLanguage.Write(PlainSwitch());
        await Assert.That(Occurrences(text, "break;")).IsEqualTo(3)
            .Because("each case body and the default case need a terminator");
        await Assert.That(text).Contains("case 1:");
        await Assert.That(text).Contains("default:");
    }

    [Test]
    public async Task Switch_WriteRead_RoundTrips() {
        var original = PlainSwitch();
        var printed = CSharpLanguage.Write(original);
        await Assert.That(Occurrences(printed, "break;")).IsEqualTo(3)
            .Because("the round trip must read back the terminating break the writer prints");
        var reread = SingleSwitch(printed);
        await Assert.That(Shape(reread)).IsEqualTo(Shape(original))
            .Because("a trailing break is syntax, so it must not become a node or a block");
        await Assert.That(Shape(reread)).DoesNotContain("BreakStatement");
    }

    [Test]
    public async Task SwitchWithBreak_Reads_WithoutAddingANode() {
        var reread = SingleSwitch(
            "switch (x) { case 1: a(); break; default: b(); break; }");
        await Assert.That(reread.Cases.Count).IsEqualTo(1);
        await Assert.That(reread.Cases[0].Body).IsTypeOf<Invoke>();
        await Assert.That(reread.DefaultCase).IsTypeOf<Invoke>();
    }

    [Test]
    public async Task SwitchCase_EndingInReturn_GetsNoBreak() {
        Node[] terminators = [
            new Return(new Constant(2)),
            new ThrowStatement(new Variable("error")),
            new BreakStatement(),
            new ContinueStatement(),
            new GotoStatement("done"),
        ];
        foreach (var terminator in terminators) {
            var text = CSharpLanguage.Write(
                new SwitchStatement(new Variable("x"), [new SwitchCase(new Constant(1), terminator)], terminator));
            await Assert.That(Occurrences(text, "break;")).IsEqualTo(terminator is BreakStatement ? 2 : 0)
                .Because($"case body {terminator.GetType().Name} already terminates");
        }
    }

    [Test]
    public async Task PrintedSwitch_ParsesWithRoslyn() {
        var switchText = CSharpLanguage.Write(new SwitchStatement(
            new Variable("x"),
            [
                new SwitchCase(new Constant(1), ConsoleWrite(1)),
                new SwitchCase(new Constant(2), ConsoleWrite(2)),
            ],
            ConsoleWrite(0)));
        var text = $"class C {{ void M(int x) {{ {switchText} }} }}";
        await Assert.That(CompilationErrors(text)).IsEmpty();
    }

    // ── for headers ──────────────────────────────────────────────────────────

    private const string ForSource = "for (var i = 0; i < 3; i = i + 1) { }";

    [Test]
    public async Task ForIncrement_PrintsAsAnExpression() {
        var text = CSharpLanguage.Write(CSharpLanguage.Read(ForSource));
        await Assert.That(text).Contains("for (var i = 0; i < 3; i = i + 1)")
            .Because("the increment slot holds an expression, so it must not declare the variable");
        await Assert.That(text).DoesNotContain("var i = i");
    }

    [Test]
    public async Task ForCondition_Assignment_PrintsAsAnExpression() {
        var text = CSharpLanguage.Write(CSharpLanguage.Read("for (var i = 0; (ready = i < 3); i = i + 1) { }"));
        await Assert.That(text).Contains("ready = i < 3");
        await Assert.That(text).DoesNotContain("var ready");
    }

    [Test]
    public async Task ForHeader_ParsesWithRoslyn() {
        var text = CSharpLanguage.Write(CSharpLanguage.Read(ForSource));
        await Assert.That(SyntaxErrors(text)).IsEmpty();
    }

    [Test]
    public async Task ForLoop_WriteRead_RoundTrips() {
        var printed = CSharpLanguage.Write(CSharpLanguage.Read(ForSource));
        var reread = CSharpLanguage.Read(printed);
        await Assert.That(CSharpLanguage.Write(reread)).IsEqualTo(printed);
    }

    [Test]
    public async Task ForBody_StillFusesTheDeclaration() {
        var text = CSharpLanguage.Write(CSharpLanguage.Read("for (var i = 0; i < 3; i = i + 1) { total = i; }"));
        await Assert.That(text).Contains("var total = i;")
            .Because("the body keeps the statement rule that fuses an undeclared name into its declaration");
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

    private static SwitchStatement PlainSwitch() => new(
        new Variable("x"),
        [
            new SwitchCase(new Constant(1), new Invoke(new Variable("a"))),
            new SwitchCase(new Constant(2), new Invoke(new Variable("b"))),
        ],
        new Invoke(new Variable("c")));

    private static Invoke ConsoleWrite(object value) =>
        new(new Member(new TypeReference("System.Console"), "WriteLine"), new Constant(value));

    private static SwitchStatement SingleSwitch(string statementText) {
        var unit = CSharpLanguage.Read($"class C {{ void M() {{ {statementText} }} }}");
        var found = new List<SwitchStatement>();
        Collect(unit);
        return found.Count == 1
            ? found[0]
            : throw new InvalidOperationException($"Expected one switch, found {found.Count}.");

        void Collect(Node node) {
            if (node is SwitchStatement sw) {
                found.Add(sw);
                return;
            }
            foreach (var child in node.Children.OfType<Node>())
                Collect(child);
        }
    }

    private static string Shape(Node? node) {
        if (node is null)
            return "null";
        var leaf = node switch {
            Variable variable => variable.Name,
            Constant constant => constant.ToString(),
            _ => null,
        };
        if (leaf is not null)
            return $"{node.GetType().Name}({leaf})";
        var children = node.Children.OfType<Node>().Select(Shape);
        return $"{node.GetType().Name}[{string.Join(",", children)}]";
    }

    private static int Occurrences(string text, string token) {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static IReadOnlyList<string> CompilationErrors(string text) {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "PrintedSwitch",
            [CSharpSyntaxTree.ParseText(text)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return [.. compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()}")];
    }

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