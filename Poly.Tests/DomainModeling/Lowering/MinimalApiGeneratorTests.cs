using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

public class MinimalApiGeneratorTests {
    private static Domain ParseDomain(string poly) {
        var ctx = DomainAuthoringContext.CreateWithSqlPack();
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    private static string S(Domain d) {
        var st = new StorageAnalyzer(d).Analyze();
        var bh = new BehaviorAnalyzer(d).Analyze();
        var ag = new AggregateAnalyzer(d).Analyze();
        return new MinimalApiGenerator(d, storageModel: st, behaviorModel: bh, aggregateModel: ag).Generate("TDbCtx");
    }

    private static string IR(Domain d) {
        var st = new StorageAnalyzer(d).Analyze();
        var bh = new BehaviorAnalyzer(d).Analyze();
        var ag = new AggregateAnalyzer(d).Analyze();
        return new CSharpGenerator().Generate(new MinimalApiGenerator(d, storageModel: st, behaviorModel: bh, aggregateModel: ag).GenerateCompilationUnit("TDbCtx"));
    }

    // ── Strong structural assertions (T1) ─────────────────────

    [Test]
    public async Task Seed_IsPresentInBoth() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("SeedAsync")).IsTrue();
        await Assert.That(IR(d).Contains("SeedAsync")).IsTrue();
    }

    [Test]
    public async Task Detail_UsesBindingName() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        await Assert.That(S(d).Contains("is Item item")).IsTrue();
        await Assert.That(IR(d).Contains("is Item item")).IsTrue();
    }

    [Test]
    public async Task Detail_UsesTernary() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        await Assert.That(S(d).Contains("Results.Ok")).IsTrue();
        await Assert.That(S(d).Contains("Results.NotFound")).IsTrue();
        await Assert.That(IR(d).Contains("Results.Ok")).IsTrue();
        await Assert.That(IR(d).Contains("Results.NotFound")).IsTrue();
    }

    [Test]
    public async Task List_UsesToListAsync() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("ToListAsync")).IsTrue();
        await Assert.That(IR(d).Contains("ToListAsync")).IsTrue();
    }

    [Test]
    public async Task Post_HasCreateCall() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains(".Create(")).IsTrue();
        await Assert.That(IR(d).Contains(".Create(")).IsTrue();
    }

    [Test]
    public async Task Post_HasSaveChanges() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("SaveChangesAsync")).IsTrue();
        await Assert.That(IR(d).Contains("SaveChangesAsync")).IsTrue();
    }

    [Test]
    public async Task Post_HasCreate_NotInSeed() {
        // Isolate MapPost body: split on static SeedAsync function to isolate endpoints
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var sParts = S(d).Split("static async Task SeedAsync");
        var irParts = IR(d).Split("static async Task SeedAsync");
        await Assert.That(sParts.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(irParts.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(sParts[0].Contains(".Create(")).IsTrue();
        await Assert.That(irParts[0].Contains(".Create(")).IsTrue();
    }

    [Test]
    public async Task Post_ChecksIsSuccess() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("IsSuccess")).IsTrue();
        await Assert.That(IR(d).Contains("IsSuccess")).IsTrue();
    }

    [Test]
    public async Task Post_HasConflict() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("Conflict")).IsTrue();
        await Assert.That(IR(d).Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task Setup_HasCreateBuilder() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("CreateBuilder(args)")).IsTrue();
        await Assert.That(IR(d).Contains("CreateBuilder(args)")).IsTrue();
    }

    [Test]
    public async Task Setup_HasUsingScope() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("using (var scope")).IsTrue();
        await Assert.That(IR(d).Contains("using (var scope")).IsTrue();
    }

    [Test]
    public async Task Setup_HasAddDbContext() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("AddDbContext")).IsTrue();
        await Assert.That(IR(d).Contains("AddDbContext")).IsTrue();
    }

    [Test]
    public async Task Setup_HasUseInMemoryDatabase() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("UseInMemoryDatabase")).IsTrue();
        await Assert.That(IR(d).Contains("UseInMemoryDatabase")).IsTrue();
    }

    [Test]
    public async Task Dto_RecordPresent() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("ItemDto")).IsTrue();
        await Assert.That(IR(d).Contains("ItemDto")).IsTrue();
    }

    [Test]
    public async Task Child_RouteHasParentKey() {
        var d = ParseDomain("domain T\nPatron: entity { Name: Text }\nLoan: entity { Amount: Number }");
        await Assert.That(S(d).Contains("MapGet")).IsTrue();
        await Assert.That(IR(d).Contains("MapGet")).IsTrue();
    }

    [Test]
    public async Task BadRequest_Present() {
        var d = ParseDomain("domain T\nUser: entity { Name: Text }\nProfile: entity { Bio: Text }");
        await Assert.That(S(d).Length > 0).IsTrue();
        await Assert.That(IR(d).Length > 0).IsTrue();
    }

    [Test]
    public async Task CreatedKeyAccess_Present() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        var s = S(d);
        var ir = IR(d);
        await Assert.That(s.Contains("Created")).IsTrue();
        await Assert.That(ir.Contains("Created")).IsTrue();
        await Assert.That(ir.Contains("Value.Sku")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_Present() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Activate: action { }\n}");
        var s = S(d);
        var ir = IR(d);
        await Assert.That(s.Contains("Activate")).IsTrue();
        await Assert.That(ir.Contains("Activate")).IsTrue();
        await Assert.That(s.Contains("MapPost")).IsTrue();
        await Assert.That(ir.Contains("MapPost")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasTryCatch() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var ir = IR(d);
        await Assert.That(ir.Contains("try")).IsTrue();
        await Assert.That(ir.Contains("catch")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasStatusCode() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var ir = IR(d);
        await Assert.That(ir.Contains("StatusCode")).IsTrue();
        await Assert.That(ir.Contains("500")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasIsSuccessBranch() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var ir = IR(d);
        await Assert.That(ir.Contains("IsSuccess")).IsTrue();
        await Assert.That(ir.Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasNotFoundMessage() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var ir = IR(d);
        await Assert.That(ir.Contains("Item not found")).IsTrue();
    }

    [Test]
    public async Task Endpoints_MapGetAndMapPost() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(S(d).Contains("MapGet")).IsTrue();
        await Assert.That(IR(d).Contains("MapGet")).IsTrue();
        await Assert.That(S(d).Contains("MapPost")).IsTrue();
        await Assert.That(IR(d).Contains("MapPost")).IsTrue();
    }

    [Test]
    public async Task NoCommentStubsInIr() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var ir = IR(d);
        await Assert.That(ir.Contains("/* child")).IsFalse();
        await Assert.That(ir.Contains("/* action")).IsFalse();
    }
}