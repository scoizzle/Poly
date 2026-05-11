using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrModifierTests {
    private readonly ClrTypeDefinitionRegistry _registry = ClrTypeDefinitionRegistry.Shared;

    [Test]
    public async Task NestedTypes_ReportAccessModifiers() {
        await Assert.That(_registry.GetTypeDefinition<PublicNested>().AccessModifier).IsEqualTo(AccessModifier.Public);
        await Assert.That(_registry.GetTypeDefinition<InternalNested>().AccessModifier).IsEqualTo(AccessModifier.Internal);
        await Assert.That(_registry.GetTypeDefinition<ProtectedNested>().AccessModifier).IsEqualTo(AccessModifier.Protected);
        await Assert.That(_registry.GetTypeDefinition<PrivateNested>().AccessModifier).IsEqualTo(AccessModifier.Private);
    }

    [Test]
    public async Task Members_ReportAccessAndLifetimeModifiers() {
        var type = _registry.GetTypeDefinition<MemberModifierTarget>();

        var publicField = type.Fields.WithName(nameof(MemberModifierTarget.PublicField)).Single();
        var privateStaticField = type.Fields.WithName("PrivateStaticField").Single();
        var publicProperty = type.Properties.WithName(nameof(MemberModifierTarget.PublicProperty)).Single();
        var privateStaticProperty = type.Properties.WithName("PrivateStaticProperty").Single();
        var protectedMethod = type.Methods.WithName("ProtectedMethod").Single(method => !method.Parameters.Any());
        var internalStaticMethod = type.Methods.WithName(nameof(MemberModifierTarget.InternalStaticMethod)).Single(method => !method.Parameters.Any());

        await Assert.That(publicField.AccessModifier).IsEqualTo(AccessModifier.Public);
        await Assert.That(publicField.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);
        await Assert.That(publicField.IsStatic).IsFalse();

        await Assert.That(privateStaticField.AccessModifier).IsEqualTo(AccessModifier.Private);
        await Assert.That(privateStaticField.LifetimeModifier).IsEqualTo(LifetimeModifier.Static);
        await Assert.That(privateStaticField.IsStatic).IsTrue();

        await Assert.That(publicProperty.AccessModifier).IsEqualTo(AccessModifier.Public);
        await Assert.That(publicProperty.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);

        await Assert.That(privateStaticProperty.AccessModifier).IsEqualTo(AccessModifier.Private);
        await Assert.That(privateStaticProperty.LifetimeModifier).IsEqualTo(LifetimeModifier.Static);

        await Assert.That(protectedMethod.AccessModifier).IsEqualTo(AccessModifier.Protected);
        await Assert.That(protectedMethod.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);

        await Assert.That(internalStaticMethod.AccessModifier).IsEqualTo(AccessModifier.Internal);
        await Assert.That(internalStaticMethod.LifetimeModifier).IsEqualTo(LifetimeModifier.Static);
        await Assert.That(internalStaticMethod.IsStatic).IsTrue();
    }

    [Test]
    public async Task Constructors_ReportAccessAndLifetimeModifiers() {
        var type = _registry.GetTypeDefinition<ConstructorModifierTarget>();

        var publicConstructor = type.Constructors.Single(ctor => !ctor.IsStatic && !ctor.Parameters.Any());
        var internalConstructor = type.Constructors.Single(ctor => ctor.Parameters.SingleOrDefault()?.ParameterTypeDefinition.Name == nameof(Int32));
        var protectedConstructor = type.Constructors.Single(ctor => ctor.Parameters.SingleOrDefault()?.ParameterTypeDefinition.Name == nameof(String));
        var privateConstructor = type.Constructors.Single(ctor => ctor.Parameters.SingleOrDefault()?.ParameterTypeDefinition.Name == nameof(Guid));
        var staticConstructor = type.Constructors.Single(ctor => ctor.IsStatic);

        await Assert.That(publicConstructor.AccessModifier).IsEqualTo(AccessModifier.Public);
        await Assert.That(publicConstructor.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);

        await Assert.That(internalConstructor.AccessModifier).IsEqualTo(AccessModifier.Internal);
        await Assert.That(internalConstructor.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);

        await Assert.That(protectedConstructor.AccessModifier).IsEqualTo(AccessModifier.Protected);
        await Assert.That(protectedConstructor.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);

        await Assert.That(privateConstructor.AccessModifier).IsEqualTo(AccessModifier.Private);
        await Assert.That(privateConstructor.LifetimeModifier).IsEqualTo(LifetimeModifier.Instance);

        await Assert.That(staticConstructor.AccessModifier).IsEqualTo(AccessModifier.Private);
        await Assert.That(staticConstructor.LifetimeModifier).IsEqualTo(LifetimeModifier.Static);
    }

    public class PublicNested;
    internal class InternalNested;
    protected class ProtectedNested;
    private class PrivateNested;

    private class MemberModifierTarget {
        public int PublicField = 42;
        private static readonly string PrivateStaticField = string.Empty;

        public string PublicProperty { get; private set; } = string.Empty;

        private static string PrivateStaticProperty => string.Empty;

        protected void ProtectedMethod() {
        }

        internal static void InternalStaticMethod() {
        }
    }

    private class ConstructorModifierTarget {
        static ConstructorModifierTarget() {
        }

        public ConstructorModifierTarget() {
        }

        internal ConstructorModifierTarget(int value) {
        }

        protected ConstructorModifierTarget(string value) {
        }

        private ConstructorModifierTarget(Guid value) {
        }
    }
}