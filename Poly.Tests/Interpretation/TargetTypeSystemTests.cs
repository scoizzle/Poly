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

    [Test]
    public async Task VariableWidening_TheTargetsConversionDecides() {
        static Block Widening() {
            var x = new Variable("x");
            return new Block([new Assignment(x, new Constant(1L)), new Assignment(x, new Constant(2)), x], [x]);
        }

        await Assert.That(Analyze(Widening()).HasErrors).IsFalse().Because("C# widens int to long implicitly");
        var underLedger = Analyze(Widening(), new LedgerHost());
        await Assert.That(underLedger.HasErrors).IsTrue().Because("the ledger has no conversion from a CLR int to a CLR long");
        await Assert.That(underLedger.Diagnostics.Select(static d => d.Message)).Contains(static m => m.Contains("cannot assign 'Int32' to variable 'x'", StringComparison.Ordinal));
    }

    [Test]
    public async Task OverloadChoice_TheTargetsConversionDecides() {
        // Math.Abs has no int-to-long-only shape problem: Abs(int) is exact. Max(long, long) with an int argument needs
        // C#'s implicit widening, which only the target can grant.
        static Node Call() => new Invoke(new Member(Poly.Introspection.CommonLanguageRuntime.ClrTypeReferences.Of(typeof(Math)), "Max"),
            [new Constant(1L), new Constant(2)]);

        await Assert.That(Analyze(Call()).HasErrors).IsFalse();
        await Assert.That(Analyze(Call(), new LedgerHost()).HasErrors).IsTrue()
            .Because("with no conversion from int to long, no Max overload applies");
    }

    [Test]
    public async Task NativeIntegerWidening_TheTargetsConversionDecides() {
        // int to nint is C#'s implicit native conversion, stated as a cast; a target without it refuses the write.
        static Block Widening() {
            var x = new Variable("x");
            return new Block([new Assignment(x, new Constant((nint)0)), new Assignment(x, new Constant(1)), x], [x]);
        }

        await Assert.That(Analyze(Widening()).HasErrors).IsFalse();
        var underLedger = Analyze(Widening(), new LedgerHost());
        await Assert.That(underLedger.HasErrors).IsTrue().Because("the ledger has no conversion from a CLR int to a CLR nint");
        await Assert.That(underLedger.Diagnostics.Select(static d => d.Message)).Contains(static m => m.Contains("cannot assign 'Int32' to variable 'x'", StringComparison.Ordinal));
    }

    [Test]
    public async Task IndexerElementType_TheTargetsConversionDecides() {
        // this[long] indexed by an int: C# widens the index; a target without that conversion binds no indexer and
        // states no element type.
        static IndexAccess Index() => new(new New(Poly.Introspection.CommonLanguageRuntime.ClrTypeReferences.Of(typeof(LongIndexed))), new Constant(1));

        var index = Index();
        await Assert.That(Analyze(index).GetResolvedType(index)?.Name).IsEqualTo("String");

        index = Index();
        await Assert.That(Analyze(index, new LedgerHost()).GetResolvedType(index)).IsNull()
            .Because("no indexer applies under the ledger, and the ledger states no element type for a CLR type");
    }
}

/// <summary>A type whose only indexer takes a long (not a collection).</summary>
public sealed class LongIndexed {
    public string this[long index] => index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}