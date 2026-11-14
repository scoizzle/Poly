using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

public partial class MinimalApiGeneratorTests {
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
        var result = new DomainEvolution(emptyDomain).ApplyAndAnalyze(changes);
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
        await Assert.That(Render(d).Contains("is T.Item item")).IsTrue();
    }

    [Test]
    public async Task List_UsesToListAsync() {
        var d = ParseDomain("domain T\nItem: entity { Name: Text }");
        await Assert.That(Render(d).Contains("ToListAsync")).IsTrue();
    }

    [Test]
    public async Task List_Route_EnglishPlural() {
        // S19: the route segment pluralizes with English rules, not name + "s".
        var d = ParseDomain("""
            domain Col
            Address: entity {
              Line1: Text
              City: Text
            }
            """);
        var rendered = Render(d);
        await Assert.That(rendered).Contains("\"/api/addresses\"");
        await Assert.That(rendered.Contains("addresss")).IsFalse();
        await Assert.That(rendered).Contains("db.Addresses");
    }

    [Test]
    [Arguments("Address", "addresses")]
    [Arguments("Box", "boxes")]
    [Arguments("Blitz", "blitzes")]
    [Arguments("Batch", "batches")]
    [Arguments("Brush", "brushes")]
    [Arguments("Company", "companies")]
    [Arguments("Key", "keys")]
    [Arguments("Person", "people")]
    [Arguments("Orders", "orders")]
    public async Task List_Route_PluralizesByShape(string entity, string segment) {
        var d = ParseDomain($"domain T\n{entity}: entity {{ Name: Text }}");
        await Assert.That(Render(d)).Contains($"\"/api/{segment}\"");
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
    public async Task ActionEndpoint_CatchAllLogsTheExceptionAndAnswersProblem500() {
        // Text oracle: no fixture action can be made to throw at run time (the module reports every domain
        // failure as a result, and the unique registry already catches a duplicate key), so the catch-all
        // is pinned on the generated text. The 500 body carries no exception text; the log does.
        var d = ParseDomain("domain T\nItem: entity {\n  Name: Text\n  Freeze: action { }\n}");
        var rendered = Render(d);
        await Assert.That(rendered).Contains("catch (Exception ex)");
        await Assert.That(rendered).Contains("app.Logger.LogError(ex, \"Action {Action} failed\", \"Item.Freeze\");");
        await Assert.That(rendered).Contains("StatusProblem(500, \"Internal Server Error\")");
    }

    /// <summary>P-03 (A4): every action handler saves inside its <c>if (result.IsSuccess)</c> true-branch
    /// and nowhere else, and each action handler saves exactly once. The domain carries one non-void
    /// (Patron.CheckOut -> Loan) and one void (Loan.Return) action: no booting oracle fixture exercises the
    /// non-void arm, so this structural check is its only automated, PR-tier oracle.</summary>
    [Test]
    public async Task ActionHandlers_SaveOnlyInsideSuccessBranch() {
        var d = ParseDomain("""
            domain T
            Patron: entity {
              Email: Text unique
              loans: many Loan
              CheckOut: action -> Loan { create in loans { DueDate: 30 } }
            }
            Loan: entity {
              DueDate: Number
              borrower: Patron
              Return: action { }
            }
            """);
        var root = CSharpSyntaxTree.ParseText(Render(d)).GetRoot();

        // Action routes are the MapPost routes below a route key: /api/x/{k}/action (create is /api/x).
        var actionHandlers = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "MapPost" })
            .Where(i => i.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.ValueText.Split('/').Length >= 4)
            .ToList();
        await Assert.That(actionHandlers.Count).IsEqualTo(2);

        var saves = new List<InvocationExpressionSyntax>();
        foreach (var handler in actionHandlers) {
            var handlerSaves = handler.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(i => i.Expression.ToString().EndsWith(".SaveChangesAsync", StringComparison.Ordinal))
                .ToList();
            await Assert.That(handlerSaves.Count).IsEqualTo(1);
            var save = handlerSaves[0];
            var branch = save.FirstAncestorOrSelf<IfStatementSyntax>();
            await Assert.That(branch).IsNotNull();
            await Assert.That(branch!.Condition.ToString()).Contains("IsSuccess");
            await Assert.That(branch.Statement.Span.Contains(save.Span)).IsTrue();
            saves.Add(save);
        }

        // A handler's save count equals the number of action handlers: none escapes a handler.
        await Assert.That(saves.Count).IsEqualTo(actionHandlers.Count);
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

        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string? Status { get; init; }");
        await Assert.That(rendered).Contains("[AllowedValues(\"Active\")]\n    public string Value { get; init; }");
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
        await Assert.That(rendered).Contains("dto.Weight");
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
              schedule: one Schedule optional
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
    private static Domain BillingSource() => ParseDomain("""
        domain billing

        public value ChargeRequest {
          Amount: Number
          Currency: Text
        }

        public entity Ledger {
          public action Charge (request: ChargeRequest) {
          }
        }
        """);

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
        // #421: the API handler still routes the bound root action. The surface-only
        // sibling bind erases the call, so the handler calls the local action with no
        // adapter invoke and the binding is not dropped from the graph.
        var filled = new DomainSuite([BillingSource(), ParentWithBillingBind()])
            .FillInternalContracts(ParentWithBillingBind());
        var poly = new DomainDslPrinter().Print(filled);

        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        var program = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("/api/orders/{number}/pay")).IsTrue();

        var order = result.Files!.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(order).Contains("internal DomainResult Pay(ChargeRequest request)");
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
              Name: Text
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
        await Assert.That(entity).Contains("namespace Registration;");
        await Assert.That(entity).Contains("class Program");
        await Assert.That(db.Files!.Single(f => f.FileName == "Registration" + "DbContext.cs").Source)
            .Contains("namespace Registration;");
        await Assert.That(db.Files!.Single(f => f.FileName == "Student.cs").Source)
            .Contains("namespace Registration;");

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(http.Success).IsTrue();
        await Assert.That(http.Files!.Count(f => f.FileName == "Program.cs")).IsEqualTo(1);
        await Assert.That(http.Files!.Any(f => f.FileName == "Program.Entity.cs")).IsTrue();
        var host = http.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(host).Contains("using Registration;");
        await Assert.That(host).Contains("Registration.Program");
        await Assert.That(host).DoesNotContain("Poly.Generated");

        var errors = CompileHost(http.Files!);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task EntityNamedLikeItsDomain_HostQualifiesItAndCompiles() {
        // A bare `Library` in the host (global scope, `using Library;`) binds to the namespace, not the entity.
        const string poly = """
            domain Library
            Library: entity {
              Code: Text unique
              Name: Text
            }
            Shelf: entity {
              Label: Text unique
              library: Library
            }
            """;

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(http.Success).IsTrue();
        var host = http.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(host).Contains("using Library;");
        await Assert.That(host).Contains("Library.Library");
        var errors = CompileHost(http.Files!);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task EntityNamedLikeAnImportedBclType_HostQualifiesItAndCompiles() {
        // The host imports System.Threading.Tasks and the domain namespace: a bare `Task` is ambiguous (CS0104).
        // Red tail from the Desk658 oracle fixture before the fix: "'Task' is an ambiguous reference between
        // 'Desk.Task' and 'System.Threading.Tasks.Task'".
        const string poly = """
            domain Desk
            Project: entity {
              Key: Text unique
              tasks: many Task
            }
            Task: entity {
              Title: Text unique
            }
            """;

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(http.Success).IsTrue();
        await Assert.That(http.Files!.Single(f => f.FileName == "Program.cs").Source).Contains("Desk.Task");
        await Assert.That(CompileHost(http.Files!)).IsEmpty();
    }

    [Test]
    public async Task EnumNamedLikeAnImportedBclType_HostQualifiesEveryReferenceAndCompiles() {
        // typeof(DayOfWeek) in [EnumDataType] and the seed DayOfWeek.Mon must be qualified like the property type.
        const string poly = """
            domain Roster
            DayOfWeek: enum { Mon, Tue }
            Shift: entity {
              Code: Text unique
              Day: DayOfWeek
            }
            """;

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(http.Success).IsTrue();
        var host = http.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(host).Contains("typeof(Roster.DayOfWeek)");
        await Assert.That(host).Contains("Roster.DayOfWeek.Mon");
        await Assert.That(CompileHost(http.Files!)).IsEmpty();
    }

    [Test]
    public async Task EnumNamedLikeAKeyword_NullableDtoMemberKeepsItsEscapeAndCompiles() {
        // L-08b half 2 fix round: the nullable DTO member must print Kw.@event?, not Kw.event?.
        const string poly = """
            domain Kw
            event: enum { A, B }
            Thing: entity {
              Name: Text unique
              Kind: event
            }
            """;

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(http.Success).IsTrue();
        var host = http.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(host).Contains("Kw.@event? Kind");
        await Assert.That(CompileHost(http.Files!)).IsEmpty();
    }

    /// <summary>E-10a (#924): the hosts' own names reach C# through the expression position too.</summary>
    [Test]
    [Arguments("event: enum { A, B }\n            Thing: entity {\n              Name: Text unique\n              Kind: event\n            }")]
    [Arguments("class: entity {\n              Name: Text unique\n            }")]
    [Arguments("class: entity {\n              Name: Text unique\n              things: many Thing\n            }\n            Thing: entity {\n              Name: Text unique\n              owner: class\n            }")]
    public async Task KeywordNamedTypes_HostAndPlainExportCompile(string body) {
        var poly = "domain Kw\n            uses sqlite\n            " + body;

        var db = new Compiler().Compile(poly, CompileMode.Db, DbmsPack.Sqlite);
        await Assert.That(db.Success).IsTrue();
        await Assert.That(CompileHost(db.Files!).Where(e => !e.Contains("CS5001", StringComparison.Ordinal))).IsEmpty();

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(http.Success).IsTrue();
        await Assert.That(CompileHost(http.Files!)).IsEmpty();
    }

    [Test]
    public async Task KeywordNamedEnum_SimulatorAndGeneratedCSharpAgreeOnACreateAndAnAction() {
        var scenario = ParityScenario.FromDsl("""
            domain Kw
            uses persistence
            event: enum { A, B }
            Thing: entity {
              Name: Text unique
              Kind: event
              Flip: action { assign Kind to "B" }
            }
            """, "KeywordEnumParity");

        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Thing", ("Name", "a"), ("Kind", "A"));
            side.Invoke("Flip");
        });

        await Assert.That(outcomes.Select(o => o.Success)).IsEquivalentTo([true, true]);
        await Assert.That(outcomes[1].State["Kind"]).IsEqualTo("B");
    }

    [Test]
    public async Task KeywordNamedEntity_SimulatorAndGeneratedCSharpAgreeOnACreateAndAnAction() {
        var scenario = ParityScenario.FromDsl("""
            domain Kw
            uses persistence
            class: entity {
              Name: Text unique
              Done: Boolean default(false)
              Finish: action { assign Done to true }
            }
            """, "KeywordEntityParity");

        var outcomes = await scenario.AssertAgree(side => {
            side.Create("class", ("Name", "a"));
            side.Invoke("Finish");
        });

        await Assert.That(outcomes.Select(o => o.Success)).IsEquivalentTo([true, true]);
        await Assert.That(outcomes[1].State["Done"]).IsEqualTo("True");
    }

    /// <summary>E-10a (#923): a primitive type of a library (the temporal <c>DateTime</c>) is not a generated
    /// domain type, so the host never namespace-qualifies it.</summary>
    [Test]
    public async Task RequiredTemporalMembers_HostCompiles() {
        const string poly = """
            domain Shop
            uses temporal
            uses sqlite
            uses http
            Thing: entity {
              Name: Text unique
              Dt: DateTime
              Tm: Time
              Du: Duration
            }
            """;

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(http.Success).IsTrue();
        await Assert.That(http.Files!.Single(f => f.FileName == "Program.cs").Source).DoesNotContain("Shop.DateTime");
        await Assert.That(CompileHost(http.Files!)).IsEmpty();
    }

    [Test]
    public async Task ActionParameterOfDateTime_HostCompiles() {
        const string poly = """
            domain Shop
            uses temporal
            uses sqlite
            uses http
            Thing: entity {
              Name: Text unique
              Book: action (at: DateTime) { }
            }
            """;

        var http = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);

        await Assert.That(http.Success).IsTrue();
        await Assert.That(CompileHost(http.Files!)).IsEmpty();
    }

    /// <summary>An entity whose create takes an entity-typed property gets no POST route and no DTO, not a route
    /// that always answers 400. The DSL cannot declare that property (a defaulted or optional one), so an
    /// API-built model is the only oracle there is; there is no running-app test for it.</summary>
    [Test]
    public async Task EntityWithEntityTypedProperty_HasNoPostRoute() {
        var domain = new Domain("T", [
            new Entity("Other", [new Property("Name", new DomainTypeReference("Text"), [])], [], [], []),
            new Entity("Item", [
                new Property("Sku", new DomainTypeReference("Text"), []),
                new Property("Peer", new DomainTypeReference("Other"), [new DefaultValueConstraint(new Literal(1))]),
            ], [], [], []),
        ]) { Extensions = ["persistence"] };

        var rendered = Render(domain);

        // Controls: the entity next to it has its POST route and DTO, and the entity itself is listed.
        await Assert.That(rendered).Contains("MapPost(\"/api/others\"");
        await Assert.That(rendered).Contains("OtherDto");
        await Assert.That(rendered).Contains("MapGet(\"/api/items\"");
        await Assert.That(rendered).DoesNotContain("MapPost(\"/api/items\"");
        await Assert.That(rendered).DoesNotContain("ItemDto");
    }

    /// <summary>L-15a (#683): an entity with a required link and no parent is an aggregate root, but its
    /// <c>Create</c> answers Required whenever the link is absent, and a POST body cannot carry one. So it gets
    /// no POST route, no create DTO and no seed row; the entity next to it keeps all three.</summary>
    [Test]
    public async Task EntityWithRequiredLink_HasNoPostRouteAndNoSeedRow() {
        var rendered = Render(ParseDomain("""
            domain Shop
            Customer: entity { Name: Text }
            Order: entity {
              Number: Text
              customer: Customer
            }
            """));

        await Assert.That(rendered).Contains("MapPost(\"/api/customers\"");
        await Assert.That(rendered).Contains("MapGet(\"/api/orders\"");
        await Assert.That(rendered).DoesNotContain("MapPost(\"/api/orders\"");
        await Assert.That(rendered).DoesNotContain("OrderDto");
        await Assert.That(rendered).Contains("Shop.Customer.Create(\"Sample\")");
        await Assert.That(rendered).DoesNotContain("Shop.Order.Create(");
    }

    [Test]
    public async Task GeneratedHost_ParsesNoFailureText() {
        var rendered = Render(ParseDomain("""
            domain T
            Item: entity { Name: Text }
            """));
        // Positive control: the typed path is present.
        await Assert.That(rendered).Contains("DomainFailureStatus.Http");
        await Assert.That(rendered).Contains("Results.Problem");
        // No failure text is read: not the old matching strings, not the old status function, not the shim.
        await Assert.That(rendered).DoesNotContain("Unique constraint");
        await Assert.That(rendered).DoesNotContain("already has a target");
        await Assert.That(rendered).DoesNotContain("FailureStatus(");
        await Assert.That(rendered).DoesNotContain("ErrorMessage");
        await Assert.That(rendered).DoesNotContain("Results.BadRequest");
        await Assert.That(rendered).DoesNotContain("Results.NotFound");
        await Assert.That(rendered).DoesNotContain("Results.Conflict");
    }

    [Test]
    public async Task ActionDto_PropertiesArePascalCase() {
        var rendered = Render(ParseDomain("""
            domain T
            Lead: entity { Email: Text unique }
            Account: entity {
              Name: Text
              PlaceHq: action (city: Text, region: Text) { }
              ConvertLead: action (lead: Lead) { }
            }
            """));
        await Assert.That(rendered).Contains("public string City");
        await Assert.That(rendered).Contains("public string Region");
        await Assert.That(rendered).Contains("public string LeadId");
        await Assert.That(rendered).Contains("dto.City");
        await Assert.That(rendered).Contains("dto.LeadId");
        await Assert.That(rendered.Contains("public string city")).IsFalse();
        await Assert.That(rendered.Contains("dto.leadId")).IsFalse();
    }

    [Test]
    public async Task CreatedUri_DropsToStringOnStringKey() {
        var rendered = Render(ParseDomain("""
            domain T
            Item: entity { Sku: Text unique }
            """));
        await Assert.That(rendered).Contains("Value.Sku");
        await Assert.That(rendered.Contains("Sku.ToString()")).IsFalse();
    }

    [Test]
    public async Task CreatedUri_FormatsNonStringKeyInvariantlyAndEscaped() {
        var rendered = Render(ParseDomain("""
            domain T
            Item: entity { Code: Number unique }
            """));
        await Assert.That(rendered).Contains(
            "System.Uri.EscapeDataString(System.Convert.ToString(itemResult.Value.Code, System.Globalization.CultureInfo.InvariantCulture))");
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

    // ── S-decl: the schema id in the generated host ─────────────────

    private const string CounterHeader = "domain Counter\nuses http\nuses sqlite\n";

    private const string CounterBody = """
        Tally: entity {
          Name: Text unique length(1, 20)
          N: Number default(0) range(0, 1000000)
          Bump: action { assign N to N + 1 }
        }
        """;

    private static string HostProgram(string poly) {
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(poly, "Counter.poly", CancellationToken.None);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return result.Files.Single(f => f.FileName == "Program.cs").Source;
    }

    private static int? SchemaIdOf(string poly) {
        var match = System.Text.RegularExpressions.Regex.Match(HostProgram(poly), @"var schemaId = (\d+);");
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    [Test]
    public async Task SchemaId_TwoGenerations_AreIdentical_AndPositive() {
        var first = SchemaIdOf(CounterHeader + CounterBody);
        var second = SchemaIdOf(CounterHeader + CounterBody);

        await Assert.That(first).IsNotNull();
        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first!.Value).IsGreaterThan(0);
    }

    [Test]
    [Arguments("a required property is added", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Label: Text length(1, 10)\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("a property is renamed", "Tally: entity {\n  Name: Text unique length(1, 20)\n  Count: Number default(0) range(0, 1000000)\n  Bump: action { assign Count to Count + 1 }\n}")]
    [Arguments("an entity is added", CounterBody + "\nOther: entity {\n  Code: Text length(1, 5)\n}")]
    [Arguments("a Number becomes optional", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number optional range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("a unique index is dropped", "Tally: entity {\n  Name: Text length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("a length bound changes", "Tally: entity {\n  Name: Text unique length(1, 30)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("a link is added", CounterBody + "\nOwner: entity {\n  Code: Text length(1, 5)\n  tallies: many Tally\n}")]
    [Arguments("a stage is added", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n  Open: stage { Close: action { transition to Done } }\n  Done: stage { }\n}")]
    [Arguments("a column is removed", "Tally: entity {\n  Name: Text unique length(1, 20)\n}")]
    [Arguments("a Number becomes a Decimal", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Decimal default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("an entity is renamed", "Count: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("a table name is set", "Tally: entity table(\"TALLY_ROWS\") {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    public async Task SchemaId_SchemaChange_Differs(string change, string body) {
        var baseline = SchemaIdOf(CounterHeader + CounterBody);
        var changed = SchemaIdOf(CounterHeader + body);

        await Assert.That(changed).IsNotNull().Because(change);
        await Assert.That(changed).IsNotEqualTo(baseline).Because(change);
    }

    private const string Tally1 = "Tally: entity {\n  Name: Text unique length(1, 20)\n";

    [Test]
    [Arguments("Date becomes DateTime", Tally1 + "  When: Date optional\n}", Tally1 + "  When: DateTime optional\n}")]
    [Arguments("Text becomes an enum", Tally1 + "  Kind: Text optional\n}", "Level: enum { Low, High }\n" + Tally1 + "  Kind: Level optional\n}")]
    // The CHECK constraint is emitted only for a range the analysis proves; an action that writes N from a free parameter
    // removes the proof, so the CHECK goes: a real schema difference (the guide says so).
    [Arguments("an action changes whether a range is proven", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 10)\n}", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 10)\n  Set: action (v: Number) { assign N to v }\n}")]
    [Arguments("a stage is removed", Tally1 + "  Open: stage { Close: action { transition to Done } }\n  Done: stage { }\n}", Tally1 + "}")]
    [Arguments("a navigation is removed", Tally1 + "}\nOwner: entity {\n  Code: Text unique\n  tallies: many Tally\n}", Tally1 + "}\nOwner: entity {\n  Code: Text unique\n}")]
    [Arguments("a link becomes owned", Tally1 + "}\nOwner: entity {\n  Code: Text unique\n  tallies: many Tally\n}", Tally1 + "}\nOwner: entity {\n  Code: Text unique\n  tallies: many owned Tally\n}")]
    [Arguments("an entity is removed", Tally1 + "}\nOther: entity {\n  Code: Text unique\n}", Tally1 + "}")]
    public async Task SchemaId_BetweenTwoDocuments_Differs(string change, string a, string b) {
        await Assert.That(SchemaIdOf(CounterHeader + b)).IsNotEqualTo(SchemaIdOf(CounterHeader + a)).Because(change);
    }

    /// <summary>A <c>key(...)</c> designation changes routes, not a table or an index (the key index is never printed).</summary>
    [Test]
    public async Task SchemaId_KeyDesignationChange_HasNoDatabaseEffect_SoTheIdStays() {
        var a = CounterHeader + "Tally: entity key(Name) {\n  Name: Text unique length(1, 20)\n  Code: Text unique length(1, 20)\n}";
        var b = CounterHeader + "Tally: entity key(Code) {\n  Name: Text unique length(1, 20)\n  Code: Text unique length(1, 20)\n}";

        await Assert.That(SchemaIdOf(b)).IsEqualTo(SchemaIdOf(a));
    }

    [Test]
    public async Task SchemaId_LineEndings_AreNormalisedBeforeHashing() {
        var unix = "poly-schema-id/1\nb.Property<int>(\"Id\");\nb.HasKey(\"Id\");\n";
        var windows = unix.Replace("\n", "\r\n", StringComparison.Ordinal);

        await Assert.That(MinimalApiGenerator.FoldSchemaId(MinimalApiGenerator.NormalizeSchemaIdInput(windows)))
            .IsEqualTo(MinimalApiGenerator.FoldSchemaId(MinimalApiGenerator.NormalizeSchemaIdInput(unix)));
        await Assert.That(MinimalApiGenerator.NormalizeSchemaIdInput(windows)).IsEqualTo(unix);
    }

    [Test]
    [Arguments("an action body", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 2 }\n}")]
    [Arguments("an action is added", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n  Reset: action { assign N to 0 }\n}")]
    [Arguments("a policy is added", "Tally: entity {\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Positive: policy { N > 0 }\n  Bump: action { assign N to N + 1 }\n}")]
    [Arguments("a comment", "// a note\nTally: entity {\n  // another\n  Name: Text unique length(1, 20)\n  N: Number default(0) range(0, 1000000)\n  Bump: action { assign N to N + 1 }\n}")]
    public async Task SchemaId_NonSchemaChange_Unchanged(string change, string body) {
        var baseline = SchemaIdOf(CounterHeader + CounterBody);
        var changed = SchemaIdOf(CounterHeader + body);

        await Assert.That(changed).IsEqualTo(baseline).Because(change);
    }

    [Test]
    public async Task SchemaId_SqliteHost_CreatedSetsIt_ExistingIsCompared_AndNamesTheFile() {
        var program = HostProgram(CounterHeader + CounterBody);

        await Assert.That(program).Contains("var created = await db.Database.EnsureCreatedAsync();");
        await Assert.That(program).Contains("PRAGMA user_version");
        await Assert.That(program).Contains("different version of the model");
        await Assert.That(program.IndexOf("different version of the model", StringComparison.Ordinal))
            .IsLessThan(program.IndexOf("SeedAsync(db)", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("uses persistence")]
    [Arguments("uses sqlserver")]
    public async Task SchemaId_NonSqliteHost_HasNoCheck(string store) {
        var program = HostProgram("domain Counter\nuses http\n" + store + "\n" + CounterBody);

        await Assert.That(program).DoesNotContain("user_version");
        await Assert.That(program).DoesNotContain("schemaId");
    }

    private static MinimalApiGenerator GeneratorFor(string poly) {
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(poly, "Counter.poly", CancellationToken.None);
        var domain = result.Domain!;
        var analysis = result.Analysis!;
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        return new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate);
    }

    private static string WithoutWhitespace(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>The id is computed from a second <c>DbContextGenerator</c> run, so a drift from the artifact the app
    /// actually compiles must fail here: the hashed body is the <c>OnModelCreating</c> text of the emitted file.</summary>
    [Test]
    public async Task SchemaId_HashedModelText_IsTheOnModelCreatingOfTheEmittedDbContext() {
        var poly = CounterHeader + "Level: enum { Low, High }\n" + CounterBody.Replace("Bump:", "Kind: Level optional\n  Open: stage { Close: action { transition to Done } }\n  Done: stage { }\n  Bump:", StringComparison.Ordinal)
            + "\nOwner: entity {\n  Code: Text unique\n  tallies: many Tally\n}";
        var emitted = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(poly, "Counter.poly", CancellationToken.None).Files.Single(f => f.FileName == "CounterDbContext.cs").Source;
        var input = GeneratorFor(poly).SchemaIdInput();
        var body = input.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("column ", StringComparison.Ordinal)
            && !l.StartsWith("stage ", StringComparison.Ordinal) && !l.StartsWith("navigation ", StringComparison.Ordinal));

        await Assert.That(WithoutWhitespace(emitted)).Contains(WithoutWhitespace(string.Join("\n", body)));
    }

    /// <summary>EF's own model, read from the compiled generated context, against the hashed input: every column EF will
    /// create, every foreign key and every index must be accounted for by a line of the input (a scalar by its
    /// <c>column</c> or printed property line, a stage by its <c>stage</c> line, the key by its printed property, a foreign
    /// key by a <c>navigation</c> line on either side, an index by its printed <c>HasIndex</c>).</summary>
    [Test]
    public async Task SchemaId_Input_AccountsForEveryColumnForeignKeyAndIndexOfEfsOwnModel() {
        var poly = CounterHeader + """
            Level: enum { Low, High }
            Tally: entity {
              Name: Text unique length(1, 20)
              N: Number default(0) range(0, 1000000)
              Price: Decimal optional
              Born: DateTime optional
              Kind: Level optional
              Open: stage { Close: action { transition to Done } }
              Done: stage { }
            }
            Owner: entity {
              Code: Text unique
              tallies: many Tally
              spare: Spare optional
            }
            Spare: entity {
              Label: Text unique
            }
            """;
        var files = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(poly, "Counter.poly", CancellationToken.None).Files.Where(f => f.FileName != "Program.cs" && f.FileName.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        var assembly = ExportedCSharp.CompileAndLoad(files, "SchemaIdModel");
        var contextType = assembly.GetTypes().Single(t => t.Name == "CounterDbContext");
        var optionsBuilderType = typeof(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)Activator.CreateInstance(optionsBuilderType)!;
        Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(builder, "Data Source=:memory:");
        using var context = (Microsoft.EntityFrameworkCore.DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        var input = GeneratorFor(poly).SchemaIdInput();
        var flat = WithoutWhitespace(input);
        var unaccounted = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes()) {
            var table = Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions.GetTableName(entityType)!;
            var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, null);
            var foreignKeyColumns = entityType.GetForeignKeys().SelectMany(k => k.Properties).Select(p => p.Name).ToHashSet();
            foreach (var property in entityType.GetProperties()) {
                var column = Microsoft.EntityFrameworkCore.RelationalPropertyExtensions.GetColumnName(property, store)!;
                var accounted =
                    input.Contains($"column {entityType.ClrType.Name}.{column} ", StringComparison.Ordinal)
                    || (property.Name == "CurrentStage" && input.Contains($"stage {entityType.ClrType.Name}.CurrentStage ", StringComparison.Ordinal))
                    || (property.IsPrimaryKey() && flat.Contains($"Property<int>(\"{property.Name}\")", StringComparison.Ordinal))
                    || (foreignKeyColumns.Contains(property.Name) && entityType.GetForeignKeys().Where(k => k.Properties.Contains(property)).Any(k =>
                        new[] { k.DependentToPrincipal?.Name, k.PrincipalToDependent?.Name }.Where(n => n is not null)
                            .Any(n => input.Contains($"navigation {k.DeclaringEntityType.ClrType.Name}.{n} ", StringComparison.Ordinal)
                                || input.Contains($"navigation {k.PrincipalEntityType.ClrType.Name}.{n} ", StringComparison.Ordinal))));
                if (!accounted)
                    unaccounted.Add($"{table}.{column}");
            }
            foreach (var index in entityType.GetIndexes()) {
                // EF's convention indexes a foreign key column: it exists because of the navigation line, not a printed index.
                if (!index.IsUnique && index.Properties.All(p => foreignKeyColumns.Contains(p.Name)))
                    continue;
                foreach (var property in index.Properties) {
                    if (!flat.Contains($"HasIndex(x=>x.{property.Name})", StringComparison.Ordinal))
                        unaccounted.Add($"{table} index on {property.Name}");
                }
            }
        }

        await Assert.That(unaccounted).IsEmpty();
    }
}