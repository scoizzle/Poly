using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// I6: the shared primitive vocabulary is not a CLR list. A second host can name a primitive the CLR has no type for,
/// and its provider resolves it like any other declaration.
/// </summary>
public class PrimitiveVocabularyTests {
    [Test]
    public async Task Vocabulary_NamesThePrimitivesASecondHostNeeds() {
        // ECMAScript's language types the CLR has no type for: Undefined, Null, Symbol, BigInt.
        var names = Enum.GetNames<PrimitiveType>();
        foreach (var name in new[] { "Undefined", "Null", "Symbol", "BigInt" })
            await Assert.That(names).Contains(name);
    }

    [Test]
    public async Task SecondHost_StatesAndResolvesItsOwnPrimitives() {
        // The vocabulary names what a second host has; its provider resolves and enumerates it, and the CLR does not
        // pretend to have a type for it.
        var provider = new SecondHostProvider();
        provider.Add(new HostPrimitive("Undefined", PrimitiveType.Undefined), PrimitiveType.Undefined);

        var resolved = provider.GetTypeDefinition(PrimitiveType.Undefined);

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Name).IsEqualTo("Undefined");
        await Assert.That(provider.Types).Contains(resolved);
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(PrimitiveType.Undefined)).IsNull();
        await Assert.That(ClrTypeDefinitionRegistry.Shared.Types.Any(static type => type.PrimitiveType == PrimitiveType.Undefined)).IsFalse();
    }

    private sealed class SecondHostProvider : ITypeDefinitionProvider {
        private readonly Dictionary<string, ITypeDefinition> _types = new(StringComparer.Ordinal);
        private readonly Dictionary<PrimitiveType, ITypeDefinition> _primitives = new();

        public void Add(ITypeDefinition definition, PrimitiveType? primitive) {
            _types[definition.Name] = definition;
            if (primitive is { } id)
                _primitives[id] = definition;
        }

        public ITypeDefinition? GetTypeDefinition(string name) => _types.GetValueOrDefault(name);
        public ITypeDefinition? GetTypeDefinition(PrimitiveType primitiveTypeId) => _primitives.GetValueOrDefault(primitiveTypeId);
        public IEnumerable<ITypeDefinition> Types => _types.Values;
    }

    private sealed class HostPrimitive(string name, PrimitiveType? primitive) : ITypeDefinition {
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
        public PrimitiveType? PrimitiveType => primitive;
        public TypeKind Kind => TypeKind.Primitive;

        public bool IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }
}