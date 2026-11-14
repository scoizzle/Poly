using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// P-13b (i): <see cref="MinimalApiGenerator.ReadsOf(Entity, BehaviorAction)"/>, the stored navigation state an action reads
/// at any depth. Trees render as <c>root{Nav{Nav}, Coll[items]{…}, Coll[loaded]}</c>: <c>[items]</c> is a collection whose
/// items are iterated, <c>[loaded]</c> one that is only counted, tested or added to.
/// </summary>
public partial class MinimalApiGeneratorTests {
    private sealed record HostCompile(Domain Domain, AnalysisResult Analysis, ArtifactCatalog Catalog);

    /// <summary>The compile the DslCompiler facade runs for a host (sqlite, <c>http</c> loaded), keeping the domain,
    /// the analysis and the lowered catalog that the host generator reads.</summary>
    private static HostCompile CompileForHost(string poly) {
        var catalog = ProductCatalog.Create("sqlite");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite", HostIds = ["http"] };
        var compiled = new PolyCompiler(catalog, options).Compile(poly);
        if (!compiled.Succeeded)
            throw new InvalidOperationException(string.Join("; ", compiled.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return new HostCompile(compiled.Domain!, compiled.Analysis!, compiled.Session!.ArtifactCatalog);
    }

    private static MinimalApiGenerator HostGenerator(HostCompile compile) {
        var storage = compile.Analysis.GetMetadata<StorageMappingMetadata>(compile.Domain)!.Storage;
        var behavior = BehaviorMetadata.From(compile.Domain, compile.Analysis);
        var aggregate = compile.Analysis.GetMetadata<OwnershipAggregateMetadata>(compile.Domain)!.Aggregate;
        return new MinimalApiGenerator(compile.Domain, compile.Analysis, storage, behavior, aggregate,
            catalog: new CatalogIndex(compile.Catalog));
    }

    private static string Reads(string poly, string entity, string action) =>
        HostGenerator(CompileForHost(poly)).ReadsOf(entity, action).Render();

    private static string ReadsOfShipped(string relativePath, string entity, string action) =>
        Reads(File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, relativePath)), entity, action);

    /// <summary>The same compile with one method's body replaced: the walk over shapes the DSL cannot author.</summary>
    private static MinimalApiGenerator WithBody(HostCompile compile, string entity, string method, Node body) {
        var catalog = new ArtifactCatalog();
        var types = compile.Catalog.Artifacts.Select(a => a.Descriptor.Id.Type).Distinct().ToList();
        foreach (var type in types)
            catalog.DeclareType(type, types);
        foreach (var artifact in compile.Catalog.Artifacts) {
            if (artifact.Descriptor.Id.Type == "entity" && artifact.Descriptor.Id.Segments[^1] == entity) {
                var replaced = ((IReadOnlyList<TypeDefinitionNode>)artifact.Payload!)
                    .Select(t => t.Name != entity ? t : t with {
                        Methods = [.. t.Methods!.Select(m => m.Name == method ? m with { Body = body } : m)],
                    })
                    .ToList();
                catalog.Register(artifact with { Payload = replaced });
            }
            else
                catalog.Register(artifact);
        }
        return HostGenerator(compile with { Catalog = catalog });
    }

