using Poly.Ast.Nodes;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Runtime;

namespace Poly.Tests.Introspection;

/// <summary>
/// A type reference the CLR host mints carries the platform's definition, not a spelling of its name: two types that
/// share a full name are still different types, and an implementer is assignable to the interface it implements. The
/// string form is what a reference prints, never what it means.
/// </summary>
public class ClrReferenceIdentityTests {
    [Test]
    public async Task Of_PlainType_CarriesTheProvidersDefinition() {
        var reference = ClrTypeReferences.Of(typeof(string));

        await Assert.That(reference).IsTypeOf<TypeDefinitionReference>();
        var stated = ((TypeDefinitionReference)reference).TypeDefinition;
        await Assert.That(stated.IsSameTypeAs(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string))))
            .IsTrue().Because("a minted reference states the provider's definition, not a name to resolve again");
    }

    [Test]
    public async Task Of_GenericType_CarriesTheClosedDefinition() {
        var reference = ClrTypeReferences.Of(typeof(Func<int, string>));

        await Assert.That(reference).IsTypeOf<TypeDefinitionReference>();
        var stated = ((TypeDefinitionReference)reference).TypeDefinition;
        await Assert.That(stated.TryGetRuntimeType(out var runtime) && runtime == typeof(Func<int, string>))
            .IsTrue();
    }

    [Test]
    public async Task Of_NullableType_KeepsTheOptionalShapeOverItsUnderlyingDefinition() {
        var reference = ClrTypeReferences.Of(typeof(int?));

        await Assert.That(reference).IsTypeOf<OptionalTypeReference>();
        await Assert.That(((OptionalTypeReference)reference).InnerType).IsTypeOf<TypeDefinitionReference>();
    }

    [Test]
    public async Task Assignability_AsksTheDefinition_NotTheName() {
        var port = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(IStoragePort));

        ITypeDefinition sameName = new ForeignDefinition("Poly.Runtime", "IStoragePort");
        await Assert.That(TypeDefinitionExtensions.IsAssignableByHierarchy(port, sameName)).IsFalse()
            .Because("a definition from another platform with the same full name is not the port");

        ITypeDefinition implementer = new ForeignDefinition("App", "AppDbContext", [port]);
        await Assert.That(TypeDefinitionExtensions.IsAssignableByHierarchy(port, implementer)).IsTrue()
            .Because("an implementer of the port is the port's shape, which a name comparison cannot see");
    }

    [Test]
    public async Task Denotes_AnImplementerOfThePort_IsTrue() {
        var implementer = ClrTypeReferences.Of(typeof(Poly.DomainModeling.Runtime.ModuleStoragePort));
        await Assert.That(ClrTypeReferences.Denotes(implementer, typeof(IStoragePort))).IsTrue()
            .Because("a parameter typed as an implementer of the port takes the port");
    }

    [Test]
    public async Task Denotes_ASpelledName_IsFalse() {
        var spelled = new NamedTypeReference("IStoragePort", "Poly.Runtime");
        await Assert.That(ClrTypeReferences.Denotes(spelled, typeof(IStoragePort))).IsFalse()
            .Because("a name is not a type: only a stated definition denotes one");
        await Assert.That(ClrTypeReferences.Denotes(ClrTypeReferences.To<string>(), typeof(IStoragePort))).IsFalse();
        await Assert.That(ClrTypeReferences.Denotes(ClrTypeReferences.To<IStoragePort>(), typeof(IStoragePort))).IsTrue();
    }

    /// <summary>A definition from another platform: identity is its own reference, so a same-named definition from
    /// elsewhere is a different type.</summary>
    private sealed class ForeignDefinition(
        string ns, string name, IReadOnlyList<ITypeDefinition>? interfaces = null) : ITypeDefinition {
        public string Name => name;
        public string? Namespace => ns;
        public AccessModifier AccessModifier => AccessModifier.Public;
        public ITypeDefinition? BaseType => null;
        public IEnumerable<ITypeDefinition> Interfaces => interfaces ?? [];
        public IEnumerable<IParameter> GenericParameters => [];
        public IEnumerable<ITypeMember> Members => [];
        public IEnumerable<ITypeField> Fields => [];
        public IEnumerable<ITypeProperty> Properties => [];
        public IEnumerable<ITypeMethod> Methods => [];
        public IEnumerable<ITypeConstructor> Constructors => [];
        public IEnumerable<ITypeOperator> Operators => [];
        public ITypeDefinition? NullableOf => null;
        public bool IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        bool ITypeDefinition.IsEnumeration => false;
        bool ITypeDefinition.HasValueSemantics => false;
        bool ITypeDefinition.IsInterface => false;
        bool ITypeDefinition.IsDelegate => false;
        bool ITypeDefinition.IsBoolean => false;
        bool ITypeDefinition.IsText => false;
        bool ITypeDefinition.IsCharacter => false;
        bool ITypeDefinition.IsNumber => false;
        bool ITypeDefinition.IsIntegral => false;
        bool ITypeDefinition.IsSigned => false;
        bool ITypeDefinition.IsDecimal => false;
        bool ITypeDefinition.IsNativeSized => false;
        int ITypeDefinition.NumberBits => 0;
        bool ITypeDefinition.IsTemporal => false;
        bool ITypeDefinition.HasDate => false;
        bool ITypeDefinition.HasTime => false;
        bool ITypeDefinition.HasOffset => false;
        bool ITypeDefinition.IsDuration => false;
        bool ITypeDefinition.IsUniqueIdentifier => false;
        bool ITypeDefinition.IsBinary => false;
        bool ITypeDefinition.IsTopType => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }
}