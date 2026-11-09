using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.Polyc;

public class PolycCompileTests {
    private static PolyCompiler Compiler(string seed = "authoring") => new(ProductCatalog.Catalog, seed);

    [Test]
    public async Task Compile_WarningsOnly_SucceedsAndReportsWarning() {
        var result = Compiler().Compile(PolycTestSupport.Hotel(), "hotel.poly");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Files.Count).IsGreaterThan(0);
        var warning = result.Diagnostics.First(d => d.IsWarning);
        await Assert.That(warning.Code).IsEqualTo("DMREL002");
        await Assert.That(warning.File).IsEqualTo("hotel.poly");
        await Assert.That(warning.Placed).IsTrue();
    }

    [Test]
    public async Task Compile_ManyErrors_ReportsEveryOne() {
        var result = Compiler().Compile(PolycTestSupport.ManyErrors(12), "broken.poly");
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(12);
        await Assert.That(result.Files.Count).IsEqualTo(0);
        await Assert.That(errors.Select(e => e.Code).Distinct().Single()).IsEqualTo("DMEFF011");
    }

    [Test]
    public async Task Compile_FindingWithoutSpan_IsKeptUnplacedAtOneOne() {
        var result = Compiler().Compile("domain D\nA: entity {\n  X: Text\n  X: Text\n}\n", "dup.poly");
        var finding = result.Diagnostics.Single(d => d.IsError);
        await Assert.That(finding.Placed).IsFalse();
        await Assert.That(finding.Line).IsEqualTo(1);
        await Assert.That(finding.Column).IsEqualTo(1);
    }

    [Test]
    public async Task Compile_UnknownUses_FailsClosedAtHeaderTokenWithoutAnalyzingBody() {
        var result = Compiler().Compile("domain D\nuses nope\nA: entity {\n  X: Missing\n}\n", "x.poly");
        var only = result.Diagnostics.Single();
        await Assert.That(only.Code).IsEqualTo("DSL0002");
        await Assert.That(only.Line).IsEqualTo(2);
        await Assert.That(only.Column).IsEqualTo(6);
        await Assert.That(only.EndColumn).IsEqualTo(10);
        await Assert.That(only.Placed).IsTrue();
    }

    [Test]
    public async Task Compile_DuplicateUses_ReportsDsl0003AtSecondOccurrence() {
        var result = Compiler().Compile("domain D\nuses sqlite\nuses sqlite\nA: entity {\n  X: Text\n}\n", "x.poly");
        var only = result.Diagnostics.Single();
        await Assert.That(only.Code).IsEqualTo("DSL0003");
        await Assert.That(only.Line).IsEqualTo(3);
    }

    [Test]
    public async Task Compile_DuplicatePinnedUses_ReportsDsl0003AtSecondOccurrence() {
        var result = Compiler().Compile(
            "domain D\nuses billing 2024-11-20\nuses billing v1\nA: entity {\n  X: Text\n}\n", "x.poly");
        var only = result.Diagnostics.Single();
        await Assert.That(only.Code).IsEqualTo("DSL0003");
        await Assert.That(only.Line).IsEqualTo(3);
    }

    [Test]
    public async Task Compile_ParseError_ReportsDsl0001AtPosition() {
        var result = Compiler().Compile("domain D\nA: entity {\n  X: Text @\n}\n", "x.poly");
        var only = result.Diagnostics.Single();
        await Assert.That(only.Code).IsEqualTo("DSL0001");
        await Assert.That(only.Line).IsEqualTo(3);
        await Assert.That(only.Column).IsEqualTo(11);
        await Assert.That(only.Placed).IsTrue();
        await Assert.That(only.Message).DoesNotContain("(line");
    }

    [Test]
    public async Task Compile_AuthoringSeed_IsDefaultWhenNoUses() {
        var result = Compiler().Compile(PolycTestSupport.Orders, "orders.poly");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Domain!.Extensions).Contains("storage");
        var language = Compiler("language").Compile(PolycTestSupport.Orders, "orders.poly");
        await Assert.That(language.Succeeded).IsTrue();
        await Assert.That(language.Domain!.Extensions).DoesNotContain("storage");
    }

    [Test]
    public async Task Compile_UnknownSeed_FailsClosed() {
        Assert.Throws<ArgumentException>(() => new PolyCompiler(ProductCatalog.Catalog, "nope"));
        await Assert.That(ExtensionCatalog.Seed("Authoring")).IsEqualTo(ExtensionCatalog.ProductAuthoring);
    }

    [Test]
    public async Task Compile_UnknownUses_HasEmptyPath() {
        var result = Compiler().Compile("domain D\nuses nope\n", "x.poly");
        var diagnostic = result.Diagnostics.Single(d => d.Code == DiagnosticCodes.UnknownExtension);
        await Assert.That(diagnostic.Path).IsEqualTo("");
        await Assert.That(diagnostic.Message).Contains("nope");
    }

    [Test]
    public async Task Compile_WiredCatalog_EmitsHostFilesForUses() {
        var sqlite = Compiler().Compile("domain Shop\nuses sqlite\n\nItem: entity {\n  Sku: Text unique\n}\n", "shop.poly");
        await Assert.That(sqlite.Succeeded).IsTrue();
        await Assert.That(sqlite.Files.Select(f => f.FileName)).Contains("ShopDbContext.cs");

        var http = Compiler().Compile(
            "domain Shop\nuses storage\nuses sqlite\nuses http\n\nItem: entity {\n  Sku: Text unique\n}\n",
            "shop.poly");
        await Assert.That(http.Succeeded).IsTrue();
        var names = http.Files.Select(f => f.FileName).ToList();
        await Assert.That(names).Contains("Program.cs");
        await Assert.That(names).Contains("demo.http");
        await Assert.That(names).Contains("ShopDbContext.cs");
    }

    [Test]
    public async Task Compile_UsesPosix_WindowsTarget_ReportsDmcon004() {
        var result = Compiler().Compile(
            "domain Shop\nuses posix\n\nOrder: entity { Total: Number }\n", "posix.poly", target: "windows");

        var error = result.Diagnostics.Single(d => d.IsError);
        await Assert.That(error.Code).IsEqualTo("DMCON004");
        await Assert.That(error.Message).Contains("windows");
        await Assert.That(result.Files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Compile_UsesPosix_PosixTarget_Succeeds() {
        var result = Compiler().Compile(
            "domain Shop\nuses posix\n\nOrder: entity { Total: Number }\n", "posix.poly", target: "posix");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Domain!.Extensions).Contains("posix");
    }

    [Test]
    public async Task Compile_UsesPosix_NoHostTarget_ReportsDmcon004() {
        var result = Compiler().Compile(
            "domain Shop\nuses posix\n\nOrder: entity { Total: Number }\n", "posix.poly");

        var error = result.Diagnostics.Single(d => d.IsError);
        await Assert.That(error.Code).IsEqualTo("DMCON004");
        await Assert.That(error.Message).Contains("no target");
    }

    [Test]
    public async Task Compile_DocumentUses_WinsOverSeed() {
        var result = Compiler().Compile("domain D\nuses sqlite\nA: entity {\n  X: Text\n}\n", "x.poly");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Domain!.Extensions).Contains("sqlite");
        await Assert.That(result.Domain!.Extensions).DoesNotContain("storage");
    }
}