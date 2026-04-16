using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrConstructorTests {
    [Test]
    public async Task TypeWithConstructors_ExposesConstructorCollection() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var type = registry.GetTypeDefinition<ConstructorTarget>();

        var constructors = type.Constructors.ToList();

        await Assert.That(constructors.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(constructors.All(ctor => ctor.MemberTypeDefinition == type)).IsTrue();
    }

    [Test]
    public async Task PublicParameterlessConstructor_HasEmptyParameters() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var type = registry.GetTypeDefinition<ConstructorTarget>();

        var constructor = type.Constructors.Single(ctor => !ctor.IsStatic && !ctor.Parameters.Any());

        await Assert.That(constructor.Name).IsEqualTo(type.Name);
        await Assert.That(constructor.Parameters).IsEmpty();
    }

    [Test]
    public async Task ConstructorWithOptionalArgument_HasOrderedParameters() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var type = registry.GetTypeDefinition<ConstructorTarget>();

        var constructor = type.Constructors.Single(ctor =>
            !ctor.IsStatic &&
            ctor.Parameters.Count() == 2 &&
            ((ITypeDefinition)ctor.Parameters.First().ParameterTypeDefinition).GetRuntimeType() == typeof(string));

        var parameters = constructor.Parameters.ToArray();

        await Assert.That(parameters[0].Name).IsEqualTo("name");
        await Assert.That(parameters[0].Position).IsEqualTo(0);
        await Assert.That(parameters[0].IsOptional).IsFalse();
        await Assert.That(parameters[1].Name).IsEqualTo("count");
        await Assert.That(parameters[1].Position).IsEqualTo(1);
        await Assert.That(parameters[1].IsOptional).IsTrue();
        await Assert.That(parameters[1].DefaultValue).IsEqualTo(0);
    }

    [Test]
    public async Task FindMatchingConstructors_UsesParameterCompatibilityRules() {
        var registry = new ClrTypeDefinitionRegistry();
        var type = registry.GetTypeDefinition<ConstructorTarget>();
        var stringType = registry.GetTypeDefinition<string>();

        var constructors = type.FindMatchingConstructors([stringType]).ToList();

        await Assert.That(constructors).HasSingleItem();
        await Assert.That(constructors[0].Parameters.Count()).IsEqualTo(2);
        await Assert.That(constructors[0].Parameters.Last().IsOptional).IsTrue();
    }

    [Test]
    public async Task StaticConstructor_IsExposedSeparatelyFromMethods() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var type = registry.GetTypeDefinition<ConstructorTarget>();

        var staticConstructor = type.Constructors.Single(ctor => ctor.IsStatic);

        await Assert.That(staticConstructor.Parameters).IsEmpty();
        await Assert.That(type.Methods.Any(method => method.Name == type.Name)).IsFalse();
    }

    private sealed class ConstructorTarget {
        static ConstructorTarget() {
        }

        public ConstructorTarget() {
        }

        public ConstructorTarget(string name, int count = 0) {
            Name = name;
            Count = count;
        }

        private ConstructorTarget(Guid id) {
            Identifier = id;
        }

        public string? Name { get; }

        public int Count { get; }

        public Guid Identifier { get; }
    }
}