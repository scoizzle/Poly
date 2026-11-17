using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using PrimitiveType = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.Introspection;

public class TypeDefinitionProviderCollectionTests {
    [Test]
    public async Task GetTypeDefinition_ReturnsFromFirstProvider() {
        var mockProvider1 = new MockTypeDefinitionProvider();
        var mockProvider2 = new MockTypeDefinitionProvider();
        var collection = new TypeDefinitionProviderCollection();
        collection.Add(mockProvider1);
        collection.Add(mockProvider2);

        // Mock provider1 returns a type for "TestType"
        mockProvider1.AddType("TestType", new MockTypeDefinition("TestType"));
        var result = collection.GetTypeDefinition("TestType");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("TestType");
    }

    [Test]
    public async Task GetTypeDefinition_ReturnsNullIfNoProviderHasType() {
        var mockProvider1 = new MockTypeDefinitionProvider();
        var mockProvider2 = new MockTypeDefinitionProvider();
        var collection = new TypeDefinitionProviderCollection();
        collection.Add(mockProvider1);
        collection.Add(mockProvider2);

        var result = collection.GetTypeDefinition("NonExistentType");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AddProvider_AddsProviderToCollection() {
        var collection = new TypeDefinitionProviderCollection();
        var provider = new MockTypeDefinitionProvider();

        collection.Add(provider);

        provider.AddType("AddedType", new MockTypeDefinition("AddedType"));
        var result = collection.GetTypeDefinition("AddedType");
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetTypeDefinition_ReturnsFromFirstProviderWhenMultipleHaveSameType() {
        var mockProvider1 = new MockTypeDefinitionProvider();
        var mockProvider2 = new MockTypeDefinitionProvider();
        var collection = new TypeDefinitionProviderCollection();
        collection.Add(mockProvider1);
        collection.Add(mockProvider2);

        // Both providers have "SharedType", but with different instances
        var type1 = new MockTypeDefinition("SharedType") { Tag = "Provider1" };
        var type2 = new MockTypeDefinition("SharedType") { Tag = "Provider2" };
        mockProvider1.AddType("SharedType", type1);
        mockProvider2.AddType("SharedType", type2);

        var result = collection.GetTypeDefinition("SharedType");

        await Assert.That(result).IsNotNull();
        // The collection uses LIFO (stack) order, so the last added provider (mockProvider2) is checked first
        await Assert.That(((MockTypeDefinition)result!).Tag).IsEqualTo("Provider2");
    }

    [Test]
    public async Task FindMatchingMethodOverloads_SingleOverload_ReturnsMethod() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var methods = stringType.FindMatchingMethodOverloads("ToString", []);

        await Assert.That(methods).HasSingleItem();
        await Assert.That(methods.Single().Name).IsEqualTo("ToString");
    }

    [Test]
    public async Task FindMatchingMethodOverloads_MultipleOverloads_ReturnsBestMatch() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();
        var charType = registry.GetTypeDefinition<char>();

        var methods = stringType.FindMatchingMethodOverloads("IndexOf", [charType]);

        await Assert.That(methods).HasSingleItem();
        await Assert.That(methods.Single().Name).IsEqualTo("IndexOf");
    }

    [Test]
    public async Task FindMatchingMethodOverloads_NoMatch_ReturnsEmpty() {
        var registry = new ClrTypeDefinitionRegistry();
        var stringType = registry.GetTypeDefinition<string>();

        var methods = stringType.FindMatchingMethodOverloads("NonExistentMethod", []);

        await Assert.That(methods).IsEmpty();
    }

    [Test]
    public async Task PrimitiveTypeIdAndKind_AreExposedForClrTypes() {
        var registry = new ClrTypeDefinitionRegistry();

        // Test primitive types
        ITypeDefinition intType = registry.GetTypeDefinition(typeof(int));
        ITypeDefinition stringType = registry.GetTypeDefinition(typeof(string));
        ITypeDefinition dateTimeType = registry.GetTypeDefinition(typeof(DateTime));
        ITypeDefinition listType = registry.GetTypeDefinition(typeof(List<int>));

        // int is primitive
        await Assert.That(intType.PrimitiveType).IsEqualTo(PrimitiveType.Int32);
        await Assert.That(intType.Kind).IsEqualTo(TypeKind.Primitive);

        // string is primitive
        await Assert.That(stringType.PrimitiveType).IsEqualTo(PrimitiveType.String);
        await Assert.That(stringType.Kind).IsEqualTo(TypeKind.Primitive);

        // DateTime is primitive, not a DateTime-shaped category bit
        await Assert.That(dateTimeType.PrimitiveType).IsEqualTo(PrimitiveType.DateTime);
        await Assert.That(dateTimeType.Kind).IsEqualTo(TypeKind.Primitive);

        // List<int> is a class; its element type is structural, not a flag
        await Assert.That(listType.PrimitiveType).IsNull();
        await Assert.That(listType.Kind).IsEqualTo(TypeKind.Class);
        await Assert.That(listType.GetElementType()!.GetRuntimeTypeOrThrow()).IsEqualTo(typeof(int));
    }

