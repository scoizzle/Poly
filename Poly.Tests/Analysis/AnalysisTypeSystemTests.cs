using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Analysis;

/// <summary>
/// The analysis resolves through its own type system (#998 T12a; docs/decisions/2026-11-19-a-type-system-owns-definitions-
/// vocabulary-and-rules.md §2). A unit's declarations are its layer's own sources; the CLR is reached only as the target
/// chain's answer, so an analysis given only the ledger resolves no CLR name and binds no C# keyword.
/// </summary>
public class AnalysisTypeSystemTests {
    private static readonly Analyzer Analyzer = new AnalyzerBuilder()
        .UseTypeDefinitionNodeAnalyzer()
        .UseUnitTypeScope()
        .UseTypeAndMemberResolver()
        .UseSyntaxTypeCompatibility()
        .Build();

    private static AnalysisResult Analyze(Node node, ITypeSystem? target = null) =>
        Analyzer.Analyze(node, settings: target is null ? null : AnalysisSettings.Default.With<ITypeSystem>(target));

    [Test]
    public async Task QualifiedClrName_IsUnresolved_UnderALedgerTarget() {
        var created = new New(new NamedTypeReference("StringBuilder", "System.Text"));

        var underLedger = Analyze(created, new LedgerHost());

        await Assert.That(underLedger.GetResolvedType(created)).IsNull()
            .Because("the ledger's chain holds no System.Text.StringBuilder");
        await Assert.That(underLedger.HasErrors).IsTrue();
        await Assert.That(underLedger.Diagnostics.Select(static diagnostic => diagnostic.Message))
            .Contains(static message => message.Contains("System.Text.StringBuilder", StringComparison.Ordinal));
    }

    [Test]
    public async Task BareClrName_UnderAnImportedNamespace_IsUnresolved_UnderALedgerTarget() {
        var created = new New(new NamedTypeReference("StringBuilder"));
        var unit = new CompilationUnitNode(["System.Text"], null, [], [created]);

        var underLedger = Analyze(unit, new LedgerHost());

        await Assert.That(underLedger.GetResolvedType(created.Type)).IsNull()
            .Because("using System.Text imports a namespace none of the ledger's chain sources declares");
        await Assert.That(underLedger.HasErrors).IsTrue();
        await Assert.That(underLedger.Diagnostics.Select(static diagnostic => diagnostic.Message))
            .Contains(static message => message.Contains("StringBuilder", StringComparison.Ordinal));
    }

    [Test]
    public async Task Keyword_IsUnbound_UnderALedgerTarget() {
        var ledger = new LedgerHost();
        await Assert.That(ledger.Bind("int")).IsNull().Because("the ledger has no words of its own");

        var underLedger = new AnalysisContext(AnalysisSettings.Default.With<ITypeSystem>(ledger));
        await Assert.That(underLedger.TypeSystem.Bind("int")).IsNull()
            .Because("the ledger binds no word and no C# sits beneath it");

        var underCSharp = new AnalysisContext();
        await Assert.That(underCSharp.TypeSystem.Bind("int")?.Name).IsEqualTo("Int32")
            .Because("the C# target's own vocabulary still answers");
    }

    [Test]
    public async Task ClrByTypeLookup_FollowsTheTargetChain_AndAnswersNullWithNoClrBeneath() {
        var underCSharp = new AnalysisContext();
        await Assert.That(underCSharp.TypeSystem.ClrDefinitionOf(typeof(decimal)))
            .IsSameReferenceAs(Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(decimal)))
            .Because("the default target's sources hold the CLR registry");

        var underLedger = new AnalysisContext(AnalysisSettings.Default.With<ITypeSystem>(new LedgerHost()));
        await Assert.That(underLedger.TypeSystem.ClrDefinitionOf(typeof(decimal))).IsNull()
            .Because("a chain with no CLR source beneath it states no CLR type");
    }

    [Test]
    public async Task Declaration_OutranksTheUsing_ForABareName() {
        // The precedence pin: a unit's declaration is its layer's own source, so it answers before the imported
        // namespace does, and the import is not an ambiguity.
        var reference = new NamedTypeReference("Uri");
        var unit = new CompilationUnitNode(["System"], null, [new TypeDefinitionNode("Uri")], [reference]);

        var analysis = Analyze(unit);

        await Assert.That(analysis.GetResolvedType(reference)?.FullName).IsEqualTo("Uri")
            .Because("the declaration's full name is unqualified; the CLR's System.Uri is not the answer");
        await Assert.That(analysis.Diagnostics.Select(static diagnostic => diagnostic.Message))
            .DoesNotContain(static message => message.Contains("ambiguous", StringComparison.Ordinal));
    }
}