    private static Block ReturnsSuccessAfter(params Node[] statements) =>
        new([.. statements, new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")))]);

    private const string LinkedPoly = """
        domain Shop
        Region: entity { Name: Text }
        Customer: entity { region: Region  Label: Text }
        Order: entity {
          customer: Customer
          Note: Text optional
          Touch: action { assign Note to "x" }
        }
        """;

    /// <summary>A unique property makes the lowered action take the storage port (<c>db</c>).</summary>
    private const string PortPoly = """
        domain Lab
        Region: entity { Name: Text }
        Widget: entity {
          Code: Text unique
          region: Region
          Recode: action (code: Text) { assign Code to code }
        }
        """;

    // ── Trees ─────────────────────────────────────────────────

    [Test]
    public async Task ActionReads_ScalarOnlyAction_IsAnEmptyTree() {
        var reads = Reads(LinkedPoly, "Order", "Touch");

        await Assert.That(reads).IsEqualTo("this{}");
    }

    [Test]
    public async Task ActionReads_GuardThroughTwoHops_ReadsBothHops() {
        const string poly = """
            domain Shop
            Region: entity { Name: Text }
            Customer: entity { region: Region }
            Order: entity {
              customer: Customer
              InRegion: policy { customer region Name is "north" }
              Go: action require InRegion { }
            }
            """;

        await Assert.That(Reads(poly, "Order", "Go")).IsEqualTo("this{Customer{Region}}");
    }

    /// <summary>The DSL reads a parameter's scalars only (`p region Name` is rejected), so the read through a parameter is
    /// the one `create in` makes: the new line links the product, and <c>Line.Create</c> attaches it to the product's
    /// lines (<c>product.AttachLines(created)</c>, a lowered callee whose parameter is bound to <c>p</c>).</summary>
    [Test]
    public async Task ActionReads_ParameterRootedRead_ReadsThroughTheParameter() {
        const string poly = """
            domain Shop
            Product: entity { Price: Number  lines: many Line via product }
            Order: entity {
              lines: many Line via order
              Add: action (p: Product) -> Line { create in lines { Qty: 1  product: p } }
            }
            Line: entity { Qty: Number  product: Product  order: Order }
            """;

        await Assert.That(Reads(poly, "Order", "Add")).IsEqualTo("this{Lines[loaded]}; p{Lines[loaded]}");
    }

    [Test]
    public async Task ActionReads_NestedRoute_ReadsItsParentsLink() {
        const string poly = """
            domain Shop
            Customer: entity { Name: Text }
            Order: entity { customer: Customer  lines: many owned Line }
            Line: entity {
              order: Order via lines
              Note: Text optional
              Tag: action { assign Note to order customer Name }
            }
            """;

        await Assert.That(Reads(poly, "Line", "Tag")).IsEqualTo("this{Order{Customer}}");
    }

    [Test]
    public async Task ActionReads_EntryEffectReadingALink_IsRead() {
        const string poly = """
            domain Desk
            Region: entity { Name: Text }
            Ticket: entity {
              region: Region
              Label: Text optional
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { entry { assign Label to region Name } }
            }
            """;

        await Assert.That(Reads(poly, "Ticket", "Close")).IsEqualTo("this{Region}");
    }

    [Test]
    public async Task ActionReads_CreateIn_LoadsTheCollectionWithoutDescending() {
        const string poly = """
            domain Shop
            Order: entity {
              lines: many Line
              Add: action -> Line { create in lines { Qty: 1 } }
            }
            Line: entity { Qty: Number }
            """;

        await Assert.That(Reads(poly, "Order", "Add")).IsEqualTo("this{Lines[loaded]}");
    }

    /// <summary>An item's to-one back to the collection it was iterated from is the owner itself (EF fixup sets it): no
    /// new node. The other direction, a to-one's inverse collection, is a new node.</summary>
    [Test]
    public async Task ActionReads_ItemsBackReference_IsTheOwnerAndAToOnesCollectionIsANewNode() {
        const string poly = """
            domain Shop
            Order: entity {
              Open: Boolean default(true)
              lines: many Line via order
              Go: action { for lines as l invoke l.Check }
            }
            Line: entity {
              order: Order
              OrderOpen: policy { order Open is true }
              Check: action require OrderOpen { }
            }
            """;
        var compile = CompileForHost(poly);
        var siblings = new Member(new Member(new Member(new ThisReference(), "Order"), "Lines"), "Count");
        var counting = WithBody(compile, "Line", "__BodyOfCheck", ReturnsSuccessAfter(new Assignment(new Variable("n"), siblings)));

        await Assert.That(HostGenerator(compile).ReadsOf("Order", "Go").Render()).IsEqualTo("this{Lines[items]}");
        await Assert.That(counting.ReadsOf("Line", "Check").Render()).IsEqualTo("this{Order{Lines[loaded]}}");
    }

    /// <summary>Re-entering a method already on the stack with the same bindings reuses it: the reads are the ones being
    /// recorded, so it is not a cycle.</summary>
    [Test]
    public async Task ActionReads_RecursionWithTheSameBindings_IsReused() {
        var compile = CompileForHost(LinkedPoly);
        var body = ReturnsSuccessAfter(new IfStatement(
            new Equal(new Member(new ThisReference(), "Customer"), new Constant(null)),
            new Block(new Invoke(new Member(new ThisReference(), "Touch")))));

        var reads = WithBody(compile, "Order", "__BodyOfTouch", body).ReadsOf("Order", "Touch").Render();

        await Assert.That(reads).IsEqualTo("this{Customer}");
    }

    // ── Shipped actions ───────────────────────────────────────

    /// <summary>`Section.Occupy` (through `invoke section.Occupy`) reads its own hall in `ParentFree` and invokes
    /// `hall.MarkChildBusy`, which reads the hall's scalars.</summary>
    [Test]
    public async Task ActionReads_HotelStartSection_ReadsTheSectionsHall() {
        var reads = ReadsOfShipped("docs/probes/dogfood/hotel.poly", "EventHold", "StartSection");

        await Assert.That(reads).IsEqualTo("this{Section{Hall}}");
    }

    /// <summary>`Section.Vacate` reads `Occupied` (a scalar) and invokes `hall.ClearChildBusy`, which reads and writes the
    /// hall's `BusySections`; the `Released` notify iterates the (empty over HTTP) guest registry.</summary>
    [Test]
    public async Task ActionReads_HotelReleaseSection_ReadsTheSectionsHall() {
        var reads = ReadsOfShipped("docs/probes/dogfood/hotel.poly", "EventHold", "ReleaseSection");

        await Assert.That(reads).IsEqualTo("this{Section{Hall}}");
    }

    [Test]
    public async Task ActionReads_InsuranceReviewAll_ReadsEachClaimsDocuments() {
        var reads = ReadsOfShipped("docs/probes/scratch-2026-10-14/insurance.poly", "Policy", "ReviewAll");

        await Assert.That(reads).IsEqualTo("this{Claims[items]{Documents[loaded]}}");
    }

    [Test]
    public async Task ActionReads_GrepScan_ReadsEachSourcesLines() {
        var reads = ReadsOfShipped("docs/probes/grep-2026-10-17/grep.poly", "Query", "Scan");

        await Assert.That(reads).IsEqualTo("this{Sources[items]{Lines[items]}}");
    }

    [Test]
    public async Task ActionReads_CrmLeadConvert_ReadsTheAccount() {
        var reads = ReadsOfShipped("docs/probes/dogfood/crm.poly", "Lead", "Convert");

        await Assert.That(reads).IsEqualTo("this{Account}");
    }

    [Test]
    public async Task ActionReads_InsurancePolicyholderFile_PassesTheStoragePortValuesOnly() {
        var reads = ReadsOfShipped("docs/probes/scratch-2026-10-14/insurance.poly", "Policyholder", "File");

        await Assert.That(reads).IsEqualTo("this{Claims[loaded]}");
    }

    // ── The subscriber registry (#560) ────────────────────────

    /// <summary>Pins the named hole: over HTTP the registry is empty, so the `when` handler's read of the watcher's region is
    /// not in the tree. When #560 rewires subscribers the flag turns false and this test fails.</summary>
    [Test]
    public async Task ActionReads_SubscribersEmptyOverHttp_HandlersAreNotWalked() {
        const string poly = """
            domain Watch
            Region: entity { Name: Text }
            Watcher: entity {
              region: Region
              Seen: Text optional
              items: many Item
              when items Done { assign Seen to region Name }
            }
            Item: entity {
              watcher: Watcher via items
              Open: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            """;

        await Assert.That(ActionReads.SubscribersEmptyOverHttp).IsTrue();
        await Assert.That(Reads(poly, "Item", "Finish")).IsEqualTo("this{}");
    }

    // ── Refusals ──────────────────────────────────────────────

    [Test]
    public async Task ActionReads_DescendingRecursion_IsRefusedNamingTheCycle() {
        const string poly = """
            domain Tree
            Node: entity {
              kids: many Node
              Close: action { }
            }
            """;
        var compile = CompileForHost(poly);
        var kid = new Variable("kid");
        var body = ReturnsSuccessAfter(new ForEachLoop(kid, new Member(new ThisReference(), "Kids"),
            new Invoke(new Member(kid, "Close"))));

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Node", "__BodyOfClose", body).ReadsOf("Node", "Close"));

        await Assert.That(refusal.Message).Contains("Action 'Node.Close' cannot be served over HTTP");
        await Assert.That(refusal.Message).Contains("call cycle");
    }

    [Test]
    public async Task ActionReads_CallOutsideTheModuleWithAnEntity_IsRefused() {
        var compile = CompileForHost(LinkedPoly);
        var body = ReturnsSuccessAfter(new Invoke(new Member(new TypeReference("Telemetry"), "Peek"), new ThisReference()));

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Order", "__BodyOfTouch", body).ReadsOf("Order", "Touch"));

        await Assert.That(refusal.Message).Contains("Action 'Order.Touch'");
        await Assert.That(refusal.Message).Contains("outside the module and the whitelist");
    }

    [Test]
    public async Task ActionReads_EntityThroughAJoinOfDifferentValues_IsRefused() {
        var compile = CompileForHost(LinkedPoly);
        var either = new Conditional(new Constant(true), new Member(new ThisReference(), "Customer"), new ThisReference());
        var body = ReturnsSuccessAfter(new Assignment(new Variable("x"), new Member(either, "Label")));

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Order", "__BodyOfTouch", body).ReadsOf("Order", "Touch"));

        await Assert.That(refusal.Message).Contains("Action 'Order.Touch'");
    }

    [Test]
    public async Task ActionReads_EntityThroughAJoinOfTheSameNode_IsFollowed() {
        var compile = CompileForHost(LinkedPoly);
        var customer = new Member(new ThisReference(), "Customer");
        var either = new Conditional(new Constant(true), customer, customer);
        var body = ReturnsSuccessAfter(new Assignment(new Variable("x"), new Member(new Member(either, "Region"), "Name")));

        var reads = WithBody(compile, "Order", "__BodyOfTouch", body).ReadsOf("Order", "Touch").Render();

        await Assert.That(reads).IsEqualTo("this{Customer{Region}}");
    }

    [Test]
    public async Task ActionReads_EntityThroughALambda_IsRefused() {
        var compile = CompileForHost(LinkedPoly);
        var body = ReturnsSuccessAfter(new Assignment(new Variable("f"),
            new Lambda([new Parameter("o")], new Member(new ThisReference(), "Customer"))));

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Order", "__BodyOfTouch", body).ReadsOf("Order", "Touch"));

        await Assert.That(refusal.Message).Contains("'Lambda' node");
    }

    // ── The whitelist (one test per entry) ────────────────────

    public static IEnumerable<string> WhitelistEntries() =>
        ActionReadsWalker.Whitelist.Select(e => $"{e.Receiver}.{e.Method}");

    /// <summary>Each entry is reached with the action's own link as its argument (where the entry takes an entity):
    /// the tree holds that link, read to pass it, and nothing through it.</summary>
    [Test]
    [MethodDataSource(nameof(WhitelistEntries))]
    public async Task ActionReads_WhitelistEntry_ReadsNothingThroughItsArguments(string entry) {
        var compile = CompileForHost(LinkedPoly);
        var customer = new Member(new ThisReference(), "Customer");
        var failure = new Invoke(new Member(new ClrTypeReference(typeof(DomainFailure)), "Policy"), new Constant("m"), new Constant("p"));
        var (call, expected) = entry switch {
            "Poly.Runtime.ActionScope.*" => ((Node)new Invoke(new Member(new ClrTypeReference(typeof(ActionScope)), "Touch"), customer), "this{Customer}"),
            "System.Object.ReferenceEquals" => (new Invoke(new Member(new ClrTypeReference(typeof(object)), "ReferenceEquals"), customer, customer), "this{Customer}"),
            "Poly.Runtime.DomainResult.*" => (new Invoke(new Member(new NamedTypeReference("DomainResult", TypeArguments: [new NamedTypeReference("Customer")]), "Success"), customer), "this{Customer}"),
            "Poly.Runtime.DomainFailure.*" => (failure, "this{}"),
            "Poly.Runtime.DomainFailure.At" => (new Invoke(new Member(failure, "At"), new Constant("Order.Touch")), "this{}"),
            "Poly.Runtime.IStoragePort.*" => (new Invoke(new Member(new Parameter("db"), "Exists"), new Constant("Widget"), new Constant("Code"), new Constant("x")), "this{}"),
            _ => throw new InvalidOperationException($"No test for whitelist entry '{entry}'."),
        };
        var generator = entry == "Poly.Runtime.IStoragePort.*"
            ? WithBody(CompileForHost(PortPoly), "Widget", "__BodyOfRecode", ReturnsSuccessAfter(call))
            : WithBody(compile, "Order", "__BodyOfTouch", ReturnsSuccessAfter(call));
        var reads = entry == "Poly.Runtime.IStoragePort.*"
            ? generator.ReadsOf("Widget", "Recode").Render()
            : generator.ReadsOf("Order", "Touch").Render();

        await Assert.That(reads).IsEqualTo(expected);
    }

    /// <summary>The port takes values only: an entity argument refuses.</summary>
    [Test]
    public async Task ActionReads_StoragePortWithAnEntity_IsRefused() {
        var compile = CompileForHost(PortPoly);
        var body = ReturnsSuccessAfter(new Invoke(new Member(new Parameter("db"), "Record"), new Member(new ThisReference(), "Region")));

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Widget", "__BodyOfRecode", body).ReadsOf("Widget", "Recode"));

        await Assert.That(refusal.Message).Contains("storage-port call 'Record' with an entity argument");
    }

    /// <summary>Lowering emits <c>LinkRelated</c> with no definition, so it is not on the whitelist and refuses.</summary>
    [Test]
    public async Task ActionReads_LinkRelated_IsRefused() {
        var compile = CompileForHost(LinkedPoly);
        var body = ReturnsSuccessAfter(new Invoke(new Member(new ThisReference(), "LinkRelated"),
            new Constant("customer"), new Member(new ThisReference(), "Customer")));

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Order", "__BodyOfTouch", body).ReadsOf("Order", "Touch"));

        await Assert.That(refusal.Message).Contains("'Order.LinkRelated', which the module does not define");
    }

    // ── Every Node kind is classified ─────────────────────────

    [Test]
    public async Task ActionReads_EveryNodeKind_IsClassified() {
        var kinds = new[] { typeof(Node).Assembly, typeof(MinimalApiGenerator).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Node).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var unclassified = kinds.Where(t => ActionReadsWalker.KindOf(t) is null).Select(t => t.FullName).ToList();

        await Assert.That(kinds.Count).IsGreaterThan(50);
        await Assert.That(unclassified).IsEmpty();
    }

    // ── The sweep ─────────────────────────────────────────────

    public static IEnumerable<string> LowerableDomains() => GoldenDomains.Lowerable.Select(d => d.Path);

    /// <summary>No action of a shipped document is refused: the owner chose loading over refusing.</summary>
    [Test]
    [MethodDataSource(nameof(LowerableDomains))]
    public async Task ActionReads_EveryShippedAction_IsNotRefused(string relativePath) {
        var compile = CompileForHost(File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, relativePath)));
        var generator = HostGenerator(compile);
        var behavior = BehaviorMetadata.From(compile.Domain, compile.Analysis);
        var refused = new List<string>();
        foreach (var entity in behavior.Entities) {
            foreach (var action in entity.Actions.Select(a => a.Name).Distinct()) {
                try {
                    generator.ReadsOf(entity.Name, action);
                }
                catch (ActionReadsRefusedException refusal) {
                    refused.Add(refusal.Message);
                }
            }
        }

        await Assert.That(refused).IsEmpty();
    }
}