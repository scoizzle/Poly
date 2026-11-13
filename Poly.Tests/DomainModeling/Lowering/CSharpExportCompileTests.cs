using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;

using Compiler = Poly.DslCompiler.DslCompiler;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-22 (#604): the generated C# honours DSL visibility. A type or action prints <c>public</c> iff it is
/// marked <c>public</c>, otherwise <c>internal</c>; a member of a public type whose signature would name an
/// internal type is demoted to <c>internal</c>; the host's DbContext and DTOs follow. Every export compiles
/// with warnings as errors. Created standalone here; later export slices extend it.
/// </summary>
public sealed class CSharpExportCompileTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    private sealed record Exported(IReadOnlyList<(string FileName, string Source)> Files, CSharpCompilation Compilation) {
        public string AllText => string.Join("\n", Files.Select(f => f.Source));

        public IReadOnlyList<string> Diagnostics => Compilation.GetDiagnostics()
            .Where(d => d.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToArray();

        public INamedTypeSymbol Type(string name) =>
            AllTypes(Compilation.GlobalNamespace).First(t => t.Name == name && t.ContainingType is null);

        public IEnumerable<IMethodSymbol> Methods(string type, string name) =>
            Type(type).GetMembers(name).OfType<IMethodSymbol>();
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) {
        foreach (var type in ns.GetTypeMembers())
            yield return type;
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var type in AllTypes(child))
                yield return type;
    }

    private static Exported Export(string poly) {
        var session = DomainSession.ForSource(poly, Seed, Catalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), Seed);
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!outcome.Succeeded) {
            throw new InvalidOperationException(string.Join("; ", outcome.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        }

        return Compile(session.Emit(outcome.Root, outcome.Analysis));
    }

    private static Exported ExportHost(string poly) {
        var result = new Compiler()
            .Load(new HttpLibrary())
            .Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("; ", result.Errors ?? []));
        // Program.cs is a top-level ASP.NET host (and carries the DTOs): it needs the web SDK references this
        // gate does not load, so the booted PackOracle fixtures cover it. The DbContext and entities compile here.
        var all = result.Files!.Select(f => (f.FileName, f.Source)).ToArray();
        return Compile(all, all
            .Where(f => f.FileName.EndsWith(".cs", StringComparison.Ordinal) && f.FileName != "Program.cs")
            .ToArray());
    }

    private static Exported Compile(
        IReadOnlyList<(string FileName, string Source)> files,
        IReadOnlyList<(string FileName, string Source)>? compiled = null) {
        var trees = (compiled ?? files).Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.FileName)).ToArray();
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithGeneralDiagnosticOption(ReportDiagnostic.Error);
        return new Exported(files, CSharpCompilation.Create("L22_" + Guid.NewGuid().ToString("N"), trees, references, options));
    }

    private const string Repro604 = """
        domain Vis

        Money: value {
          Amount: Number
        }

        public value Pub {
          Amount: Number
        }

        Hidden: entity {
          Count: Number default(0)
          Bump: action { assign Count to Count + 1 }
        }

        public entity Shown {
          Count: Number default(0)
          Secret: action { assign Count to Count + 1 }
          public action Set { assign Count to 1 }
        }
        """;

    private const string InternalOrderInternalLine = """
        domain Shop

        Line: entity {
          Qty: Number default(1)
        }

        Order: entity {
          Total: Number default(0)
          lines: many Line
          main: Line
          AddLine: action (qty: Number) {
            create in lines { Qty: qty }
          }
        }
        """;

    [Test]
    public async Task Export_WhenEntityNotPublic_EmitsInternalAndCompiles() {
        var e = Export(Repro604);

        await Assert.That(e.Type("Hidden").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Money").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Shown").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Type("Pub").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Shown", "Set").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Shown", "Secret").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Hidden", "Bump").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Hidden", "Create").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Export_Issue604Repro_HidesInternalAndKeepsPublicSurface() {
        var e = Export("""
            domain Vis

            Money: value {
              Amount: Number
            }

            public value Pub {
              Amount: Number
            }

            Hidden: entity {
              Total: Number
              Bump: action (n: Number) { assign Total to n }
            }

            public entity Shown {
              Total: Number
              public action Set (n: Number, p: Pub) { assign Total to n }
              Secret: action (n: Number) { assign Total to n }
            }
            """);

        await Assert.That(e.Type("Hidden").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Money").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Shown").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Type("Pub").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Shown", "Set").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Shown", "Secret").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Export_WhenActionNotPublic_PrintsInternalMethod() {
        var text = Export(Repro604).AllText;

        await Assert.That(text).Contains("internal DomainResult Secret(");
        await Assert.That(text).Contains("public DomainResult Set(");
    }

    [Test]
    public async Task Export_WhenInternalEntityRelatesToInternalEntity_NavigationStaysPublicCreateIsInternalAndCompile() {
        var e = Export(InternalOrderInternalLine);

        await Assert.That(e.Type("Order").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Line").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        var order = e.Type("Order");
        await Assert.That(order.GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(order.GetMembers("Main").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        var creates = order.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal)).ToArray();
        await Assert.That(creates.Length).IsGreaterThan(0);
        await Assert.That(creates.All(m => m.DeclaredAccessibility == Accessibility.Internal)).IsTrue();
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    // L-16a: Create{Nav} is internal on every entity, public ones included. Read from the compiled compilation's
    // symbols (the wrapper and its private body), never from printed text.
    [Test]
    public async Task CreateNav_OnPublicEntity_IsInternal() {
        var e = Export("""
            domain Shop

            public entity Line {
              Qty: Number default(1)
            }

            public entity Order {
              Total: Number default(0)
              lines: many Line
              AddLine: action (qty: Number) {
                create in lines { Qty: qty }
              }
            }
            """);

        var order = e.Type("Order");
        await Assert.That(order.DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        var createNav = e.Methods("Order", "CreateLines").ToArray();
        await Assert.That(createNav.Length).IsEqualTo(1);
        await Assert.That(createNav[0].DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(order.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Name.Contains("CreateLines", StringComparison.Ordinal))
            .All(m => m.DeclaredAccessibility is Accessibility.Internal or Accessibility.Private)).IsTrue();
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    private static string DbContextText(Exported e) =>
        e.Files.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;

    // L-22b: the navigation prints internal (the surface stays closed), the pack maps it explicitly, and the whole
    // host compiles with warnings as errors. A8 pins the F2 consequence: Create and Create{Nav} are internal too.
    [Test]
    public async Task Export_WhenPublicEntityHasManyNavToInternalEntity_NavigationIsInternalAndMappedAndCompiles() {
        var e = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Line: entity {
              Qty: Number range(1, 100)
            }

            public entity Order {
              Code: Text unique length(3, 8)
              lines: many Line
              public action AddLine (qty: Number) { create in lines { Qty: qty } }
            }
            """);

        await Assert.That(e.Type("Order").GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Order", "Create").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Order", "CreateLines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Order", "AddLine").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        var db = DbContextText(e);
        await Assert.That(db).Contains("b.HasMany(e => e.Lines).WithOne()");
        await Assert.That(db.IndexOf("HasMany(", StringComparison.Ordinal))
            .IsLessThan(db.IndexOf("FindNavigation(nameof(Order.Lines))", StringComparison.Ordinal));
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Export_WhenPublicEntityHasSingularNavToInternalEntity_NavigationIsInternalAndMappedAndCompiles() {
        var e = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Receipt: entity {
              Note: Text
            }

            public entity Order {
              Code: Text unique length(3, 8)
              receipt: one Receipt
              public action Issue (note: Text) { create in receipt { Note: note } }
            }
            """);

        await Assert.That(e.Type("Order").GetMembers("Receipt").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Order", "Create").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Order", "CreateReceipt").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(DbContextText(e)).Contains("b.HasOne(e => e.Receipt).WithMany()");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    // The peer declares the pairing (`via` on the target): analysis resolves it once, the pack reads that fact.
    [Test]
    public async Task Export_WhenPeerDeclaresVia_ManyNavMapsWithTheResolvedInverse() {
        var e = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Line: entity {
              Qty: Number range(1, 100)
              order: Order via lines
            }

            public entity Order {
              Code: Text unique length(3, 8)
              lines: many owned Line
              public action AddLine (qty: Number) { create in lines { Qty: qty } }
            }
            """);

        await Assert.That(e.Type("Order").GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Line").GetMembers("Order").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Order", "Create").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Methods("Order", "CreateLines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(DbContextText(e)).Contains("b.HasMany(e => e.Lines).WithOne(i => i.Order)");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    // Reverse shape: the public entity is the child; its singular nav to the internal parent is the internal one.
    [Test]
    public async Task Export_WhenPublicChildNavigatesToInternalParent_SingularNavIsInternalAndMapsWithItsCollectionInverse() {
        var e = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Order: entity {
              Code: Text unique length(3, 8)
              lines: many Line
            }

            public entity Line {
              Qty: Number range(1, 100)
              order: Order
            }
            """);

        await Assert.That(e.Type("Line").GetMembers("Order").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(e.Type("Order").GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Line", "Create").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        var db = DbContextText(e);
        await Assert.That(db).Contains("b.HasOne(e => e.Order).WithMany(i => i.Lines)");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    // The predicate is exactly "owner public AND target internal": every other pairing prints and maps as before.
    [Test]
    [Arguments("public entity", "public entity")]
    [Arguments("", "")]
    [Arguments("", "public entity")]
    public async Task Export_WhenNavigationIsNotPublicOwnerToInternalTarget_EmitsNoExplicitMapping(string ownerHeader, string lineHeader) {
        var owner = ownerHeader.Length == 0 ? "Order: entity" : ownerHeader + " Order";
        var line = lineHeader.Length == 0 ? "Line: entity" : lineHeader + " Line";
        var e = ExportHost($$"""
            domain Shop
            uses sqlite
            uses http

            {{line}} {
              Qty: Number range(1, 100)
            }

            {{owner}} {
              Code: Text unique length(3, 8)
              lines: many Line
            }
            """);

        await Assert.That(e.Type("Order").GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        var db = DbContextText(e);
        await Assert.That(db).DoesNotContain("HasMany(");
        await Assert.That(db).DoesNotContain("HasOne(");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("public entity", "public entity")]
    [Arguments("", "")]
    [Arguments("", "public entity")]
    public async Task Export_WhenSingularNavigationIsNotPublicOwnerToInternalTarget_EmitsNoExplicitMapping(string ownerHeader, string targetHeader) {
        var owner = ownerHeader.Length == 0 ? "Order: entity" : ownerHeader + " Order";
        var target = targetHeader.Length == 0 ? "Receipt: entity" : targetHeader + " Receipt";
        var e = ExportHost($$"""
            domain Shop
            uses sqlite
            uses http

            {{target}} {
              Note: Text
            }

            {{owner}} {
              Code: Text unique length(3, 8)
              receipt: one Receipt
            }
            """);

        await Assert.That(e.Type("Order").GetMembers("Receipt").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        var db = DbContextText(e);
        await Assert.That(db).DoesNotContain("HasMany(");
        await Assert.That(db).DoesNotContain("HasOne(");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    // E-03 binding re-check: printed types live in the domain namespace, and the host qualifies an entity
    // named `Program` or like its domain. DomainAccess matches domain types by simple name, so a qualified
    // name must never reach it: demotion, the explicit EF mapping and the host clamp still apply on those shapes.
    [Test]
    [Arguments("Shop", "Order")]
    [Arguments("Registration", "Program")]
    [Arguments("Library", "Library")]
    public async Task Export_NamespacedDomain_PublicOwnerWithInternalTargets_StaysDemotedMappedAndClamped(string domainName, string owner) {
        var e = ExportHost($$"""
            domain {{domainName}}
            uses sqlite
            uses http

            Money: value {
              Amount: Number
            }

            Line: entity {
              Qty: Number range(1, 100)
            }

            public entity {{owner}} {
              Code: Text unique length(3, 8)
              Total: Number default(0)
              lines: many Line
              Pay: action (bill: Money) { assign Total to Total + 1 }
            }
            """);

        var db = DbContextText(e);
        await Assert.That(e.AllText).Contains($"namespace {domainName};");
        // By metadata name: a referenced assembly may declare its own global `Program`.
        var ownerType = e.Compilation.GetTypeByMetadataName($"{domainName}.{owner}")!;
        await Assert.That(ownerType.GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(ownerType.GetMembers("Pay").OfType<IMethodSymbol>().Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(db).Contains("b.HasMany(e => e.Lines).WithOne()");
        await Assert.That(e.AllText).Contains($"internal class {domainName}DbContext");
        await Assert.That(e.AllText).Contains("internal record PayDto");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("Registration", "Program")]
    [Arguments("Library", "Library")]
    public async Task Export_NamespacedDomain_AllPublicEntities_KeepsDbContextAndDtoPublic(string domainName, string owner) {
        var e = ExportHost($$"""
            domain {{domainName}}
            uses sqlite
            uses http

            public entity {{owner}} {
              Code: Text unique length(3, 8)
            }
            """);

        await Assert.That(e.AllText).Contains($"public class {domainName}DbContext");
        await Assert.That(e.AllText).Contains($"public record {owner}Dto");
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Export_WhenAllMarkedPublic_EmitsPublicSurfaceUnchanged() {
        var e = Export("""
            domain Open

            public value Money {
              Amount: Number
            }

            public entity Account {
              Count: Number default(0)
              public action Bump { assign Count to Count + 1 }
            }
            """);

        await Assert.That(e.Type("Account").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Type("Money").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Account", "Bump").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Methods("Account", "Create").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Export_WhenInternalEntity_PacksStillCompile() {
        var e = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Line: entity {
              Qty: Number default(1)
            }

            Order: entity {
              Total: Number default(0)
              lines: many Line
            }
            """);

        await Assert.That(e.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Export_WhenInternalEntity_HostTypesAreInternal() {
        var text = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Order: entity {
              Total: Number range(0, 100)
            }
            """).AllText;

        await Assert.That(text).Contains("internal class ShopDbContext");
        await Assert.That(text).Contains("internal record OrderDto");
    }

    [Test]
    public async Task Export_WhenPublicEntity_HostTypesStayPublic() {
        var text = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            public entity Order {
              Total: Number range(0, 100)
            }
            """).AllText;

        await Assert.That(text).Contains("public class ShopDbContext");
        await Assert.That(text).Contains("public record OrderDto");
    }

    [Test]
    public async Task Export_WhenStagedEntity_StageEnumFollowsItsEntity() {
        var e = Export("""
            domain Flow

            Hidden: entity {
              Draft: stage { Go: action { transition to Done } }
              Done: stage {}
            }

            public entity Shown {
              Draft: stage { Go: action { transition to Done } }
              Done: stage {}
            }
            """);

        var enums = AllTypes(e.Compilation.GlobalNamespace).Where(t => t.TypeKind == TypeKind.Enum).ToArray();
        await Assert.That(enums.Single(t => t.Name.StartsWith("Hidden", StringComparison.Ordinal)).DeclaredAccessibility)
            .IsEqualTo(Accessibility.Internal);
        await Assert.That(enums.Single(t => t.Name.StartsWith("Shown", StringComparison.Ordinal)).DeclaredAccessibility)
            .IsEqualTo(Accessibility.Public);
        await Assert.That(e.Diagnostics).IsEmpty();
    }

    // F6 (fix round 1): the host clamp's two other branches.
    [Test]
    public async Task Export_WhenPublicEntityHasInternalActionWithInternalValueParameter_ActionDtoIsInternal() {
        var text = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            Money: value {
              Amount: Number
            }

            public entity Order {
              Code: Text length(1, 10)
              Total: Number default(0)
              Pay: action (bill: Money) { assign Total to Total + 1 }
            }
            """).AllText;

        await Assert.That(text).Contains("internal record PayDto");
        await Assert.That(text).Contains("public record OrderDto");
    }

    [Test]
    public async Task Export_WhenDomainMixesPublicAndInternalEntities_DbContextIsInternalAndPublicEntityDtoStaysPublic() {
        var text = ExportHost("""
            domain Shop
            uses sqlite
            uses http

            public entity Order {
              Total: Number range(0, 100)
            }

            Audit: entity {
              Note: Text length(1, 50)
            }
            """).AllText;

        await Assert.That(text).Contains("internal class ShopDbContext");
        await Assert.That(text).Contains("public record OrderDto");
    }
}