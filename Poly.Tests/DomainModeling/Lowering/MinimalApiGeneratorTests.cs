using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Tests.TestHelpers;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

public class MinimalApiGeneratorTests {
    private static string WithPersistence(string poly) {
        if (poly.Contains("uses persistence", StringComparison.Ordinal))
            return poly;
        var nl = poly.IndexOf('\n');
        return nl < 0 ? poly : poly[..(nl + 1)] + "uses persistence\n" + poly[(nl + 1)..];
    }

    private static Domain ParseDomain(string poly) {
        poly = WithPersistence(poly);
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

    /// <summary>Honest HTTP seed: Load(HttpLibrary), not CompileMode.All inventing a host.</summary>
    private static Compiler CompilerWithHttpHost() =>
        new Compiler().Load(new HttpLibrary());

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
        var parts = Render(d).Split("System.Threading.Tasks.Task SeedAsync");
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
        analysis = analysis.WithoutMetadata<EntityStructureMetadata>(item);
        var gen = new MinimalApiGenerator(d, analysis, storage, behavior, aggregate);

        var ex = Assert.Throws<InvalidOperationException>(() => gen.GenerateCompilationUnit("TDbCtx"));
        await Assert.That(ex!.Message).Contains("EntityStructureMetadata is required");
    }

    [Test]
    public async Task Seed_AsyncReturnType_IsFullyQualified() {
        // S26: a domain entity named `Task` shadows System.Threading.Tasks.Task in the
        // emitted host unless the seed's return type is qualified.
        var d = ParseDomain("""
            domain T
            Task: entity {
              Label: Text
            }
            """);

        await Assert.That(Render(d)).Contains("System.Threading.Tasks.Task SeedAsync");
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
        var rendered = Render(d with { Types = types });

        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string Status { get; init; }");
        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string @value { get; init; }");
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
        var session = DomainSession.Open(filled);
        session.Lower(filled, analysis);

        var contributor = new MinimalApiHostArtifactContributor();
        var files = contributor.Contribute(filled, analysis, session.ArtifactCatalog);
        var program = ContributedFile.Text(files.Single(f =>
            f.Descriptor.Id.Type == ContributedFile.Type && ContributedFile.FileName(f) == "Program.cs"));
        var http = ContributedFile.Text(files.Single(f =>
            f.Descriptor.Id.Type == ContributedFile.Type && ContributedFile.FileName(f) == "demo.http"));

        // Root routes exist.
        await Assert.That(program.Contains("/api/invoices")).IsTrue();
        await Assert.That(http.Contains("/api/invoices")).IsTrue();
        // The produced billing contract's Ledger entity never becomes a public route.
        await Assert.That(program.Contains("Ledger")).IsFalse();
        await Assert.That(http.Contains("ledgers")).IsFalse();
    }

    [Test]
    public async Task Compile_All_ParentWithProducedBillingContract_DoesNotEmitLedgerRoutes() {
        // pack-3c-2 end-to-end: Load(HttpLibrary) + CompileMode.All persistence seed;
        // a filled internal billing contract still yields composition-root-only output.
        var filled = new DomainSuite([BillingSource(), ParentWithBillingContract()])
            .FillInternalContracts(ParentWithBillingContract());
        var poly = new DomainDslPrinter().Print(filled);

        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
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

        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        var program = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("/api/orders/{number}/pay")).IsTrue();

