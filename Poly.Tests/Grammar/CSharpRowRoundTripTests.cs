using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

namespace Poly.Tests.Grammar;

/// <summary>
/// G-23b: round-trip tests for the C# surface defects the pattern-row check found (#848, #849, enum
/// values, the fused declaration, block lambdas, labels), and the guard for the one dead DSL row.
/// </summary>
public class CSharpRowRoundTripTests {
    /// <summary>Green guard: the shadowed <c>annotation/no-args</c> row is deleted; a bare annotation matches <c>with-args</c>.</summary>
    [Test]
    public async Task Annotation_BareCall_MatchesWithArgs() {
        var matcher = new Matcher<DslToken, DslTokenKind>(DslGrammar.Build(), new DslTokenReader("label()"));
        var match = matcher.TryMatch("annotation");
        await Assert.That(match?.PatternName).IsEqualTo("with-args");
    }

    // ── #849: an empty enum ──────────────────────────────────────────────────

    public static IEnumerable<TypeDefinitionNode> EmptyEnums() => [
        new TypeDefinitionNode("Empty", Kind: TypeKind.Enum),
        new TypeDefinitionNode("Empty", Fields: [], Kind: TypeKind.Enum),
    ];

    [Test]
    [MethodDataSource(nameof(EmptyEnums))]
    public async Task EmptyEnum_ReReadsAsAnEmptyEnum(TypeDefinitionNode empty) {
        var printed = CSharpLanguage.Write([empty]);
        var reRead = CSharpLanguage.Read(printed).Types.Single();

        await Assert.That(reRead.Name).IsEqualTo("Empty");
        await Assert.That(reRead.EffectiveKind).IsEqualTo(TypeKind.Enum);
        await Assert.That(reRead.Fields?.Count ?? 0).IsEqualTo(0);
        await Assert.That(CSharpLanguage.Write([reRead])).IsEqualTo(printed);
        await Assert.That(printed.ReplaceLineEndings("\n")).IsEqualTo("#nullable enable\npublic enum Empty\n{}");
    }

    // ── #848: an interface body ──────────────────────────────────────────────

    private const string ShapeInterface = "interface IShape { int Sides { get; } long Area(long scale); }";

    [Test]
    public async Task Interface_WithMembers_ReReads() {
        var original = CSharpLanguage.Read(ShapeInterface).Types.Single();
        var printed = CSharpLanguage.Write(CSharpLanguage.Read(ShapeInterface));
        var reRead = CSharpLanguage.Read(printed).Types.Single();

        await Assert.That(reRead.EffectiveKind).IsEqualTo(TypeKind.Interface);
        await Assert.That(reRead.Properties!.Select(p => p.Name)).IsEquivalentTo(original.Properties!.Select(p => p.Name));
        await Assert.That(reRead.Methods!.Select(m => m.Name)).IsEquivalentTo(original.Methods!.Select(m => m.Name));
        await Assert.That(reRead.Properties![0].Getter is not null).IsTrue();
        await Assert.That(reRead.Properties![0].Setter is null).IsTrue();
        await Assert.That(reRead.Methods![0].Body is null).IsTrue();
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
        await Assert.That(printed).Contains("{").And.Contains("}");
    }

    [Test]
    public async Task Interface_Empty_ReReads() {
        var printed = CSharpLanguage.Write([new TypeDefinitionNode("IEmpty", Kind: TypeKind.Interface)]);
        var reRead = CSharpLanguage.Read(printed).Types.Single();

        await Assert.That(reRead.Name).IsEqualTo("IEmpty");
        await Assert.That(reRead.EffectiveKind).IsEqualTo(TypeKind.Interface);
        await Assert.That(CSharpLanguage.Write([reRead])).IsEqualTo(printed);
    }

    [Test]
    public async Task Interface_MembersPrintThroughMemberRows() {
        var unit = CSharpLanguage.Read(ShapeInterface);
        using var scope = PrintTrace.Begin();
        CSharpLanguage.Write(unit);

        var rows = scope.Observations.Where(o => ReferenceEquals(o.Grammar, CSharpLanguage.Grammar)).ToArray();
        await Assert.That(rows.Where(o => o.Rule == "member" && o.Pattern == "method").Select(o => o.Text))
            .IsEquivalentTo(new[] { "public long Area(long scale);" });
        await Assert.That(rows.Where(o => o.Rule == "member" && o.Pattern == "property").Select(o => o.Text))
            .IsEquivalentTo(new[] { "public int Sides { get; }" });
    }

    /// <summary>The class convention: a property with no accessors prints <c>{ get; set; }</c>.</summary>
    [Test]
    public async Task Interface_PropertyWithNoAccessors_TakesTheClassConvention() {
        var printed = CSharpLanguage.Write([new TypeDefinitionNode("I", Kind: TypeKind.Interface,
            Properties: [new PropertyDefinitionNode("P", TypeReference.To<int>())])]);
        await Assert.That(printed).Contains("System.Int32 P { get; set; }");
        await Assert.That(CSharpLanguage.Read(printed).Types.Single().Properties!.Single().Setter is not null).IsTrue();
    }

