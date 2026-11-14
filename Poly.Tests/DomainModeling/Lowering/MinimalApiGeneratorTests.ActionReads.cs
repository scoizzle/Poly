using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Tests.Docs;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// P-13b (i): <see cref="MinimalApiGenerator.ReadsOf(Entity, BehaviorAction)"/>, the stored navigation state an action reads
/// at any depth. Trees render as <c>root{Nav{Nav}, Coll[items]{…}, Coll[loaded]}</c>: <c>[items]</c> is a collection whose
/// items are iterated, <c>[loaded]</c> one that is only counted, tested or added to.
/// </summary>
public partial class MinimalApiGeneratorTests {
    private sealed record HostCompile(Domain Domain, AnalysisResult Analysis, ArtifactCatalog Catalog);

    /// <summary>The compile the DslCompiler facade runs for a host (sqlite, <c>http</c> loaded), keeping the domain,
    /// the analysis and the lowered catalog that the host generator reads. <paramref name="generateHost"/> false loads no host,
    /// so the compile does not run the generator (a refused action fails a host compile; these tests ask the walk itself).</summary>
    private static HostCompile CompileForHost(string poly, bool generateHost = true) {
        var catalog = ProductCatalog.Create("sqlite");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite", HostIds = generateHost ? ["http"] : [] };
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

    /// <summary>With two to-ones of the same type on the item, only the inverse of the iterated collection is the owner:
    /// <c>assignedTo</c> leads back to the user, <c>reviewedBy</c> is a new node.</summary>
    [Test]
    public async Task ActionReads_ItemWithTwoToOnesOfTheOwnersType_OnlyTheInverseIsTheOwner() {
        const string poly = """
            domain Desk
            User: entity {
              Name: Text
              assigned: many Task via assignedTo
              Go: action { for assigned as t invoke t.Check }
            }
            Task: entity {
              assignedTo: User
              reviewedBy: User
              Mine: policy { assignedTo Name is "a" }
              Theirs: policy { reviewedBy Name is "b" }
              Check: action require Mine require Theirs { }
            }
            """;

        await Assert.That(Reads(poly, "User", "Go")).IsEqualTo("this{Assigned[items]{ReviewedBy}}");
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

    // ── The preload ───────────────────────────────────────────

    private static string GoldenPath(string name) =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "DomainModeling", "Lowering", "Golden", "ActionReads", name);

    /// <summary>The statements an action route runs before the module's action: the lookups and every load.</summary>
    private static string PreloadOf(string program, string route) {
        var lines = program.Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains($"MapPost(\"{route}\""));
        var end = Array.FindIndex(lines, start, l => l.TrimStart().StartsWith("var result", StringComparison.Ordinal));
        return $"// {route}\n" + string.Join("\n", lines[(start + 1)..end]) + "\n";
    }

