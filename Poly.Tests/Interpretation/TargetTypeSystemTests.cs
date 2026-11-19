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

    [Test]
    public async Task VmLambdaBridge_AsksTheAnalysisTarget() {
        // A literal lambda quoted for an Expression<Func<int, int, object>> parameter: the VM's lambda bridge states its
        // body's operator, and the analysis result names the target that decides it (#998 T4c).
        var x = new Parameter("x", Poly.Introspection.CommonLanguageRuntime.ClrTypeReferences.Of(typeof(int)));
        var y = new Parameter("y", Poly.Introspection.CommonLanguageRuntime.ClrTypeReferences.Of(typeof(int)));
        var call = new Invoke(
            new Member(Poly.Introspection.CommonLanguageRuntime.ClrTypeReferences.Of(typeof(BridgeProbe<int, int, object>)), "Apply"),
            new Lambda([x, y], new Add(x, y)), new Constant(1), new Constant(2));
        var analysis = Poly.Interpretation.Interpreter.Analyze(call, Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared);
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.TypeSystem).IsSameReferenceAs(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

        await Assert.That(() => Poly.Interpretation.Interpreter.Compile(call, analysis)).ThrowsNothing();

        var underLedger = analysis with { Settings = analysis.Settings.With<ITypeSystem>(new LedgerHost()) };
        await Assert.That(underLedger.TypeSystem).IsTypeOf<LedgerHost>();
        await Assert.That(() => Poly.Interpretation.Interpreter.Compile(call, underLedger)).Throws<InvalidOperationException>()
            .WithMessageContaining("defines no Add operator");
    }

    [Test]
    public async Task ConstantFolding_ReducesOnlyACSharpUnsignedOperand() {
        // x % 8 -> x & 7 is sound only for C#'s unsigned simple types; a foreign type answering "unsigned 32-bit"
        // about itself is not one (#998 T6a).
        static (Lambda Lambda, Modulo Modulo) Program(Node type) {
            var x = new Parameter("x", type);
            var modulo = new Modulo(x, new Constant(8u));
            return (new Lambda([x], modulo), modulo);
        }

        var (clrLambda, clrModulo) = Program(Poly.Introspection.CommonLanguageRuntime.ClrTypeReferences.Of(typeof(uint)));
        await Assert.That(Analyze(clrLambda).GetNodeReplacement(clrModulo)).IsTypeOf<BitwiseAnd>();

        var (foreignLambda, foreignModulo) = Program(new TypeDefinitionReference(new ForeignUnsignedWord()));
        await Assert.That(Analyze(foreignLambda).GetNodeReplacement(foreignModulo) is BitwiseAnd).IsFalse()
            .Because("only C#'s own unsigned simple types are strength-reduced");
    }

    [Test]
    public async Task CSharpRules_AnswerOnlyForCSharpsOwnDefinitions() {
        var rules = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Rule<Poly.Interpretation.CSharp.ICSharpTypeRules>();
        var clr = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;

        await Assert.That(rules).IsNotNull();
        await Assert.That(rules!.SimpleTypeOf(clr.GetTypeDefinition(typeof(uint)))).IsEqualTo(Poly.Interpretation.CSharp.SimpleType.UInt);
        await Assert.That(rules.SimpleTypeOf(clr.GetTypeDefinition(typeof(string)))).IsNull().Because("string is not a C# simple type");
        await Assert.That(rules.SimpleTypeOf(new ForeignUnsignedWord())).IsNull();
        await Assert.That(rules.SimpleTypeOf(new LedgerHost().Amount)).IsNull();
        await Assert.That(rules.IsEnumType(clr.GetTypeDefinition(typeof(DayOfWeek)))).IsTrue();
        await Assert.That(rules.HasValueSemantics(clr.GetTypeDefinition(typeof(DayOfWeek)))).IsTrue();
        await Assert.That(rules.HasValueSemantics(clr.GetTypeDefinition(typeof(string)))).IsFalse();
        await Assert.That(((ITypeSystem)new LedgerHost()).Rule<Poly.Interpretation.CSharp.ICSharpTypeRules>()).IsNull();
    }

    [Test]
    public async Task DateOffset_AsksTheTargetsCSharpRule_AndFailsClosedForAForeignNumber() {
        // A date plus a number of days is an offset only when the target's C# rule knows the number (#998 T6a). A
        // foreign definition that answers "decimal number" is no C# number; a target with no C# rule decides none.
        static ITypeDefinition Clr(Type type) => Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
        var csharp = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;
        var date = Clr(typeof(DateTime));

        await Assert.That(DateOffsetRule.Applies(csharp, OperatorKind.Add, date, Clr(typeof(int)))).IsTrue();
        await Assert.That(DateOffsetRule.Applies(csharp, OperatorKind.Subtract, date, Clr(typeof(nint)))).IsTrue();
        await Assert.That(DateOffsetRule.Applies(csharp, OperatorKind.Add, date, Clr(typeof(decimal?)))).IsTrue()
            .Because("only a non-nullable decimal beside a non-nullable date is left to the provider");
        await Assert.That(DateOffsetRule.Applies(csharp, OperatorKind.Add, Clr(typeof(DateTime?)), Clr(typeof(decimal)))).IsTrue();
        await Assert.That(DateOffsetRule.Applies(csharp, OperatorKind.Add, date, Clr(typeof(decimal)))).IsFalse();

        await Assert.That(DateOffsetRule.Applies(csharp, OperatorKind.Add, date, new LedgerHost().Amount)).IsFalse()
            .Because("the ledger's amount is no C# number, whatever it answers about itself");
        await Assert.That(DateOffsetRule.Applies(new LedgerHost(), OperatorKind.Add, date, Clr(typeof(decimal)))).IsFalse()
            .Because("a target with no C# rule decides no offset");
    }

    [Test]
    public async Task CompatibilityCategory_IsCSharpsRule_NotWhatAForeignTypeAnswers() {
        // The ledger's amount answers that it is a decimal number; it is no C# type, so the C# category table files it
        // under nothing (#998 T6b).
        var csharp = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;
        var clr = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;

        await Assert.That(SyntaxTypeCompatibilityAnalyzer.CategoryOf(csharp, new LedgerHost().Amount)).IsEqualTo(SyntaxTypeCompatibilityAnalyzer.Cat.Unknown);
        await Assert.That(SyntaxTypeCompatibilityAnalyzer.CategoryOf(csharp, new ForeignUnsignedWord())).IsEqualTo(SyntaxTypeCompatibilityAnalyzer.Cat.Unknown);
        await Assert.That(SyntaxTypeCompatibilityAnalyzer.CategoryOf(csharp, clr.GetTypeDefinition(typeof(decimal)))).IsEqualTo(SyntaxTypeCompatibilityAnalyzer.Cat.Number);
        await Assert.That(SyntaxTypeCompatibilityAnalyzer.CategoryOf(new LedgerHost(), clr.GetTypeDefinition(typeof(decimal)))).IsEqualTo(SyntaxTypeCompatibilityAnalyzer.Cat.Unknown)
            .Because("a target that is not C# answers no C# category");
    }

    [Test]
    public async Task ElementType_PositionalIndexer_IsCSharpsRule_NotWhatAForeignIndexAnswers() {
        // C#'s element type of a collection is its positional indexer's: an Item whose parameters are C#'s fixed-width
        // integers (#998 T6c). An index typed by a foreign word that answers "integral" is no C# integer, so the pick
        // falls to the sequence element (none here).
        var clr = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;
        var csharp = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;
        var text = clr.GetTypeDefinition(typeof(string));

        await Assert.That(csharp.ElementType(new ForeignIndexed(clr.GetTypeDefinition(typeof(int)), text))).IsSameReferenceAs(text);
        await Assert.That(csharp.ElementType(new ForeignIndexed(new ForeignUnsignedWord(), text))).IsNull()
            .Because("the foreign word answers that it is an integer, but it is none of C#'s");
        await Assert.That(csharp.ElementType(clr.GetTypeDefinition(typeof(List<int>)))?.Name).IsEqualTo("Int32");
        await Assert.That(csharp.ElementType(clr.GetTypeDefinition(typeof(int[])))?.Name).IsEqualTo("Int32");
    }
}

