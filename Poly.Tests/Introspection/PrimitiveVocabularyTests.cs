using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// There is no shared list of primitive types (#998 S5). A host names its own ground types and states what each is as
/// a fact; its provider resolves them by name like any other declaration. (Formerly: the shared <c>PrimitiveType</c>
/// vocabulary, which listed ECMAScript's types so a second host could "have" them.)
/// </summary>
public class PrimitiveVocabularyTests {
    [Test]
    public async Task Contract_NamesNoGroundType() {
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var introspection = typeof(ITypeDefinition).Assembly.GetTypes()
            .Where(static type => type.Namespace == typeof(ITypeDefinition).Namespace)
            .Select(static type => type.Name).ToList();

        await Assert.That(introspection).DoesNotContain("PrimitiveType");
        await Assert.That(typeof(ITypeDefinition).GetProperty("PrimitiveType", any)).IsNull();
        await Assert.That(typeof(ITypeDefinitionProvider).GetMethods(any)
            .Where(static method => method.Name == nameof(ITypeDefinitionProvider.GetTypeDefinition))
            .SelectMany(static method => method.GetParameters())
            .Select(static parameter => parameter.ParameterType)).IsEquivalentTo(new[] { typeof(string) })
            .Because("a provider resolves a name; no id from a shared list");
        await Assert.That(typeof(Poly.Ast.Node).Assembly.GetType("Poly.Ast.Nodes.PrimitiveTypeReference")).IsNull();
    }

    [Test]
    public async Task ClrTable_IsTheClrHostsOwn() {
        await Assert.That(typeof(ClrPrimitive).IsPublic).IsFalse();
        await Assert.That(typeof(ClrPrimitive).Namespace).IsEqualTo(typeof(ClrTypeDefinitionRegistry).Namespace);
        foreach (var primitive in Enum.GetValues<ClrPrimitive>())
            await Assert.That(primitive.GetClrType()).IsNotNull().Because($"the CLR table lists only types the CLR has; '{primitive}'");
    }

    [Test]
    public async Task ClrTable_IsNamedOnlyByTheClrHostTheVmAndTheRuntimeExecutor() {
        string[] hosts = ["Poly/Introspection/CommonLanguageRuntime/", "Poly/Interpretation/Vm/", "Poly/DomainModeling/Runtime/"];
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var word = new System.Text.RegularExpressions.Regex(@"\bClrPrimitive\b");
        var outside = new[] { "Poly", "src", "Poly.Mcp" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(static file => !file.Contains("/obj/", StringComparison.Ordinal) && !file.Contains("/bin/", StringComparison.Ordinal))
            .Where(file => !hosts.Any(host => file.StartsWith(host, StringComparison.Ordinal)))
            .Where(file => word.IsMatch(File.ReadAllText(Path.Combine(root, file))))
            .ToList();

        await Assert.That(outside).IsEmpty()
            .Because("the CLR's table of built-in types is the CLR host's; a meaning layer asks a definition for a fact");
    }

    [Test]
    public async Task ClrRoundTrip_DateTimeOffset_IsItsOwnGroundType() {
        await Assert.That(typeof(DateTimeOffset).GetPrimitiveType()).IsEqualTo(ClrPrimitive.DateTimeOffset);
        await Assert.That(ClrPrimitive.DateTimeOffset.GetClrType()).IsEqualTo(typeof(DateTimeOffset));
        ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(DateTimeOffset));
        await Assert.That(definition.GetFact<Atomic>()).IsNotNull();
        await Assert.That(definition.GetFact<Temporal>()).IsEqualTo(new Temporal(HasDate: true, HasTime: true, HasOffset: true));
    }

    [Test]
    public async Task SecondHost_StatesAndResolvesATypeTheClrHasNoTypeFor() {
        // ECMAScript's `undefined`: the host names it and states what it is; its provider resolves and enumerates it,
        // and the CLR does not pretend to have a type for it.
        var provider = new SecondHostProvider();
        provider.Add(new HostPrimitive("Undefined", new HostAbsence()));

        var resolved = provider.GetTypeDefinition("Undefined");

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.GetFact<HostAbsence>()).IsNotNull();
        await Assert.That(resolved.GetFact<Atomic>()).IsNull().Because("the host states only the facts it gave");
        await Assert.That(provider.Types).Contains(resolved);
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("Undefined")).IsNull();
        await Assert.That(ClrTypeDefinitionRegistry.Shared.Types.Any(static type => type.GetFact<HostAbsence>() is not null)).IsFalse();
    }

    [Test]
    public async Task SecondHost_StatesANumberNoSharedListNames_AsAFactOfItsOwn() {
        // A 128-bit decimal float: the host states what it is as a fact of its own and as a number, its provider
        // resolves and enumerates it, and it is not confusable with the CLR's decimal.
        var provider = new SecondHostProvider();
        var decimal128 = new HostDecimal(Bits: 128);
        provider.Add(new HostPrimitive("Decimal128", decimal128));

        var resolved = provider.GetTypeDefinition("Decimal128");

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.GetFact<HostDecimal>()).IsEqualTo(decimal128);
        await Assert.That(resolved.GetClrPrimitive()).IsNull();
        ITypeDefinition clrDecimal = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(decimal));
        await Assert.That(clrDecimal.GetFact<HostDecimal>()).IsNull()
            .Because("the CLR's decimal is not the host's, and states nothing for the host's question");
        await Assert.That(TypeDefinitionExtensions.IsSameType(resolved, clrDecimal)).IsFalse();
        await Assert.That(provider.Types).Contains(resolved);
    }

    /// <summary>What the second host's decimal is, in the host's own terms.</summary>
    private sealed record HostDecimal(int Bits) : TypeFact;

    /// <summary>The second host's type of the absent value.</summary>
    private sealed record HostAbsence : TypeFact;

    private sealed class SecondHostProvider : ITypeDefinitionProvider {
        private readonly Dictionary<string, ITypeDefinition> _types = new(StringComparer.Ordinal);

        public void Add(ITypeDefinition definition) => _types[definition.Name] = definition;

        public ITypeDefinition? GetTypeDefinition(string name) => _types.GetValueOrDefault(name);
        public IEnumerable<ITypeDefinition> Types => _types.Values;
    }

    private sealed class HostPrimitive(string name, TypeFact fact) : ITypeDefinition {
        public TFact? GetFact<TFact>() where TFact : TypeFact => fact as TFact;
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
        public ITypeDefinition? NullableOf => null;

        public bool IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }
}