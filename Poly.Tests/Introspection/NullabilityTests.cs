using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// I5: nullability is a platform-stated relation, and since #998 T9 the platform is the type system: it names what a
/// nullable form wraps, so a conversion into it can be stated without the root knowing the host's nullability model.
/// </summary>
public class NullabilityTests {
    [Test]
    public async Task TypeSystem_StatesNullableForms_AndTheDefinitionDoesNot() {
        await Assert.That(typeof(ITypeSystem).GetMethod(nameof(ITypeSystem.NullableOf))).IsNotNull();
        var any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        await Assert.That(typeof(ITypeDefinition).GetProperty("NullableOf", any)).IsNull();
    }

    [Test]
    public async Task ClrNullable_StatesWhatItWraps_AndTheConversionAgrees() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nullableLong = (ITypeDefinition)registry.GetTypeDefinition(typeof(long?));
        var longType = (ITypeDefinition)registry.GetTypeDefinition(typeof(long));

        var csharp = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;
        await Assert.That(csharp.NullableOf(nullableLong)).IsSameReferenceAs(longType);
        await Assert.That(csharp.NullableOf(longType)).IsNull();

        // The stated relation agrees with the conversion: Int32 -> Int64? is implicit and the platform applies it.
        var conversion = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(registry.GetTypeDefinition(typeof(int)), nullableLong);

        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(conversion.Method).IsNull();
    }

    [Test]
    public async Task Nullability_IsThePlatformsRelationNotTheRoots() {
        // A host the root knows nothing about states its own nullable form, with no CLR type in sight.
        var underlying = new HostType("Text");
        var nullable = new HostType("Text?") { Wraps = underlying };

        await Assert.That(nullable.System.NullableOf(nullable)).IsSameReferenceAs(underlying);
        await Assert.That(underlying.System.NullableOf(underlying)).IsNull();
    }

    private sealed class HostType(string name) : ITypeDefinition {
        public ITypeDefinition? Wraps { get; init; }
        public string Name => name;
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

        public ITypeSystem System => HostSystem.Instance;

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    }

    /// <summary>The host's own system: a nullable form wraps what its definition says it wraps.</summary>
    private sealed class HostSystem : ITypeSystem {
        public static readonly HostSystem Instance = new();
        public ITypeSystem? Target => null;
        public IReadOnlyList<ITypeSource> Sources => [];
        public ITypeDefinition? Resolve(string qualifiedName) => null;
        public ITypeDefinition? Bind(string word) => null;
        public string? Spell(ITypeDefinition definition) => null;
        public bool IsSame(ITypeDefinition a, ITypeDefinition b) => ReferenceEquals(a, b);
        public ITypeDefinition? NullableOf(ITypeDefinition definition) => (definition as HostType)?.Wraps;
        public ITypeDefinition? ElementType(ITypeDefinition definition) => null;
        public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) => IsSame(destination, source);
        public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) => null;
        public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) => null;
        public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => null;
    }
}