/// <summary>A foreign definition that answers, on the old classification members, that it is an unsigned 32-bit
/// integer. It is no C# simple type, so C#'s rules must not treat it as one.</summary>
public sealed class ForeignUnsignedWord : ITypeDefinition {
    public string Name => "word";
    public string? Namespace => "foreign";
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => [];
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => [];
    public IEnumerable<ITypeMethod> Methods => [];
    public IEnumerable<ITypeConstructor> Constructors => [];
    public IEnumerable<ITypeOperator> Operators => [];
    bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
    IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    ITypeDefinition? ITypeDefinition.NullableOf => null;
    ITypeDefinition? ITypeDefinition.SequenceElementType => null;
    bool ITypeDefinition.IsCollectionShape => false;
    ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
    ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
}

/// <summary>A type whose only indexer takes a long (not a collection).</summary>
public sealed class LongIndexed {
    public string this[long index] => index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A foreign collection with one positional indexer, <c>Item[index]</c>, of the given types (#998 T6c).</summary>
public sealed class ForeignIndexed(ITypeDefinition index, ITypeDefinition element) : ITypeDefinition {
    public string Name => "indexed";
    public string? Namespace => "foreign";
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => Properties;
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => [new Indexer(this, index, element)];
    public IEnumerable<ITypeMethod> Methods => [];
    public IEnumerable<ITypeConstructor> Constructors => [];
    public IEnumerable<ITypeOperator> Operators => [];
    bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
    IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    ITypeDefinition? ITypeDefinition.NullableOf => null;
    ITypeDefinition? ITypeDefinition.SequenceElementType => null;
    bool ITypeDefinition.IsCollectionShape => true;
    ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
    ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;

    private sealed class Indexer(ITypeDefinition declaring, ITypeDefinition index, ITypeDefinition element) : ITypeProperty {
        public MemberKind Kind => MemberKind.Indexer;
        public string Name => "Item";
        public ITypeDefinition MemberTypeDefinition => element;
        public ITypeDefinition DeclaringTypeDefinition => declaring;
        public IEnumerable<IParameter> Parameters => [new Index(index)];
        public AccessModifier AccessModifier => AccessModifier.Public;
        public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
        public Mutability Mutability => Mutability.Mutable;
    }

    private sealed class Index(ITypeDefinition type) : IParameter {
        public int Position => 0;
        public string Name => "index";
        public ITypeDefinition ParameterTypeDefinition => type;
        public bool IsOptional => false;
        public object? DefaultValue => null;
    }
}