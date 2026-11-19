using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// What a type is, answered by the definition itself (#998). Each row pins every answer for one type, so an answer
/// that drifts, or a sub-answer true without its parent, fails here. Every CLR ground type has a row, plus the shapes
/// around them (an enum, a nullable, a struct, an interface, a delegate, a class) and the modelled definitions.
/// </summary>
public class TypeAnswerTests {
    // The answers, as a string of the ones that are true, then the bit width: compact and readable in a failure.
    private static string Answers(ITypeDefinition t) {
        var flags = new (string Name, bool Value)[] {
            ("enum", t.IsEnumeration), ("value", t.HasValueSemantics), ("interface", t.IsInterface), ("delegate", t.IsDelegate),
            ("boolean", t.IsBoolean), ("text", t.IsText), ("character", t.IsCharacter),
            ("number", t.IsNumber), ("integral", t.IsIntegral), ("signed", t.IsSigned), ("decimal", t.IsDecimal),
            ("native", t.IsNativeSized),
            ("temporal", t.IsTemporal), ("date", t.HasDate), ("time", t.HasTime), ("offset", t.HasOffset),
            ("duration", t.IsDuration),
            ("guid", t.IsUniqueIdentifier), ("binary", t.IsBinary), ("top", t.IsTopType),
        };
        var set = string.Join(" ", flags.Where(static f => f.Value).Select(static f => f.Name));
        return t.NumberBits == 0 ? set : $"{set} bits={t.NumberBits}";
    }

    private static readonly string Native = $"bits={IntPtr.Size * 8}";

    public static IEnumerable<(Type Type, string Expected)> ClrRows() => [
        (typeof(bool), "value boolean"),
        (typeof(sbyte), "value number integral signed bits=8"),
        (typeof(short), "value number integral signed bits=16"),
        (typeof(int), "value number integral signed bits=32"),
        (typeof(long), "value number integral signed bits=64"),
        (typeof(byte), "value number integral bits=8"),
        (typeof(ushort), "value number integral bits=16"),
        (typeof(uint), "value number integral bits=32"),
        (typeof(ulong), "value number integral bits=64"),
        (typeof(float), "value number signed bits=32"),
        (typeof(double), "value number signed bits=64"),
        (typeof(decimal), "value number signed decimal bits=128"),
        (typeof(nint), $"value number integral signed native {Native}"),
        (typeof(nuint), $"value number integral native {Native}"),
        (typeof(string), "text"),
        (typeof(char), "value text character"),
        (typeof(DateOnly), "value temporal date"),
        (typeof(TimeOnly), "value temporal time"),
        (typeof(DateTime), "value temporal date time"),
        (typeof(DateTimeOffset), "value temporal date time offset"),
        (typeof(TimeSpan), "value temporal duration"),
        (typeof(Guid), "value guid"),
        (typeof(byte[]), "binary"),
        (typeof(object), "top"),
        // Not ground: the answers come from the shape alone. An enum and a nullable state no number of their own.
        (typeof(DayOfWeek), "enum value"),
        (typeof(int?), "value"),
        (typeof(KeyValuePair<int, int>), "value"),
        (typeof(IDisposable), "interface"),
        (typeof(Action), "delegate"),
        (typeof(Delegate), "delegate"),
        (typeof(Uri), ""),
        (typeof(int[]), ""),
    ];

    [Test]
    [MethodDataSource(nameof(ClrRows))]
    public async Task Clr_AnswersWhatItsTypeIs(Type type, string expected) {
        ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
        await Assert.That(Answers(definition)).IsEqualTo(expected);
    }

    [Test]
    public async Task Clr_EveryGroundType_HasARow() {
        var rows = ClrRows().Select(static row => row.Type).ToHashSet();
        foreach (var primitive in Enum.GetValues<ClrPrimitive>())
            await Assert.That(rows).Contains(primitive.GetClrType()!);
    }

    [Test]
    [Arguments(TypeDeclarationForm.Class, "")]
    [Arguments(TypeDeclarationForm.Struct, "value")]
    [Arguments(TypeDeclarationForm.Interface, "interface")]
    [Arguments(TypeDeclarationForm.Enum, "enum")]
    public async Task Declaration_AnswersWhatItsFormDeclares(TypeDeclarationForm form, string expected) {
        ITypeDefinition definition = new AstTypeDefinition(new TypeDefinitionNode("T", Form: form), new AstTypeRegistry());
        await Assert.That(Answers(definition)).IsEqualTo(expected);
    }

    [Test]
    public async Task SecondHost_AnswersForItsOwnTypes() {
        var host = new Poly.Tests.TestHelpers.LedgerHost();
        await Assert.That(Answers(host.Amount)).IsEqualTo("number signed decimal bits=96");
        await Assert.That(Answers(host.Account)).IsEqualTo("");
    }

    [Test]
    public async Task Clr_EveryPublicType_StatesNoSubAnswerWithoutItsParent() {
        var types = new[] { typeof(object).Assembly, typeof(Uri).Assembly, typeof(Enumerable).Assembly }
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => !type.ContainsGenericParameters)
            .Concat([typeof(byte[]), typeof(int?), typeof(nint), typeof(nuint)]);
        var broken = types.Select(static type => (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type))
            .Where(static t => !ObeysTheParentRule(t)).Select(static t => t.FullName).ToList();

        await Assert.That(broken).IsEmpty();
    }

    [Test]
    public async Task Askers_ReadNoSubAnswerWithoutItsParent() {
        // A definition that breaks the parent rule (a sub-answer true, its parent false) is not a number, a timestamp or
        // a character to any asker: each asks the parent first, as the old typed facts made it.
        var malformed = new Answering { IsIntegral = true, IsSigned = true, NumberBits = 64, HasDate = true, HasTime = true, IsCharacter = true };

        await Assert.That(ObeysTheParentRule(malformed)).IsFalse();
        await Assert.That(SyntaxTypeCompatibilityAnalyzer.IsPolicyShiftCount(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.SimpleTypeOf(malformed))).IsFalse();
        await Assert.That(SyntaxTypeCompatibilityAnalyzer.IsTimestamp(malformed)).IsFalse();
        await Assert.That(SyntaxTypeCompatibilityAnalyzer.IsNumericLike(malformed)).IsFalse();
    }

    private static bool ObeysTheParentRule(ITypeDefinition t) =>
        (t.IsNumber || !(t.IsIntegral || t.IsSigned || t.IsDecimal || t.IsNativeSized || t.NumberBits != 0))
        && (t.IsTemporal || !(t.HasDate || t.HasTime || t.HasOffset || t.IsDuration))
        && (t.IsText || !t.IsCharacter);

    private sealed class Answering : ITypeDefinition {
        public bool IsCharacter { get; init; }
        public bool IsIntegral { get; init; }
        public bool IsSigned { get; init; }
        public int NumberBits { get; init; }
        public bool HasDate { get; init; }
        public bool HasTime { get; init; }
        public string Name => "Malformed";
        public string? Namespace => null;
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
        public bool IsEnumeration => false;
        public bool HasValueSemantics => false;
        public bool IsInterface => false;
        public bool IsDelegate => false;
        public bool IsBoolean => false;
        public bool IsText => false;
        public bool IsNumber => false;
        public bool IsDecimal => false;
        public bool IsNativeSized => false;
        public bool IsTemporal => false;
        public bool HasOffset => false;
        public bool IsDuration => false;
        public bool IsUniqueIdentifier => false;
        public bool IsBinary => false;
        public bool IsTopType => false;
        bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        ITypeDefinition? ITypeDefinition.NullableOf => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }
}