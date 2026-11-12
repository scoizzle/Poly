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

    private const string PublicOrderInternalLine = """
        domain Shop

        Line: entity {
          Qty: Number default(1)
        }

        public entity Order {
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
    public async Task Export_WhenActionNotPublic_PrintsInternalMethod() {
        var text = Export(Repro604).AllText;

        await Assert.That(text).Contains("internal DomainResult Secret(");
        await Assert.That(text).Contains("public DomainResult Set(");
    }

    [Test]
    public async Task Export_WhenPublicEntityRelatesToInternalEntity_NavigationAndCreateAreInternalAndCompile() {
        var e = Export(PublicOrderInternalLine);

        await Assert.That(e.Type("Order").DeclaredAccessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(e.Type("Line").DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        var order = e.Type("Order");
        await Assert.That(order.GetMembers("Lines").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(order.GetMembers("Main").Single().DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        var creates = order.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal)).ToArray();
        await Assert.That(creates.Length).IsGreaterThan(0);
        await Assert.That(creates.All(m => m.DeclaredAccessibility == Accessibility.Internal)).IsTrue();
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
}