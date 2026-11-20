using Poly.Interpretation.CSharp;
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
        await Assert.That(typeof(ITypeSystem).GetMethods(any)
            .Where(static method => method.Name == nameof(ITypeSystem.Resolve))
            .SelectMany(static method => method.GetParameters())
            .Select(static parameter => parameter.ParameterType)).IsEquivalentTo(new[] { typeof(string) })
            .Because("a system resolves a name; no id from a shared list (#998 T12b)");
        await Assert.That(typeof(Poly.Ast.Node).Assembly.GetType("Poly.Ast.Nodes.PrimitiveTypeReference")).IsNull();
    }

    [Test]
    public async Task ClrReferenceShorthand_IsAnInternalTool_NotPublicSurface() {
        // The public ClrTypeReference node was deleted for being public (I8k); its shorthand must not become the
        // public way to state a type in its place.
        await Assert.That(typeof(TypeReferenceExtensions).IsPublic).IsFalse();
    }

    [Test]
    public async Task ClrPrimitive_IsGone() {
        // #998 T10b: the CLR host keeps identity facts only; C#'s rules key on C#'s SimpleType and runtime identity.
        var host = typeof(ClrTypeDefinitionRegistry).Assembly;
        await Assert.That(host.GetTypes().Where(static type => type.Name is "ClrPrimitive" or "ClrPrimitiveExtensions" or "ClrPrimitiveTypes"))
            .IsEmpty().Because("no CLR primitive enum or its type table remains");
        await Assert.That(host.GetType("Poly.Introspection.CommonLanguageRuntime.ClrPrimitive")).IsNull();
    }

    [Test]
    public async Task ClrPrimitive_IsGone_FromEverySourceFile() {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var word = new System.Text.RegularExpressions.Regex(@"\bClrPrimitive\b");
        var named = new[] { "Poly", "src", "Poly.Mcp" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(static file => !file.Contains("/obj/", StringComparison.Ordinal) && !file.Contains("/bin/", StringComparison.Ordinal))
            .Where(file => word.IsMatch(File.ReadAllText(Path.Combine(root, file))))
            .ToList();

        await Assert.That(named).IsEmpty()
            .Because("the CLR's primitive enum is deleted (#998 T10b): C# keys on its own simple types and runtime identity");
    }

    [Test]
    public async Task DateTimeOffset_IsItsOwnGroundType_ByRuntimeIdentity() {
        ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(DateTimeOffset));
        await Assert.That(definition.GetRuntimeTypeOrThrow()).IsEqualTo(typeof(DateTimeOffset));
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.SimpleTypeOf(definition)).IsNull()
            .Because("it is no C# simple type");
        await Assert.That(ClrBuiltInRules.IsPrimitive(definition)).IsTrue();
        await Assert.That((ClrBuiltInRules.IsCalendarType(definition), ClrBuiltInRules.IsTimestamp(definition))).IsEqualTo((true, true))
            .Because("the CLR host's own identity facts state it (#998 T8)");
    }

    [Test]
    public async Task SecondHost_StatesAndResolvesATypeTheClrHasNoTypeFor() {
        // ECMAScript's `undefined`: the host names it and states what it is; its provider resolves and enumerates it,
        // and the CLR does not pretend to have a type for it.
        var provider = new SecondHostProvider();
        provider.Add(new HostPrimitive("Undefined", hostKind: "absence"));

        var resolved = provider.ByQualifiedName("Undefined");

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved is IHostType { HostKind: "absence" }).IsTrue();
        await Assert.That(ClrBuiltInRules.IsPrimitive(resolved!)).IsFalse().Because("it is none of the CLR host's primitives");
        await Assert.That(provider.Types).Contains(resolved);
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("Undefined")).IsNull();
        await Assert.That(ClrTypeDefinitionRegistry.Shared.Types.Any(static type => type is IHostType)).IsFalse();
    }

    [Test]
    public async Task SecondHost_StatesANumberNoSharedListNames_AsAFactOfItsOwn() {
        // A 128-bit decimal float: the host answers what it is as a number and in its own terms, its provider resolves
        // and enumerates it, and it is not confusable with the CLR's decimal.
        var provider = new SecondHostProvider();
        provider.Add(new HostPrimitive("Decimal128", hostKind: "decimal128", numberBits: 128));

        var resolved = provider.ByQualifiedName("Decimal128");

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved is IHostType { HostKind: "decimal128", HostNumberBits: 128 }).IsTrue()
            .Because("the host states what the number is in its own terms (#998 T8a)");
        await Assert.That(ClrBuiltInRules.IsPrimitive(resolved!)).IsFalse().Because("it is none of the CLR host's primitives");
        ITypeDefinition clrDecimal = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(decimal));
        await Assert.That(clrDecimal is IHostType).IsFalse()
            .Because("the CLR's decimal is not the host's, and is never asked the host's question");
        await Assert.That(TypeDefinitionExtensions.IsSameType(resolved, clrDecimal)).IsFalse();
        await Assert.That(provider.Types).Contains(resolved);
    }

    /// <summary>A question only the second host answers: what the type is, in the host's own terms.</summary>
    private interface IHostType : ITypeDefinition {
        string HostKind { get; }

        /// <summary>The width of a host number, in the host's own terms; 0 for a type that is no number.</summary>
        int HostNumberBits { get; }
    }

    private sealed class SecondHostProvider : IQualifiedNameSource {
        private readonly Dictionary<string, ITypeDefinition> _types = new(StringComparer.Ordinal);

        public void Add(ITypeDefinition definition) => _types[definition.Name] = definition;

        public ITypeDefinition? ByQualifiedName(string name) => _types.GetValueOrDefault(name);
        public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
            _types.Values.Where(type => string.Equals(type.Namespace, @namespace, StringComparison.Ordinal));
        public IEnumerable<ITypeDefinition> Types => _types.Values;
    }

    private sealed class HostPrimitive(string name, string hostKind, int numberBits = 0) : IHostType {
        public string HostKind => hostKind;
        public int HostNumberBits => numberBits;
        public string Name => name;
        public string? Namespace => null;
        public ITypeDefinition? BaseType => null;
        public IEnumerable<ITypeDefinition> Interfaces => [];
        public IEnumerable<IParameter> GenericParameters => [];
        public IEnumerable<ITypeMember> Members => [];
        public IEnumerable<ITypeOperator> Operators => [];

        public ITypeSystem System => Poly.Tests.TestHelpers.TestTypeSystem.Instance;

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    }
}