        var order = result.Files!.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("BillingAdapters.Charge(request)");
    }

    [Test]
    public async Task SharedActionName_QualifiesDtosPerEntity() {
        // CRM seam: Log/AddLine exist on several entities AND on multiple stages of the
        // same entity (stage-scoped actions are listed once per declaring stage). A plain
        // {Action}Dto would emit duplicate type definitions in one Program.cs (CS0101);
        // the generator qualifies colliding action DTOs as {Entity}{Action}Dto, emits
        // one per (entity, action), and each endpoint binds its own.
        var d = ParseDomain("""
            domain T
            Quote: entity {
              Log: action (subject: Text) { }
            }
            Order: entity {
              Log: action (subject: Text) { }
            }
            Opportunity: entity {
              Draft: stage {
                Log: action (subject: Text) { }
                AddLine: action (sku: Text, qty: Number) { }
              }
              Open: stage {
                Log: action (subject: Text) { }
                AddLine: action (sku: Text, qty: Number) { }
              }
            }
            """);
        var unit = IrUnit(d);
        await Assert.That(unit.FindType("QuoteLogDto")).IsNotNull();
        await Assert.That(unit.FindType("OrderLogDto")).IsNotNull();
        await Assert.That(unit.FindType("OpportunityLogDto")).IsNotNull();
        await Assert.That(unit.FindType("OpportunityAddLineDto")).IsNotNull();
        await Assert.That(unit.FindType("LogDto")).IsNull();
        await Assert.That(unit.FindType("AddLineDto")).IsNull();
        // One DTO per (entity, action) — stage-scoped duplicates collapse.
        var types = unit.Types.Select(t => t.Name).ToList();
        await Assert.That(types.Count(n => n == "OpportunityLogDto")).IsEqualTo(1);
        await Assert.That(types.Count(n => n == "OpportunityAddLineDto")).IsEqualTo(1);
    }

    [Test]
    public async Task StageScopedAction_EmitsOneRoute_AndChildGetUsesRouteId() {
        var d = ParseDomain("""
            domain T
            Book: entity { Isbn: Text unique }
            Patron: entity {
              Email: Text unique
              loans: many Loan
            }
            Loan: entity {
              book: Book
              Active: stage { Return: action { } }
              Overdue: stage { Return: action { } }
            }
            """);
        var rendered = Render(d);
        const string route = "\"/api/patrons/{email}/loans/{id}/return\"";
        await Assert.That(CountOf(rendered, route)).IsEqualTo(1);
        await Assert.That(rendered).Contains("\"/api/patrons/{email}\"");
        await Assert.That(rendered).Contains("\"/api/books/{isbn}\"");
        await Assert.That(rendered).Contains("\"/api/patrons/{email}/loans/{id}\"");
        await Assert.That(rendered.Contains("FirstOrDefault()")).IsFalse();
        await Assert.That(rendered).Contains("FindAsync(id)");
        await Assert.That(rendered.Contains("FindAsync(isbn)")).IsFalse();
        await Assert.That(rendered.Contains("FindAsync(email)")).IsFalse();
        await Assert.That(rendered).Contains("e.Isbn == isbn");
        await Assert.That(rendered).Contains("e.Email == email");
    }

    [Test]
    public async Task ChildGet_NaturalKey_FiltersTheParentsCollection() {
        var d = ParseDomain("""
            domain T
            Book: entity { Isbn: Text unique }
            Patron: entity {
              Email: Text unique
              fines: many Fine
            }
            Fine: entity {
              Code: Text unique
              book: Book
            }
            """);
        var rendered = Render(d);
        await Assert.That(rendered).Contains("\"/api/patrons/{email}/fines/{code}\"");
        await Assert.That(rendered).Contains("e.Code == code");
        await Assert.That(rendered.Contains("Fines.FirstOrDefault()")).IsFalse();
    }

    [Test]
    public async Task ProgramEntity_DbAndHttp_DoNotCollideWithHostProgram() {
        const string poly = """
            domain Registration
            Program: entity {
              Code: Text unique
              Name: Text required
            }
            Student: entity {
              Email: Text unique
              program: Program
            }
            """;

        var db = new Compiler().Compile(poly, CompileMode.Db, DbmsPack.Sqlite);
        await Assert.That(db.Success).IsTrue();
        await Assert.That(db.Files!.Any(f => f.FileName == "Program.cs")).IsFalse();
        var entity = db.Files!.Single(f => f.FileName == "Program.Entity.cs").Source;
        await Assert.That(entity).Contains("namespace Poly.Generated");
        await Assert.That(entity).Contains("class Program");
        await Assert.That(db.Files!.Single(f => f.FileName == "Student.cs").Source)
            .Contains("namespace Poly.Generated");

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(http.Success).IsTrue();
        await Assert.That(http.Files!.Count(f => f.FileName == "Program.cs")).IsEqualTo(1);
        await Assert.That(http.Files!.Any(f => f.FileName == "Program.Entity.cs")).IsTrue();
        var host = http.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(host).Contains("Poly.Generated.Program");

        var errors = CompileHost(http.Files!);
        await Assert.That(errors).IsEmpty();
    }

    private static int CountOf(string text, string value) {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string[] CompileHost(IReadOnlyList<(string FileName, string Source)> files) {
        var implicitUsings = CSharpSyntaxTree.ParseText("""
            global using System;
            global using System.Linq;
            global using System.Threading.Tasks;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Net.Http;
            global using Microsoft.AspNetCore.Builder;
            global using Microsoft.AspNetCore.Hosting;
            global using Microsoft.AspNetCore.Http;
            global using Microsoft.AspNetCore.Routing;
            global using Microsoft.Extensions.Configuration;
            global using Microsoft.Extensions.DependencyInjection;
            global using Microsoft.Extensions.Hosting;
            global using Microsoft.Extensions.Logging;
            """, path: "ImplicitUsings.cs");
        var trees = files
            .Where(f => f.FileName.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.FileName))
            .Append(implicitUsings);
        var compilation = CSharpCompilation.Create(
            "ProgramEntityHost",
            trees,
            HostReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Location.SourceTree?.FilePath}: {d}")
            .ToArray();
    }

    private static List<MetadataReference> HostReferences() {
        var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        void AddFrom(string path) {
            var name = Path.GetFileName(path);
            if (!byName.ContainsKey(name) && File.Exists(path))
                byName[name] = MetadataReference.CreateFromFile(path);
        }
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (tpa is not null)
            foreach (var p in tpa.Split(Path.PathSeparator))
                AddFrom(p);
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var sharedDir = coreDir is not null ? Path.GetDirectoryName(Path.GetDirectoryName(coreDir)) : null;
        var aspNetRoot = sharedDir is not null ? Path.Combine(sharedDir, "Microsoft.AspNetCore.App") : null;
        if (aspNetRoot is not null && Directory.Exists(aspNetRoot) && coreDir is not null) {
            var version = Path.GetFileName(coreDir);
            var aspNetDir = Directory.Exists(Path.Combine(aspNetRoot, version))
                ? Path.Combine(aspNetRoot, version)
                : Directory.GetDirectories(aspNetRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
            if (aspNetDir is not null)
                foreach (var p in Directory.GetFiles(aspNetDir, "*.dll"))
                    AddFrom(p);
        }
        foreach (var p in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            AddFrom(p);
        return byName.Values.ToList();
    }
}