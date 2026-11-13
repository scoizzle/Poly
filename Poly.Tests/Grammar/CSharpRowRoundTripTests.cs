using Poly.DomainModeling.Language;
using Poly.Grammar;
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
