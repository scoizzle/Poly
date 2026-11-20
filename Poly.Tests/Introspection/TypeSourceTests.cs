using System.Text;

using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// A source answers what it contains, by namespace, and nothing about what a name means (#998 S1). The same contract
/// serves a CLR assembly, a unit's declarations and a host with no CLR types.
/// </summary>
public class TypeSourceTests {
    [Test]
    public async Task ClrAssembly_TypesIn_ListsThatNamespaceAndNoOther() {
        var source = new ClrAssemblySource(typeof(StringBuilder).Assembly);

        var listed = source.TypesIn("System.Text").ToList();

        await Assert.That(listed.Select(static type => type.Name)).Contains(nameof(StringBuilder));
        await Assert.That(listed.Where(static type => type.Namespace != "System.Text")).IsEmpty();
        await Assert.That(source.TypesIn("No.Such.Namespace")).IsEmpty();
    }

    [Test]
    public async Task ClrAssembly_TypesIn_StatesTheRegistrysOwnDefinition() {
        var source = new ClrAssemblySource(typeof(StringBuilder).Assembly);

        var listed = source.TypesIn("System.Text").Single(static type => type.Name == nameof(StringBuilder));

        await Assert.That(listed).IsSameReferenceAs(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(StringBuilder)))
            .Because("a type reached through a source is the same type reached any other way");
    }

    [Test]
    public async Task ClrAssembly_TypesIn_ListsNoNestedType() {
        var source = new ClrAssemblySource(typeof(TypeSourceTests).Assembly);

        var listed = source.TypesIn(typeof(TypeSourceTests).Namespace).Select(static type => type.Name).ToList();

        await Assert.That(listed).Contains(nameof(TypeSourceTests));
        await Assert.That(listed).DoesNotContain(nameof(NestedInTheTests))
            .Because("a nested type is reached through its container");
    }

    [Test]
    public async Task ClrAssembly_TypesIn_ListsAnOpenGenericAndNoCompilerGeneratedType() {
        var source = new ClrAssemblySource(typeof(List<>).Assembly);

        var generic = source.TypesIn("System.Collections.Generic").Select(static type => type.Name).ToList();

        await Assert.That(generic).Contains(typeof(List<>).Name);
        await Assert.That(source.TypesIn("system.collections.generic")).IsEmpty().Because("a namespace is compared ordinally");
        var own = new ClrAssemblySource(typeof(TypeSourceTests).Assembly);
        var everything = typeof(TypeSourceTests).Assembly.GetTypes().Select(static type => type.Namespace).Distinct()
            .SelectMany(own.TypesIn).Select(static type => type.Name).ToList();
        await Assert.That(everything.Where(static name => name.Contains('<'))).IsEmpty()
            .Because("no source text can name a compiler-generated type");
    }

    [Test]
    public async Task ClrAssembly_TypesIn_Null_ListsTheGlobalNamespace() {
        var source = new ClrAssemblySource(typeof(TypeSourceTests).Assembly);

        var global = source.TypesIn(null).ToList();

        await Assert.That(global.Select(static type => type.Name)).Contains(nameof(DeclaredInTheGlobalNamespace));
        await Assert.That(global.Where(static type => type.Namespace is not null)).IsEmpty();
    }

    [Test]
    public async Task ClrAssembly_TypeIn_FindsOneTypeByItsNamespaceAndName() {
        var source = new ClrAssemblySource(typeof(TypeSourceTests).Assembly);
        var @namespace = typeof(TypeSourceTests).Namespace;

        await Assert.That(source.TypeIn(@namespace, nameof(TypeSourceTests)))
            .IsSameReferenceAs(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(TypeSourceTests)));
        await Assert.That(source.TypeIn(@namespace, "NoSuchType")).IsNull();
        await Assert.That(source.TypeIn(@namespace, $"{nameof(TypeSourceTests)}+{nameof(NestedInTheTests)}")).IsNull()
            .Because("a nested type is not declared in the namespace");
        await Assert.That(source.TypeIn(null, nameof(DeclaredInTheGlobalNamespace))).IsNotNull();
    }

    [Test]
    public async Task ClrRegistry_BareName_IsNotLookedUpAcrossLoadedAssemblies() {
        ITypeSource loaded = ClrTypeDefinitionRegistry.Shared;
        // Resolving the type by its runtime type first caches it under its full name, which for a global type has no
        // dot: a bare lookup must not then find it in the cache.
        _ = new ClrAssemblySource(typeof(DeclaredInTheGlobalNamespace).Assembly).TypeIn(null, nameof(DeclaredInTheGlobalNamespace));

        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(nameof(DeclaredInTheGlobalNamespace))).IsNull()
            .Because("a loaded assembly declares it, and a bare name is a scope's to bind, not the process's");
        await Assert.That(loaded.TypeIn(null, nameof(DeclaredInTheGlobalNamespace))).IsNull();
        await Assert.That(loaded.TypeIn("System.Text", nameof(StringBuilder)))
            .IsSameReferenceAs(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(StringBuilder)));
    }

    [Test]
    public async Task ClrRegistry_BareNameThatSpellsAClrBuiltIn_IsTheInterimAlias() {
        // Interim (#998 S2, removing row: the scope binds every bare name, including those of raw trees built with no
        // unit): a bare name that spells a member of the CLR host's built-in table still names that type. This test is
        // deleted with the rule; until then it pins exactly how far the alias reaches.
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("DateTime"))
            .IsSameReferenceAs(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(DateTime)));
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("Int32"))
            .IsSameReferenceAs(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(int)));
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("StringBuilder")).IsNull()
            .Because("the alias is the built-in table only, never a namespace lookup");
        await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("int")).IsNull()
            .Because("a keyword is the C# surface's name, bound by the reader, not by the registry");
    }

    [Test]
    public async Task Declarations_TypesIn_ListsADeclarationUnderItsOwnNamespace() {
        var registry = new AstTypeRegistry();
        var order = new AstTypeDefinition(new TypeDefinitionNode("Order", Namespace: "Shop"), registry, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        var loose = new AstTypeDefinition(new TypeDefinitionNode("Loose"), registry, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        registry.Add(order);
        registry.Add(loose);

        // By reference: a definition is compared by identity, never by walking its members (its System is a whole system).
        await Assert.That(registry.TypesIn("Shop").ToList()).HasSingleItem();
        await Assert.That(registry.TypesIn("Shop").Single()).IsSameReferenceAs(order);
        await Assert.That(registry.TypesIn(null).ToList()).HasSingleItem();
        await Assert.That(registry.TypesIn(null).Single()).IsSameReferenceAs(loose);
        await Assert.That(registry.TypesIn("Elsewhere")).IsEmpty();
    }

    [Test]
    public async Task HostWithNoClrTypes_IsASource() {
        var host = new LedgerHost();

        var listed = host.TypesIn(LedgerHost.Namespace).ToList();

        await Assert.That(listed).IsEquivalentTo(new[] { host.Amount, host.Account, host.Entry });
        await Assert.That(listed.Any(static type => type.TryGetRuntimeType(out _))).IsFalse()
            .Because("a host need not have a CLR type behind a definition");
        await Assert.That(host.TypesIn("System")).IsEmpty();
    }

    private sealed class NestedInTheTests;
}