    /// <summary>Fail closed: the brace-less text the writer used to print is still refused.</summary>
    [Test]
    public async Task Interface_WithoutBraces_StaysRejected() {
        await Assert.That(() => CSharpLanguage.Read("interface IShape int Sides { get; }")).Throws<FormatException>();
    }

    // ── the fused declaration `var x = v` ────────────────────────────────────

    private static CompilationUnitNode Unit(params Node[] statements) => new([], null, [], statements);

    private static string UnitBody(string printed) {
        var text = printed.ReplaceLineEndings("\n");
        const string header = "// <auto-generated/>\n\n#nullable enable\n\n";
        return text.StartsWith(header, StringComparison.Ordinal) ? text[header.Length..] : text;
    }

    [Test]
    public async Task FusedDeclaration_PrintsThroughTheVarDeclRow() {
        var unit = Unit(new Assignment(new Variable("x"), new Constant(1L)));
        using var scope = PrintTrace.Begin();
        var printed = CSharpLanguage.Write(unit);

        var rows = scope.Observations.Where(o => ReferenceEquals(o.Grammar, CSharpLanguage.Grammar)).ToArray();
        await Assert.That(rows.Where(o => o.Rule == "statement" && o.Pattern == "var-decl").Select(o => o.Text))
            .IsEquivalentTo(new[] { "var x = 1L;" });
        await Assert.That(rows.Where(o => o.Rule == "statement" && o.Pattern == "expr-stmt").Any(o => o.Text.Contains("var ")))
            .IsFalse();
        await Assert.That(UnitBody(printed)).IsEqualTo("var x = 1L;");
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
    }

    /// <summary>An assignment nested in an expression is an ordinary assignment: the fuse is a statement-level rule.</summary>
    [Test]
    public async Task NestedUndeclaredAssignment_PrintsAPlainAssignment_AndReReads() {
        var unit = Unit(new Assignment(new Variable("x"), new Assignment(new Variable("y"), new Constant(1L))));
        var printed = CSharpLanguage.Write(unit);

        await Assert.That(UnitBody(printed)).IsEqualTo("var x = y = 1L;");
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
    }

    /// <summary>
    /// An undeclared assignment as a for initializer is not a declaration: it prints as the assignment it is
    /// (it printed <c>for (var x = 0L;</c> before, which re-read to a different tree).
    /// </summary>
    [Test]
    public async Task UndeclaredForInitializer_PrintsAnAssignment_AndReReadsToTheSameText() {
        var x = new Variable("x");
        var loop = new ForLoop(new Assignment(x, new Constant(0L)), new LessThan(x, new Constant(10L)),
            new Assignment(x, new Add(x, new Constant(1L))), new Block(Array.Empty<Node>(), Array.Empty<Node>()));
        var printed = CSharpLanguage.Write(Unit(loop));

        await Assert.That(UnitBody(printed).Split('\n')[0]).IsEqualTo("for (x = 0L; x < 10L; x = x + 1L)");
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
    }

    /// <summary>Fail closed: a declaration inside a for header slot stays refused.</summary>
    [Test]
    public async Task ForHeader_WithAStatementInTheConditionSlot_StaysRefused() {
        var loop = new ForLoop(null, new Block(new Constant(1L)), null, new Block(new Constant(0L)));
        await Assert.That(() => CSharpLanguage.Write(Unit(loop))).Throws<InvalidOperationException>();
    }

    // ── shadowed-parameter callee lambdas ────────────────────────────────────

    /// <summary>
    /// Green characterisation: an analysis-less writer refuses a lambda callee (no delegate type to cast to),
    /// which is the fail-closed contract; the analysed print of the same unit prints and re-reads.
    /// </summary>
    [Test]
    [Arguments("""var s = "hi"; var n = ((long s) => s + 1)(4); n;""")]
    [Arguments("""var s = 1000; var n = ((long s) => ((long s) => s + 1)(5) + s)(10); n;""")]
    public async Task ShadowedParameterCallee_AnalysedPrint_ReReads(string body) {
        var unit = CSharpLanguage.Read($"class C {{ void M() {{ {body} }} }}");
        await Assert.That(() => CSharpLanguage.Write(unit)).Throws<InvalidOperationException>();

        var analysed = new CSharpWriter(Interpreter.Analyze(unit)).Write(unit);
        await Assert.That(CSharpLanguage.Read(analysed).Types.Single().Methods!.Single().Name).IsEqualTo("M");
    }

    // ── statement/label ──────────────────────────────────────────────────────

