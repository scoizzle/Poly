using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

public class MinimalApiGeneratorTests {
    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    /// <summary>Returns the rendered C# from the IR-backed MinimalApi generator.</summary>
    private static string Render(Domain d) =>
        GenerationAssertions.MinimalApiIr(d).Render();

    /// <summary>Returns the IR CompilationUnitNode for structural assertions.</summary>
    private static CompilationUnitNode IrUnit(Domain d) =>
        GenerationAssertions.MinimalApiIr(d);

    // ── Structural IR assertions (preferred) ─────────────────

    [Test]
    public async Task Endpoints_MapGetAndMapPost() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("MapGet")).IsTrue();
        await Assert.That(names.Contains("MapPost")).IsTrue();
    }

    [Test]
    public async Task Setup_HasCreateBuilder() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("CreateBuilder")).IsTrue();
    }

    [Test]
    public async Task Setup_HasAddDbContext() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("AddDbContext")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_Present() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Activate: action { }\n}");
        var unit = IrUnit(d);
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Activate")).IsTrue();
        await Assert.That(rendered.Contains("MapPost")).IsTrue();
    }

    [Test]
    public async Task Child_RouteHasMapGet() {
        var d = ParseDomain("domain T\nPatron: entity { Name: Text }\nLoan: entity { Amount: Number }");
        var unit = IrUnit(d);
        var names = unit.TopLevelInvocationNames();
        await Assert.That(names.Contains("MapGet")).IsTrue();
    }

    // ── Rendered-string assertions (where structural is overkill) ──

    [Test]
    public async Task Seed_IsPresent() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("SeedAsync")).IsTrue();
    }

    [Test]
    public async Task Detail_UsesBindingName() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        await Assert.That(Render(d).Contains("is Item item")).IsTrue();
    }

    [Test]
    public async Task Detail_UsesTernary() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Results.Ok")).IsTrue();
        await Assert.That(rendered.Contains("Results.NotFound")).IsTrue();
    }

    [Test]
    public async Task List_UsesToListAsync() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("ToListAsync")).IsTrue();
    }

    [Test]
    public async Task Post_HasCreateCall() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains(".Create(")).IsTrue();
    }

    [Test]
    public async Task Post_HasSaveChanges() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("SaveChangesAsync")).IsTrue();
    }

    [Test]
    public async Task Post_HasCreate_NotInSeed() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var parts = Render(d).Split("static async Task SeedAsync");
        await Assert.That(parts.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(parts[0].Contains(".Create(")).IsTrue();
    }

    [Test]
    public async Task Post_ChecksIsSuccess() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("IsSuccess")).IsTrue();
    }

    [Test]
    public async Task Post_HasConflict() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task Setup_HasUsingScope() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("using (var scope")).IsTrue();
    }

    [Test]
    public async Task Setup_HasUseInMemoryDatabase() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("UseInMemoryDatabase")).IsTrue();
    }

    [Test]
    public async Task Dto_RecordPresent() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("ItemDto")).IsTrue();
    }

    [Test]
    public async Task BadRequest_Present() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task CreatedKeyAccess_Present() {
        var d = ParseDomain("domain T\nItem: entity { Sku: Text unique }");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("Created")).IsTrue();
        await Assert.That(rendered.Contains("Value.Sku")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasTryCatch() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("try")).IsTrue();
        await Assert.That(rendered.Contains("catch")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasStatusCode() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("StatusCode")).IsTrue();
        await Assert.That(rendered.Contains("500")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasIsSuccessBranch() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("IsSuccess")).IsTrue();
        await Assert.That(rendered.Contains("Conflict")).IsTrue();
    }

    [Test]
    public async Task ActionEndpoint_HasNotFoundMessage() {
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        await Assert.That(Render(d).Contains("Item not found")).IsTrue();
    }

    [Test]
    public async Task NoCommentStubsInIr() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var rendered = Render(d);
        await Assert.That(rendered.Contains("/* child")).IsFalse();
        await Assert.That(rendered.Contains("/* action")).IsFalse();
    }

    [Test]
    public async Task Create_MissingEntityStructureMetadata_Throws() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        var analysis = DomainModelAnalyzer.Analyze(d);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(d)!.Storage;
        var behavior = BehaviorMetadata.From(d, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(d)!.Aggregate;
        var item = d.Types.OfType<Entity>().First();
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>(item);
        var gen = new MinimalApiGenerator(d, analysis, storage, behavior, aggregate);

        var ex = Assert.Throws<InvalidOperationException>(() => gen.GenerateCompilationUnit("TDbCtx"));
        await Assert.That(ex!.Message).Contains("EntityStructureMetadata is required");
    }

    [Test]
    public async Task ValueSetUnion_PropagatesAllowedValuesToDtos() {
        // Transport: equals(v) value-set constraints propagate as [AllowedValues] — the
        // member must equal the pinned value. On the entity create DTO from the declared
        // constraint; on the action DTO implicitly from `assign Status to value`.
        var d = ParseDomain("""
            domain T
            Item: entity {
              Status: Text
              SetStatus: action (value: Text) {
                assign Status to value
              }
            }
            """);
        // The DSL does not author equals(...) value-set constraints, so inject the
        // model-level EqualityConstraint directly — the transport must still propagate it.
        var item = d.Types.OfType<Entity>().First();
        item = item with {
            Properties = item.Properties.Select(p => p.Name == "Status"
                ? new Property("Status", p.Type, [new EqualityConstraint("Active")])
                : p).ToList()
        };
        var types = d.Types.Select(t => ReferenceEquals(t, d.Types.OfType<Entity>().First()) ? item : t).ToList();
        var rendered = Render(new Domain(d.Name, types));

        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string Status { get; init; }");
        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string value { get; init; }");
    }

    [Test]
    public async Task ChildAction_Parameterized_DeclaresDto() {
        var d = ParseDomain("""
            domain T
            Warehouse: entity {
              Code: Text unique
              trucks: many Truck
            }
            Truck: entity {
              Vin: Text unique
              warehouse: Warehouse
              Load: action (weight: Number) {
                assign Vin to Vin
              }
            }
            """);
        var rendered = Render(d);
        await Assert.That(rendered).Contains("LoadDto dto");
        await Assert.That(rendered).Contains("dto.weight");
    }

    [Test]
    public async Task ChildRoute_DuplicateKeyNames_Disambiguates() {
        var d = ParseDomain("""
            domain T
            Doctor: entity {
              Name: Text
              visits: many Visit
            }
            Visit: entity {
              Notes: Text
              doctor: Doctor
              Record: action (notes: Text) {
                assign Notes to notes
              }
            }
            """);
        var rendered = Render(d);
        await Assert.That(rendered.Contains("{id}/{id}")).IsFalse();
        await Assert.That(rendered).Contains("visitId");
    }

    [Test]
    public async Task ToOneChild_UsesReferenceNotCollection() {
        var d = ParseDomain("""
            domain T
            Doctor: entity {
              Name: Text unique
              schedule: one Schedule
            }
            Schedule: entity {
              Day: Text
              doctor: Doctor
              Open: action {
                assign Day to Day
              }
            }
            """);
        var rendered = Render(d);
        await Assert.That(rendered).Contains("Reference(");
        await Assert.That(rendered.Contains("Collection(e => e.Schedule)")).IsFalse();
    }

    /// <summary>A billing domain whose Ledger entity must never surface as a public route
    /// when consumed as a produced <c>contract internal</c> (pack-3b producer).</summary>
    private static Domain BillingSource() =>
        DomainFactory.Create("billing", b => b
            .AddValueType("ChargeRequest",
                new Property("Amount", new DomainTypeReference("Number"), []),
                new Property("Currency", new DomainTypeReference("Text"), []))
            .AddEntity("Ledger")
            .AddActionWithParameters("Ledger", "Charge",
                new Property("request", new DomainTypeReference("ChargeRequest"), [])));

    /// <summary>Parent domain with a root entity and a declared internal billing contract.</summary>
    private static Domain ParentWithBillingContract() => ParseDomain("""
        domain Parent
        Invoice: entity {
          Number: Text unique
        }
        Billing: contract internal billing v1 {}
        """);

    [Test]
    public async Task HostContributor_ParentWithProducedBillingContract_EmitsCompositionRootOnly() {
        // pack-3c-2: Program.cs + demo.http flow through the artifact-contributor hook and
        // emit the composition root only. A produced internal billing contract contributes
        // value types + operation endpoints — never Ledger routes.
        var filled = new DomainSuite([BillingSource(), ParentWithBillingContract()])
            .FillInternalContracts(ParentWithBillingContract());

        var analysis = DomainModelAnalyzer.Analyze(filled);

        var contributor = new MinimalApiHostArtifactContributor();
        var files = contributor.Contribute(filled, analysis);
        var program = files.Single(f => f.FileName == "Program.cs").Source;
        var http = files.Single(f => f.FileName == "demo.http").Source;

        // Root routes exist.
        await Assert.That(program.Contains("/api/invoices")).IsTrue();
        await Assert.That(http.Contains("/api/invoices")).IsTrue();
        // The produced billing contract's Ledger entity never becomes a public route.
        await Assert.That(program.Contains("Ledger")).IsFalse();
        await Assert.That(http.Contains("ledgers")).IsFalse();
    }

    [Test]
    public async Task Compile_All_ParentWithProducedBillingContract_DoesNotEmitLedgerRoutes() {
        // pack-3c-2 end-to-end: CompileMode.All pulls Program.cs + demo.http through the
        // hook; a filled internal billing contract still yields composition-root-only output.
        var filled = new DomainSuite([BillingSource(), ParentWithBillingContract()])
            .FillInternalContracts(ParentWithBillingContract());
        var poly = new DomainDslPrinter().Print(filled);

        var result = new Compiler().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        var program = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("/api/invoices")).IsTrue();
        await Assert.That(program.Contains("/api/ledgers")).IsFalse();
    }

    /// <summary>Parent Shop domain whose root Order.Pay is bound to the produced Billing.Charge
    /// endpoint (pack-3c-3). Built after the contract fill so the action can reference the contract
    /// value type; Pay carries no local effects.</summary>
    private static Domain ParentWithBillingBind() {
        var baseParent = ParseDomain("""
            domain Shop
            Order: entity {
              Number: Text unique
            }
            Billing: contract internal billing v1 {}
            """);
        var filled = new DomainSuite([BillingSource(), baseParent])
            .FillInternalContracts(baseParent);
        var result = new DomainEvolution(filled).Evolve()
            .AddActionWithParameters("Order", "Pay",
                new Property("request", new DomainTypeReference("ChargeRequest"), []))
            .AddContractBinding("ChargeOrder", "Billing", "Charge", "Pay", "request")
            .Apply();
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }
    [Test]
    public async Task Compile_All_BoundActionHandler_CallsThroughBinding() {
        // pack-3c-3: the API handler for a bound root action goes through the binding.
        // The handler calls entity.Pay(...), whose generated body invokes the emitted
        // Billing adapter (fail-closed) — the binding is never dropped by export.
        var filled = new DomainSuite([BillingSource(), ParentWithBillingBind()])
            .FillInternalContracts(ParentWithBillingBind());
        var poly = new DomainDslPrinter().Print(filled);

        var result = new Compiler().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        var program = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("/api/orders/{number}/pay")).IsTrue();

        var order = result.Files!.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("BillingAdapters.Charge(request)");
    }
}