    /// <summary>The emitted preload of one action per read shape, as text: the same document gives the same code (the templates are
    /// the golden). Set <c>POLY_UPDATE_GOLDEN=1</c> to write it.</summary>
    [Test]
    public async Task ActionReads_Preload_IsDeterministicAndMatchesTheGolden() {
        var domains = new (string Poly, string Db, string[] Routes)[] {
            (Poly.Tests.TestHelpers.PackOracleDomains.ReadDepth912.Poly, "DepthDbContext", [
                "/api/orders/{code}/threehop",           // a path of links through the entity
                "/api/orders/{code}/anynamed",           // a collection read through each item's link
                "/api/customers/{code}/region/makezone", // a nested route
                "/api/orders/{code}/lines/{lineCode}/showcustomer", // a nested route reading through its parent: the subtree loads on parentEntity
                "/api/orders/{code}/notenamed",          // a collection under a to-one
                "/api/orders/{code}/fantags",            // a collection in a collection
            ]),
            (Poly.Tests.TestHelpers.PackOracleDomains.SameRow912.Poly, "AliasDbContext", [
                "/api/orders/{code}/go",                 // a parameter whose own link is read
            ]),
            (Poly.Tests.TestHelpers.PackOracleDomains.Names912.Poly, "NamesDbContext", [
                "/api/orders/{code}/both",               // two paths whose concatenated local names are equal
            ]),
            (Poly.Tests.TestHelpers.PackOracleDomains.TwoParams912.Poly, "PairDbContext", [
                "/api/orders/{code}/both",               // two parameters of one entity type
            ]),
        };
        var programs = domains.Select(d => HostGenerator(CompileForHost(d.Poly)).Generate(d.Db)).ToList();
        var again = domains.Select(d => HostGenerator(CompileForHost(d.Poly)).Generate(d.Db)).ToList();
        var preload = string.Concat(domains.SelectMany((d, i) => d.Routes.Select(r => PreloadOf(programs[i], r))));

        await Assert.That(programs).IsEquivalentTo(again);
        if (Environment.GetEnvironmentVariable("POLY_UPDATE_GOLDEN") == "1") {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath("read_depth.preload.txt"))!);
            File.WriteAllText(GoldenPath("read_depth.preload.txt"), preload);
        }
        await Assert.That(preload).IsEqualTo(File.ReadAllText(GoldenPath("read_depth.preload.txt")));
    }

    /// <summary>Locals of the loads are unique by construction: one set, numeric suffix on a clash. Two paths whose concatenated
    /// names are equal (<c>Customer</c> + <c>Region</c> and <c>CustomerRegion</c>) must not lean on the writer's own rename
    /// (<c>name_2</c>), which only works where the declaration and every use sit in one scope.</summary>
    [Test]
    public async Task ActionReads_TreeLocalsThatCollide_GetNumericSuffixes_NotAWriterRename() {
        var program = HostGenerator(CompileForHost(Poly.Tests.TestHelpers.PackOracleDomains.Names912.Poly)).Generate("NamesDbContext");
        var preload = PreloadOf(program, "/api/orders/{code}/both");

        await Assert.That(preload).Contains("var entityCustomerRegion = entityCustomer.Region;");
        await Assert.That(preload).Contains("var entityCustomerRegion2 = entity.CustomerRegion;");
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(preload, @"\w_\d\b")).IsFalse();
    }

    /// <summary>A tree root with reads that no route parameter consumed would load nothing: generation fails instead, naming the
    /// action and the root. A root that a parameter consumed, or one with no reads, is fine.</summary>
    [Test]
    public async Task ActionReads_RootWithReadsThatMatchesNoParameter_FailsGeneration() {
        var reads = new ActionReads("Order", "Go", [
            new ReadRoot("this", "Order", []),
            new ReadRoot("q", "Product", [new ReadNode("Zone", false, "Zone", [], false)]),
            new ReadRoot("idle", "Product", []),
        ]);

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => MinimalApiGenerator.RequireEveryRootLoaded(reads, new HashSet<string> { "this" }));
        MinimalApiGenerator.RequireEveryRootLoaded(reads, new HashSet<string> { "this", "q" });

        await Assert.That(refusal.Message).Contains("Action 'Order.Go' cannot be served over HTTP");
        await Assert.That(refusal.Message).Contains("'q'");
    }

    /// <summary>Without a catalog the generator loads only the one-hop floor; production never builds one without. The host
    /// contributor (the path <c>polyc</c> and the facade take) passes the lowered catalog, so it loads what the tree says: this
    /// fails if that argument is removed.</summary>
    [Test]
    public async Task ActionReads_HostContributorPath_PassesTheCatalog_SoTheTreeLoads() {
        var result = new global::Poly.DslCompiler.DslCompiler()
            .Load(new Poly.DomainModeling.Libraries.Http.HttpLibrary())
            .Compile(Poly.Tests.TestHelpers.PackOracleDomains.ReadDepth912.Poly, global::Poly.DslCompiler.CompileMode.All, global::Poly.DslCompiler.DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();
        var program = result.Files!.Single(f => f.FileName == "Program.cs").Source;

        await Assert.That(PreloadOf(program, "/api/orders/{code}/threehop")).Contains("db.Entry(entityCustomerRegion).Reference(e => e.Zone).LoadAsync()");
    }

    /// <summary>The one-hop floor: an action whose tree is empty still loads the entity's single links (P-13a's set). The floor is
    /// a safety net for the tree's assumptions; the tree, not the floor, says what the action reads.</summary>
    [Test]
    public async Task ActionReads_ActionThatReadsNothing_StillLoadsTheOneHopFloor() {
        var program = HostGenerator(CompileForHost(LinkedPoly)).Generate("ShopDbContext");
        var preload = PreloadOf(program, "/api/orders/{id}/touch");

        await Assert.That(HostGenerator(CompileForHost(LinkedPoly)).ReadsOf("Order", "Touch").Render()).IsEqualTo("this{}");
        await Assert.That(preload).Contains("db.Entry(entity).Reference(e => e.Customer).LoadAsync()");
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

    /// <summary>Pins the #560 statement: a registry holds only the subscribers registered during this request. A stored
    /// item's registry has none, so finishing it runs no handler and the watcher's region is not in the tree (a registry the
    /// action fills is walked: <see cref="ActionReads_HandlerOfASubscriberRegisteredInTheAction_IsWalked"/>). When #560
    /// re-registers stored subscribers the flag turns false and this test fails.</summary>
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

    /// <summary>F1: a `create in` registers the owner in the new child's registry (<c>Item.Create</c> →
    /// <c>watcher.AttachItems</c> → <c>child.RegisterWatcherItemsOpenSubscriber(this)</c>), and <c>CreateItems</c> then calls
    /// <c>item.NotifyOpenSubscribers()</c>, which runs the owner's handler: <c>invoke region.Copy</c> reads the region's
    /// zone. Over HTTP that action answered 500 against the simulator's success before the walk followed it.</summary>
    [Test]
    public async Task ActionReads_HandlerOfASubscriberRegisteredInTheAction_IsWalked() {
        const string poly = """
            domain Watch
            Zone: entity { Label: Text }
            Region: entity {
              Name: Text
              zone: Zone
              Copy: action { assign Name to zone Label }
            }
            Watcher: entity {
              region: Region
              items: many Item
              when items Open { invoke region.Copy }
              Add: action -> Item { create in items { } }
            }
            Item: entity {
              Tag: Text optional
              watcher: Watcher via items
              Open: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            """;

        await Assert.That(Reads(poly, "Watcher", "Add")).IsEqualTo("this{Items[loaded], Region{Zone}}");
    }

    /// <summary>F-B: the same row reached through two paths. The new line registers on <c>p</c>'s registry; <c>Close</c>
    /// notifies through <c>this.Product</c>. When the client passes the order's own product (the simulator runs this: the
    /// handler fires and copies <c>p.Zone.Label</c>), the handler reads <c>p.Zone</c>. A registry keyed by type sees it.</summary>
    [Test]
    public async Task ActionReads_SubscriberRegisteredThroughAnotherPathToTheSameRow_IsWalked() {
        const string poly = """
            domain Alias
            Zone: entity { Label: Text }
            Product: entity {
              Name: Text
              zone: Zone
              Copy: action { assign Name to zone Label }
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { }
            }
            Line: entity {
              product: Product
              order: Order via lines
              Seen: Text optional
              when product Closed { invoke product.Copy }
            }
            Order: entity {
              product: Product
              lines: many Line via order
              Go: action (p: Product) { create in lines { product: p }  invoke product.Close }
            }
            """;

        await Assert.That(Reads(poly, "Order", "Go")).IsEqualTo("this{Lines[loaded], Product}; p{Zone}");
    }

    /// <summary>F-A: each new task's handler creates another task that registers on the project's registry, so the walk
    /// would replay the project's notify without end (it overflowed the stack). At run time the action ends: the notify
    /// ran before the first task registered, and the simulator creates one task. The walk cannot bound the chain soundly
    /// (each task's bindings differ: its parent is the previous one), so it refuses by name.</summary>
    [Test]
    public async Task ActionReads_HandlerChainThatRegistersWithoutEnd_IsRefused() {
        const string poly = """
            domain Spawn
            Project: entity {
              tasks: many Task via project
              Idle: stage { Start: action { transition to Running  create in tasks { } } }
              Running: stage { }
            }
            Task: entity {
              Tag: Text optional
              project: Project via tasks
              parent: Task optional
              kids: many Task via parent
              when project Running { create in kids { project: project } }
            }
            """;

        var refusal = Assert.Throws<ActionReadsRefusedException>(() => HostGenerator(CompileForHost(poly, generateHost: false)).ReadsOf("Project", "Start"));

        await Assert.That(refusal.Message).Contains("Action 'Project.Start' cannot be served over HTTP");
        await Assert.That(refusal.Message).Contains("a handler chain that registers further subscribers without end");
    }

    private const string SpawnPoly = """
        domain Spawn
        Project: entity {
          tasks: many Task via project
          Idle: stage { Start: action { transition to Running  create in tasks { } } }
          Running: stage { }
        }
        Task: entity {
          Tag: Text optional
          project: Project via tasks
          parent: Task optional
          kids: many Task via parent
          when project Running { create in kids { project: project } }
        }
        """;

    /// <summary>A refused action is a generation error, not a quieter host: the compile of a document whose action the walk
    /// refuses fails, naming the action and the reason. (A tree-walk recursion, <c>for kid in kids { invoke kid.Close }</c> on a
    /// self-relationship, is not authorable: <c>DMEFF007</c>. The handler chain that keeps registering subscribers is.)</summary>
    [Test]
    public async Task ActionReads_RefusedAction_FailsTheHostCompile_NamingTheAction() {
        var failure = Assert.Throws<InvalidOperationException>(() => CompileForHost(SpawnPoly));

        await Assert.That(failure.Message).Contains("Action 'Project.Start' cannot be served over HTTP");
        await Assert.That(failure.Message).Contains("a handler chain that registers further subscribers without end");
    }

    /// <summary><c>polyc build</c> of the same document (with its <c>uses</c> lines) exits non-zero, writes nothing, and the error names the action.</summary>
    [Test]
    public async Task ActionReads_RefusedAction_FailsPolycBuild_WithAnErrorNamingTheAction() {
        var dir = Path.Combine(Path.GetTempPath(), "polyc-refused-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "tree.poly");
        File.WriteAllText(path, "domain Spawn\nuses http\nuses sqlite\n" + SpawnPoly["domain Spawn".Length..].TrimStart());
        var outDir = Path.Combine(dir, "out");
        var stdout = new StringWriter();

        var exit = global::Polyc.PolycApp.Run(["build", path, "--out", outDir], TextReader.Null, false, stdout, new StringWriter());

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(stdout.ToString()).Contains("Action 'Project.Start' cannot be served over HTTP");
        await Assert.That(Directory.Exists(outDir)).IsFalse();
    }

    private const string OrderingPoly = """
        domain Proj
        Zone: entity { Label: Text }
        Region: entity { Name: Text  zone: Zone  Copy: action { assign Name to zone Label } }
        Project: entity {
          tasks: many Task via project
          Idle: stage { Start: action (r: Region) { transition to Running  create in tasks { region: r } } }
          Running: stage { }
        }
        Task: entity {
          project: Project via tasks
          region: Region
          when project Running { invoke region.Copy }
        }
        """;

    /// <summary>The notify runs before the task registers, so the walk replays the earlier iteration for it and the
    /// handler's read of <c>r.Zone</c> is in the tree. The simulator does not fire the handler here (the task joined after
    /// the notify ran; the region keeps its name): the walk over-reports, which is allowed.</summary>
    [Test]
    public async Task ActionReads_SubscriberRegisteredAfterTheNotify_IsReplayed() {
        await Assert.That(Reads(OrderingPoly, "Project", "Start")).IsEqualTo("this{Tasks[loaded]}; r{Zone}");
    }

    [Test]
    public async Task ActionReads_HandlerReadingOneHop_LandsTheLink() {
        const string poly = """
            domain Hop
            Region: entity { Name: Text }
            Watcher: entity {
              region: Region
              Seen: Text optional
              items: many Item
              when items Open { assign Seen to region Name }
              Add: action -> Item { create in items { } }
            }
            Item: entity {
              Tag: Text optional
              watcher: Watcher via items
              Open: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            """;

        await Assert.That(Reads(poly, "Watcher", "Add")).IsEqualTo("this{Items[loaded], Region}");
    }

    /// <summary>The DSL reads only a peer's scalars through a binder (`as pr`), so the handler's read of the publisher's
    /// link is built: the handler gets the publisher (<c>this.Product</c>) as its argument and reads its zone.</summary>
    [Test]
    public async Task ActionReads_HandlerReadingThePublisherThroughItsBinding_LandsThePublishersLink() {
        const string poly = """
            domain Pub
            Zone: entity { Label: Text }
            Product: entity {
              Name: Text
              zone: Zone
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { }
            }
            Line: entity {
              product: Product
              order: Order via lines
              Seen: Text optional
              when product Closed as pr { assign Seen to pr Name }
            }
            Order: entity {
              product: Product
              lines: many Line via order
              Go: action { create in lines { product: product }  invoke product.Close }
            }
            """;
        var compile = CompileForHost(poly);
        var handler = ((IReadOnlyList<TypeDefinitionNode>)compile.Catalog.Artifacts
                .Single(a => a.Descriptor.Id.Type == "entity" && a.Descriptor.Id.Segments[^1] == "Line").Payload!)
            .Single(t => t.Name == "Line").Methods!.Single(m => m.Name.StartsWith("When", StringComparison.Ordinal));
        var publisher = handler.Parameters!.Single();
        var body = ReturnsSuccessAfter(new Assignment(new Variable("z"), new Member(new Parameter(publisher.Name), "Zone")));

        var reads = WithBody(compile, "Line", handler.Name, body).ReadsOf("Order", "Go").Render();

        await Assert.That(reads).IsEqualTo("this{Lines[loaded], Product{Zone}}");
    }

    /// <summary>Transitive: the watcher's handler moves the watcher to <c>Busy</c>, which the new item watches; the item's
    /// handler reads its region's zone. Both handlers are walked.</summary>
    [Test]
    public async Task ActionReads_HandlerThatTransitionsIntoAWatchedStage_IsWalkedThrough() {
        const string poly = """
            domain Chain
            Zone: entity { Label: Text }
            Region: entity { Name: Text  zone: Zone  Copy: action { assign Name to zone Label } }
            Watcher: entity {
              items: many Item
              Idle: stage {
                Add: action (r: Region) -> Item { create in items { region: r } }
                when items Open { transition to Busy }
              }
              Busy: stage { }
            }
            Item: entity {
              watcher: Watcher via items
              region: Region
              when watcher Busy { invoke region.Copy }
              Open: stage { }
            }
            """;

        await Assert.That(Reads(poly, "Watcher", "Add")).IsEqualTo("this{Items[loaded]}; r{Zone}");
    }

    /// <summary>A replayed handler runs after its notify's frame moved on, so nothing entity-valued may flow back out of
    /// it: a returned subscriber and a local the loop assigns a subscriber to each refuse.</summary>
    [Test]
    public async Task ActionReads_ReplayedHandlerLeakingAnEntity_IsRefused() {
        var compile = CompileForHost(OrderingPoly);
        var project = ((IReadOnlyList<TypeDefinitionNode>)compile.Catalog.Artifacts
                .Single(a => a.Descriptor.Id.Type == "entity" && a.Descriptor.Id.Segments[^1] == "Project").Payload!)
            .Single(t => t.Name == "Project");
        var registry = new Member(new ThisReference(), project.Fields!.Single(f => f.Name.EndsWith("Subscribers", StringComparison.Ordinal)).Name);
        var notify = project.Methods!.Single(m => m.Name == "NotifyRunningSubscribers").Name;
        var sub = new Variable("sub");
        var leak = new Variable("leak");
        var returning = ReturnsSuccessAfter(new ForEachLoop(sub, registry, new Block(new Return(sub))));
        var assigning = ReturnsSuccessAfter(new Assignment(leak, new Constant(null)), new ForEachLoop(sub, registry, new Assignment(leak, sub)));

        var returned = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Project", notify, returning).ReadsOf("Project", "Start"));
        var assigned = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Project", notify, assigning).ReadsOf("Project", "Start"));

        await Assert.That(returned.Message).Contains("a handler result the walk cannot return into the notify that ran it");
        await Assert.That(assigned.Message).Contains("a value of a replayed handler ('leak')");
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

    /// <summary>Each entry is reached with an entity-capable argument (the port takes values only), and the tree holds what
    /// the call reads: the link it is passed, and through <c>Success(x).Value</c> what is read off <c>x</c>. Without the
    /// entry the same body refuses, so no entry is dead and no test passes vacuously.</summary>
    [Test]
    [MethodDataSource(nameof(WhitelistEntries))]
    public async Task ActionReads_WhitelistEntry_IsTheOnlyReasonTheCallIsFollowed(string entry) {
        var customer = new Member(new ThisReference(), "Customer");
        var policy = new Invoke(new Member(new ClrTypeReference(typeof(DomainFailure)), "Policy"), new Constant("m"), customer);
        var resultOfCustomer = new NamedTypeReference("DomainResult", TypeArguments: [new NamedTypeReference("Customer")]);
        var (call, expected) = entry switch {
            "Poly.Runtime.ActionScope.*" => ((Node)new Invoke(new Member(new ClrTypeReference(typeof(ActionScope)), "Touch"), customer), "this{Customer}"),
            "System.Object.ReferenceEquals" => (new Invoke(new Member(new ClrTypeReference(typeof(object)), "ReferenceEquals"), customer, customer), "this{Customer}"),
            "Poly.Runtime.DomainResult.Success" => (new Assignment(new Variable("n"),
                new Member(new Member(new Member(new Invoke(new Member(resultOfCustomer, "Success"), customer), "Value"), "Region"), "Name")),
                "this{Customer{Region}}"),
            "Poly.Runtime.DomainResult.Failure" => (new Invoke(new Member(resultOfCustomer, "Failure"), customer), "this{Customer}"),
            "Poly.Runtime.DomainFailure.*" => (policy, "this{Customer}"),
            "Poly.Runtime.DomainFailure.At" => (new Invoke(new Member(
                new Invoke(new Member(new ClrTypeReference(typeof(DomainFailure)), "Policy"), new Constant("m"), new Constant("p")), "At"), customer),
                "this{Customer}"),
            "Poly.Runtime.IStoragePort.*" => (new Invoke(new Member(new Parameter("db"), "Exists"), new Constant("Widget"), new Constant("Code"), new Constant("x")), "this{}"),
            _ => throw new InvalidOperationException($"No test for whitelist entry '{entry}'."),
        };
        var port = entry == "Poly.Runtime.IStoragePort.*";
        var generator = port
            ? WithBody(CompileForHost(PortPoly), "Widget", "__BodyOfRecode", ReturnsSuccessAfter(call))
            : WithBody(CompileForHost(LinkedPoly), "Order", "__BodyOfTouch", ReturnsSuccessAfter(call));
        var (owner, action) = port ? ("Widget", "Recode") : ("Order", "Touch");
        var without = ActionReadsWalker.Whitelist.Where(e => $"{e.Receiver}.{e.Method}" != entry).ToList();

        var reads = generator.ReadsOf(owner, action).Render();
        var refusal = Assert.Throws<ActionReadsRefusedException>(() => generator.ReadsOf(owner, action, without));

        await Assert.That(reads).IsEqualTo(expected);
        await Assert.That(refusal.Message).Contains($"Action '{owner}.{action}' cannot be served over HTTP");
    }

    /// <summary>A rename of a result's members or statics fails closed: an unknown name refuses, it does not drop reads.</summary>
    [Test]
    public async Task ActionReads_UnknownResultMemberOrStatic_IsRefused() {
        var compile = CompileForHost(LinkedPoly);
        var customer = new Member(new ThisReference(), "Customer");
        var resultOfCustomer = new NamedTypeReference("DomainResult", TypeArguments: [new NamedTypeReference("Customer")]);
        var unknownMember = ReturnsSuccessAfter(new Assignment(new Variable("v"),
            new Member(new Invoke(new Member(resultOfCustomer, "Success"), customer), "Payload")));
        var unknownStatic = ReturnsSuccessAfter(new Assignment(new Variable("v"),
            new Invoke(new Member(resultOfCustomer, "Ok"), customer)));

        var member = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Order", "__BodyOfTouch", unknownMember).ReadsOf("Order", "Touch"));
        var call = Assert.Throws<ActionReadsRefusedException>(() => WithBody(compile, "Order", "__BodyOfTouch", unknownStatic).ReadsOf("Order", "Touch"));

        await Assert.That(member.Message).Contains("member 'Payload' of a result");
        await Assert.That(call.Message).Contains("which the walk does not know on a result");
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
        // Every assembly the solution builds that can define a node; a kind in a pack outside it refuses at run time.
        var kinds = new[] {
                typeof(Node).Assembly, typeof(MinimalApiGenerator).Assembly, typeof(PolyCompiler).Assembly,
                typeof(Poly.DslCompiler.DslCompiler).Assembly, typeof(Poly.Packs.Sqlite.SqliteLibrary).Assembly,
                typeof(Poly.Packs.SqlServer.SqlServerLibrary).Assembly, typeof(Poly.Packs.MySql.MySqlLibrary).Assembly,
                typeof(Poly.Mcp.Surface.PolySurfaceTools).Assembly, typeof(IStoragePort).Assembly,
            }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Node).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var unclassified = kinds.Where(t => ActionReadsWalker.KindOf(t) is null).Select(t => t.FullName).ToList();

        await Assert.That(kinds.Count).IsGreaterThan(50);
        await Assert.That(unclassified).IsEmpty();
    }

    // ── The sweep ─────────────────────────────────────────────

    /// <summary>The shipped corpora, each through its own enumeration: every <c>*.poly</c> file in the repository
    /// (<see cref="CorpusVerdictSweepTests.PolyFiles"/>: the golden, probe, swarm, demo and pack documents), the DSL guide's
    /// fences composed through their wrappers (<see cref="GuideBlocks"/>), the demo IDE samples, the <c>poly_guide</c>
    /// quickstart, and every <see cref="PackOracleDomains"/> fixture.</summary>
    public static IEnumerable<string> ShippedCorpora() => ["files", "guide", "demo-samples", "quickstart", "pack-oracle"];

    internal static IEnumerable<(string Label, string Poly)> CorpusDocuments(string corpus) {
        switch (corpus) {
            case "files":
                foreach (var file in CorpusVerdictSweepTests.PolyFiles())
                    yield return (file, File.ReadAllText(Path.Combine(GuideBlocks.RepoRoot(), file)));
                break;
            case "guide":
                // Labelled as CorpusVerdictSweepTests labels them: the fence's first line, with its occurrence.
                var guide = GuideBlocks.Load();
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var block in guide.Blocks) {
                    var occurrence = seen[block.FirstLine] = seen.GetValueOrDefault(block.FirstLine) + 1;
                    if (block.Marker is { Kind: GuideMarkerKind.Skip, Code: null })
                        continue;
                    var label = occurrence == 1 ? $"guide fence {block.FirstLine}" : $"guide fence {block.FirstLine} ({occurrence})";
                    yield return (label, GuideBlocks.Compose(block, guide.Wrappers).Text);
                }
                break;
            case "demo-samples":
                foreach (var (name, text) in CorpusVerdictSweepTests.DemoSamples())
                    yield return ($"demo sample {name}", text);
                break;
            case "quickstart":
                yield return ("poly_guide quickstart", CorpusVerdictSweepTests.QuickstartDocument());
                break;
            case "pack-oracle":
                foreach (var property in typeof(PackOracleDomains).GetProperties().Where(p => p.PropertyType == typeof(PackOracleDomain)))
                    yield return (property.Name, ((PackOracleDomain)property.GetValue(null)!).Poly);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corpus), corpus, null);
        }
    }

    internal sealed record CorpusSweep(int Documents, int Compiled, int Actions, IReadOnlyList<string> Refused, IReadOnlyList<string> Skipped);

    /// <summary>Walks every action of every document of <paramref name="corpus"/> that compiles for a host (sqlite,
    /// <c>http</c> loaded). A document that does not compile is skipped: its corpus pins that verdict elsewhere.</summary>
    internal static CorpusSweep SweepCorpus(string corpus) {
        var documents = 0;
        var compiled = 0;
        var actions = 0;
        var refused = new List<string>();
        var skipped = new List<string>();
        foreach (var (label, poly) in CorpusDocuments(corpus)) {
            documents++;
            HostCompile compile;
            try {
                compile = CompileForHost(poly);
            }
            catch (InvalidOperationException) {
                skipped.Add(label);
                continue;
            }
            compiled++;
            var generator = HostGenerator(compile);
            foreach (var entity in BehaviorMetadata.From(compile.Domain, compile.Analysis).Entities) {
                foreach (var action in entity.Actions.Select(a => a.Name).Distinct()) {
                    actions++;
                    try {
                        generator.ReadsOf(entity.Name, action);
                    }
                    catch (ActionReadsRefusedException refusal) {
                        refused.Add($"{label}: {refusal.Message}");
                    }
                }
            }
        }
        return new CorpusSweep(documents, compiled, actions, refused, skipped);
    }

    /// <summary>No action of a shipped document is refused (the owner chose loading over refusing). A known refusal would
    /// be named here with its reason, so a new one fails the test; there is none.</summary>
    private static readonly IReadOnlyList<string> KnownRefusals = [];

    /// <summary>The documents each corpus has that do not compile for a host (their verdicts are pinned by their own
    /// corpus tests). Named, so a shipped document that stops or starts compiling fails this test by name.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> KnownSkips = new Dictionary<string, string[]> {
        ["files"] = [
            "docs/missions/evolve-2026-10-13/ambig-backref.poly",
            "docs/missions/evolve-2026-10-13/billing.poly",
            "docs/missions/evolve-2026-10-13/claims.poly",
            "docs/missions/evolve-2026-10-13/disputes.poly",
            "docs/missions/evolve-2026-10-13/library.poly",
            "docs/missions/evolve-2026-10-13/university-http.poly",
            "docs/missions/evolve-2026-10-13/university.poly",
            "docs/probes/dogfood/nested-invoke-type-mismatch.poly",
            "docs/probes/scratch-2026-10-14/restaurant.poly",
        ],
        ["guide"] = [
            "guide fence Money: value {",
            "guide fence Billing: contract internal billing v1 {",
            "guide fence Orders: entity {",
            "guide fence Customer: entity {",
            "guide fence Cabin: entity {",
            "guide fence domain Shipping",
            "guide fence domain Tokens",
            "guide fence // ✅ Correct — create is the final statement",
            "guide fence invoke Validate                              # self-only",
            "guide fence Renew: action {",
            "guide fence domain Board",
        ],
        ["demo-samples"] = [],
        ["quickstart"] = [],
        ["pack-oracle"] = [],
    };

    [Test]
    [MethodDataSource(nameof(ShippedCorpora))]
    public async Task ActionReads_EveryShippedAction_IsNotRefused(string corpus) {
        var sweep = SweepCorpus(corpus);

        await Assert.That(sweep.Compiled).IsGreaterThan(0);
        await Assert.That(sweep.Refused).IsEquivalentTo(KnownRefusals);
        await Assert.That(sweep.Skipped).IsEquivalentTo(KnownSkips[corpus]);
    }
}