    [Test]
    public async Task Label_PrintsItsStatementThroughTheRow() {
        var unit = Unit(new Block([new LabelDeclaration("exit", new Return(new Constant(0L)))], Array.Empty<Node>()));
        using var scope = PrintTrace.Begin();
        var printed = CSharpLanguage.Write(unit);

        var labels = scope.Observations
            .Where(o => ReferenceEquals(o.Grammar, CSharpLanguage.Grammar) && o.Rule == "statement" && o.Pattern == "label")
            .Select(o => o.Text.ReplaceLineEndings("\n")).ToArray();
        await Assert.That(labels).Count().IsEqualTo(1);
        await Assert.That(labels[0]).Contains("exit:").And.Contains("return 0L;");
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
        await Assert.That(CSharpLanguage.Write(new LabelDeclaration("exit", new Return(new Constant(0L))))
            .ReplaceLineEndings("\n")).IsEqualTo("exit:\nreturn 0L;");
    }

    // ── block-bodied lambdas ─────────────────────────────────────────────────

    [Test]
    [Arguments("Func<long, long> f = x => { return x; };", "lambda-block-ident", "x => {")]
    [Arguments("Func<long, long> f = (long x) => { return x; };", "lambda-block-parens", "(long x) => {")]
    [Arguments("Func<Task, Task> f = async x => { await x; };", "lambda-block-ident", "async x => {")]
    [Arguments("Func<Task, Task> f = async (Task x) => { await x; };", "lambda-block-parens", "async (Task x) => {")]
    public async Task BlockLambda_PrintsThroughItsOwnRow(string source, string pattern, string prefix) {
        var unit = CSharpLanguage.Read(source);
        using var scope = PrintTrace.Begin();
        var printed = CSharpLanguage.Write(unit);

        var rows = scope.Observations.Where(o => ReferenceEquals(o.Grammar, CSharpLanguage.Grammar)).ToArray();
        await Assert.That(rows.Where(o => o.Rule == "expr-primary" && o.Pattern == pattern).Select(o => o.Text.Split('\n')[0]))
            .IsEquivalentTo(new[] { prefix });
        await Assert.That(rows.Where(o => o.Rule == "expr-primary" && o.Pattern is "lambda-ident" or "lambda-parens")
            .Any(o => o.Text.Contains("=> {"))).IsFalse();
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
    }

    /// <summary>Green guard: an expression-bodied lambda still prints through <c>lambda-ident</c>.</summary>
    [Test]
    public async Task ExpressionLambda_StillPrintsThroughItsExpressionRow() {
        var unit = CSharpLanguage.Read("Func<long, long> f = x => x;");
        using var scope = PrintTrace.Begin();
        CSharpLanguage.Write(unit);
        await Assert.That(scope.Observations.Any(o => o.Rule == "expr-primary" && o.Pattern == "lambda-ident")).IsTrue();
    }

    // ── enum values ──────────────────────────────────────────────────────────

    [Test]
    public async Task EnumValues_SurviveAReRead() {
        var printed = CSharpLanguage.Write(CSharpLanguage.Read("enum E { A = 5, B = 7 }"));
        var text = printed.ReplaceLineEndings("\n");

        await Assert.That(text).Contains("A = 5,").And.Contains("B = 7\n");
        var fields = CSharpLanguage.Read(printed).Types.Single().Fields!;
        await Assert.That(fields.Select(f => Convert.ToInt64(((Constant)f.DefaultValue!).Value)))
            .IsEquivalentTo(new long[] { 5, 7 });
        await Assert.That(CSharpLanguage.Write(CSharpLanguage.Read(printed))).IsEqualTo(printed);
    }

    /// <summary>The domain exporter stores member-name strings as defaults; they print as ordinals, as before.</summary>
    [Test]
    public async Task EnumMember_WithANonIntegralDefault_PrintsItsOrdinal() {
        var text = CSharpLanguage.Write([new TypeDefinitionNode("E", Kind: TypeKind.Enum, Fields: [
            new FieldDefinitionNode("A", TypeReference.To<int>(), new Constant("A"), IsStatic: true, IsConst: true),
            new FieldDefinitionNode("B", TypeReference.To<int>(), null, IsStatic: true, IsConst: true),
        ])]).ReplaceLineEndings("\n");
        await Assert.That(text).Contains("A = 0,").And.Contains("B = 1\n");
    }

    /// <summary>Green guard: the fold flattens the wrapper rule, so every member survives a re-read.</summary>
    [Test]
    public async Task EnumMembers_ReReadKeepsEveryMember() {
        var unit = CSharpLanguage.Read("enum E { A = 1, B = 2, C = 3 }");
        await Assert.That(unit.Types.Single().Fields!.Select(f => f.Name)).IsEquivalentTo(new[] { "A", "B", "C" });
        var reRead = CSharpLanguage.Read(CSharpLanguage.Write(unit));
        await Assert.That(reRead.Types.Single().Fields!.Select(f => f.Name)).IsEquivalentTo(new[] { "A", "B", "C" });
    }

    /// <summary>Green guard: the member list stays one list; a leading comma is not an empty member.</summary>
    [Test]
    public async Task EnumLeadingComma_StaysRejected() {
        await Assert.That(() => CSharpLanguage.Read("enum E { , A = 0 }")).Throws<FormatException>();
    }
}