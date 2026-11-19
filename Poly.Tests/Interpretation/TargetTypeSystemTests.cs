using Poly.Analysis;
using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The analysis asks its target type system, not a definition, which operator applies (#998 T4a;
/// docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md §2.3). The target is an analysis
/// setting; with none, it is C# over the CLR.
/// </summary>
public class TargetTypeSystemTests {
    private static readonly Analyzer Analyzer = new AnalyzerBuilder()
        .UseTypeDefinitionNodeAnalyzer()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseSyntaxTypeCompatibility()
        .Build();

    private static AnalysisResult Analyze(Node node, ITypeSystem? target = null) =>
        Analyzer.Analyze(node, settings: target is null ? null : AnalysisSettings.Default.With<ITypeSystem>(target));

    [Test]
    public async Task BinaryOperator_TheTargetDecides() {
        var sum = new Add(new Constant(1), new Constant(2));

        await Assert.That(Analyze(sum).HasErrors).IsFalse().Because("C#, the default target, has int + int");

        var underLedger = Analyze(new Add(new Constant(1), new Constant(2)), new LedgerHost());
        await Assert.That(underLedger.HasErrors).IsTrue().Because("the ledger has no operator for two CLR ints");
        await Assert.That(underLedger.Diagnostics.Select(static d => d.Message)).Contains(static m => m.Contains("no Add operator", StringComparison.Ordinal));
    }

    [Test]
    public async Task UnaryOperator_TheTargetDecides() {
        await Assert.That(Analyze(new UnaryMinus(new Constant(1))).HasErrors).IsFalse();

        var underLedger = Analyze(new UnaryMinus(new Constant(1)), new LedgerHost());
        await Assert.That(underLedger.HasErrors).IsTrue().Because("the ledger has no unary minus for a CLR int");
        await Assert.That(underLedger.Diagnostics.Select(static d => d.Message)).Contains(static m => m.Contains("no UnaryMinus operator", StringComparison.Ordinal));
    }

    [Test]
    public async Task Context_TargetIsTheSetting_ElseCSharp() {
        var ledger = new LedgerHost();
        await Assert.That(new AnalysisContext().TypeSystem).IsSameReferenceAs(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        await Assert.That(new AnalysisContext(Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared,
            AnalysisSettings.Default.With<ITypeSystem>(ledger)).TypeSystem).IsSameReferenceAs(ledger);
    }
}