    // Mock implementations for testing
    private class MockTypeDefinition(string name) : ITypeDefinition {
        public string Name { get; } = name;
        public string? Namespace => null;
        public AccessModifier AccessModifier => AccessModifier.Public;
        public IEnumerable<ITypeMember> Members => [];
        public ITypeDefinition? BaseType => null;
        public IEnumerable<ITypeDefinition> Interfaces => [];
        public IEnumerable<IParameter> GenericParameters => [];
        public PrimitiveType? PrimitiveType => null;
        public PrimitiveAtom? PlatformAtom => null;
        public ITypeDefinition? NullableOf => null;
        public TypeKind Kind => TypeKind.Class;

        public IEnumerable<ITypeMember> GetMembers(string name) => Enumerable.Empty<ITypeMember>();
        public bool IsAssignableTo(ITypeDefinition targetType) => false;

        public bool TryGetMethod(string name, IEnumerable<Type> parameterTypes, out ITypeMethod? method) {
            method = null;
            return false;
        }

        public string? Tag { get; set; }
        public IEnumerable<ITypeField> Fields => [];
        public IEnumerable<ITypeProperty> Properties => [];
        public IEnumerable<ITypeMethod> Methods => [];
        public IEnumerable<ITypeConstructor> Constructors => [];
        public IEnumerable<ITypeOperator> Operators => [];
        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }

    [Test]
    public async Task NeutralProviderContract_ExposesNoClrHandle() {
        // A platform with no CLR types must not be asked to implement a member it cannot honour: the Type-based
        // resolution is the CLR provider's, stated on its own interface.
        var neutral = typeof(ITypeDefinitionProvider).GetMethods()
            .Select(static method => $"{method.Name}({string.Join(",", method.GetParameters().Select(static p => p.ParameterType.Name))})")
            .ToArray();
        await Assert.That(neutral).DoesNotContain("GetTypeDefinition(Type)");
        await Assert.That(typeof(IClrTypeDefinitionProvider).GetMethods()
            .Any(static method => method.Name == "GetTypeDefinition"
                && method.GetParameters() is [{ ParameterType: var parameterType }]
                && parameterType == typeof(Type))).IsTrue();
    }

    [Test]
    public async Task GetTypeDefinition_Primitive_DoesNotAssumeTheProviderHasClrTypes() {
        // Primitive resolution is the provider's: a platform without CLR types must not inherit the CLR's answer,
        // and must not be asked for one through a runtime Type it never mentioned.
        ITypeDefinitionProvider provider = new NoClrTypeProvider();
        await Assert.That(provider.GetTypeDefinition(PrimitiveType.Decimal)).IsNull();
    }

    [Test]
    public async Task ComposedProvider_EnumeratesWhatItsPartsHold() {
        // I2: one enumeration over the stack, each definition once by identity.
        var modeled = new MockTypeDefinitionProvider();
        var money = new MockTypeDefinition("Money");
        modeled.AddType("Money", money);
        var registry = new ClrTypeDefinitionRegistry();
        var collection = new TypeDefinitionProviderCollection(modeled, registry);
        collection.AddFallback(modeled);

        var before = collection.Types.ToArray();

        await Assert.That(before).Contains(money);
        await Assert.That(before.Count(candidate => ReferenceEquals(candidate, money))).IsEqualTo(1);
        // A fresh registry holds nothing until it is asked.
        await Assert.That(before.Any(candidate => candidate.GetRuntimeType() == typeof(int))).IsFalse();

        collection.GetTypeDefinition(typeof(int));

        await Assert.That(collection.Types.Any(candidate => candidate.GetRuntimeType() == typeof(int))).IsTrue();
    }

    [Test]
    public async Task NeutralProviderContract_ExposesTheTypesItHolds() {
        // I2: a consumer can enumerate what a provider holds, so a derived question ("what converts to X", "what
        // implements Y") stops needing a caller-supplied universe.
        await Assert.That(typeof(ITypeDefinitionProvider).GetProperty("Types")).IsNotNull();
    }

    private sealed class NoClrTypeProvider : ITypeDefinitionProvider {
        public ITypeDefinition? GetTypeDefinition(string name) => null;

        public ITypeDefinition? GetTypeDefinition(Type type) =>
            throw new NotSupportedException("This platform has no CLR types.");

        public ITypeDefinition? GetTypeDefinition(PrimitiveType primitiveTypeId) => null;
        public IEnumerable<ITypeDefinition> Types => [];
    }

    private class MockTypeDefinitionProvider : ITypeDefinitionProvider {
        private readonly Dictionary<string, ITypeDefinition> _types = [];

        public void AddType(string name, ITypeDefinition type) {
            _types[name] = type;
        }

        public ITypeDefinition? GetTypeDefinition(string name) {
            return _types.TryGetValue(name, out var type) ? type : null;
        }

        public ITypeDefinition? GetTypeDefinition(Type type) {
            throw new NotImplementedException();
        }

        public ITypeDefinition? GetTypeDefinition(PrimitiveType primitiveTypeId) => null;
        public IEnumerable<ITypeDefinition> Types => _types.Values;
    }
}