// SCAFFOLDING (P-13b fix round 2 probes): deleted in the last commit.
using System.Text;

using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.Packs.Product;

namespace Poly.Tests;

public sealed class P13bR2Probe {
    internal const string Spawn = """
        domain Spawn
        Project: entity {
          tasks: many Task via project
          Idle: stage { Start: action { transition to Running  create in tasks { } } }
          Running: stage { }
        }
        Task: entity {
          Tag: Text optional
          project: Project via tasks
          parent: Task
          kids: many Task via parent
          when project Running { create in kids { project: project } }
        }
        """;

    internal const string Alias = """
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

    private static string Out => Environment.GetEnvironmentVariable("P13B_DUMP")!;

    private static (CompileResult, MinimalApiGenerator?) Compile(string poly, string name) {
        Directory.CreateDirectory(Out);
        var catalog = ProductCatalog.Create("sqlite");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite", HostIds = ["http"] };
        var compiled = new PolyCompiler(catalog, options).Compile(poly);
        File.WriteAllText(Path.Combine(Out, name + "-compile.txt"), "succeeded=" + compiled.Succeeded + "\n"
            + string.Join("\n", compiled.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        if (!compiled.Succeeded)
            return (compiled, null);
        foreach (var (file, source) in compiled.Files)
            File.WriteAllText(Path.Combine(Out, name + "-" + file), source);
        var domain = compiled.Domain!;
        var analysis = compiled.Analysis!;
        return (compiled, new MinimalApiGenerator(domain, analysis,
            analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage,
            BehaviorMetadata.From(domain, analysis),
            analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate,
            catalog: new CatalogIndex(compiled.Session!.ArtifactCatalog)));
    }

    [Test]
    public async Task SpawnCompiles() {
        var (compiled, _) = Compile(Spawn, "spawn");
        await Assert.That(compiled.Diagnostics).IsNotNull();
    }

    [Test]
    public async Task AliasCompilesAndWalks() {
        var (compiled, generator) = Compile(Alias, "alias");
        var text = generator is null ? "no compile" : generator.ReadsOf("Order", "Go").Render();
        File.WriteAllText(Path.Combine(Out, "alias-tree.txt"), text);
        await Assert.That(compiled.Diagnostics).IsNotNull();
    }

    [Test]
    public async Task SpawnWalks() {
        var (_, generator) = Compile(Spawn, "spawn");
        File.WriteAllText(Path.Combine(Out, "spawn-tree.txt"), "started\n");
        var text = generator is null ? "no compile" : generator.ReadsOf("Project", "Start").Render();
        File.WriteAllText(Path.Combine(Out, "spawn-tree.txt"), text);
        await Assert.That(text).IsNotNull();
    }

    [Test]
    public async Task Simulate() {
        var log = new StringBuilder();
        var harness = PolySurfaceHarness.New();
        var spawn = harness.Open("spawn");
        log.AppendLine("spawn define=" + harness.Define(spawn, Spawn).Success);
        var project = harness.Create(spawn, "Project", "{}").InstanceId;
        var start = harness.Invoke(spawn, project, "Start");
        log.AppendLine("Start: " + start.Summary);
        log.AppendLine("tasks: " + harness.List(spawn, "Task").Data?.ToJsonString());

        var alias = harness.Open("alias");
        log.AppendLine("alias define=" + harness.Define(alias, Alias).Success);
        var zone = harness.Create(alias, "Zone", """{"Label":"north"}""").InstanceId;
        var product = harness.Create(alias, "Product", """{"Name":"p"}""").InstanceId;
        log.AppendLine("link zone: " + harness.Link(alias, product, "zone", zone).Summary);
        var order = harness.Create(alias, "Order", "{}").InstanceId;
        log.AppendLine("link product: " + harness.Link(alias, order, "product", product).Summary);
        var go = harness.Invoke(alias, order, "Go", $$"""{"p":"{{product}}"}""");
        log.AppendLine("Go: " + go.Summary);
        log.AppendLine("product after: " + harness.Get(alias, product).Data?.ToJsonString());
        File.WriteAllText(Path.Combine(Out, "r2-sim.txt"), log.ToString());
        await Assert.That(log.Length).IsGreaterThan(0);
    }

    internal const string OrderingDoc = """
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

    internal const string PublisherDoc = """
        domain Pub
        Product: entity {
          Name: Text
          lines: many Line via product
          Open: stage { Close: action { transition to Closed } }
          Closed: stage { }
        }
        Line: entity {
          product: Product
          order: Order via lines
          Seen: Number default(0)
          when product Closed as pr { assign Seen to count pr lines }
        }
        Order: entity {
          product: Product
          lines: many Line via order
          Go: action { create in lines { product: product }  invoke product.Close }
        }
        """;

    internal const string ChainDoc = """
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

    internal const string OneHopDoc = """
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

    [Test]
    public async Task Rows() {
        var log = new StringBuilder();
        {
            var (c, g) = Compile(OrderingDoc, "ordering");
            log.AppendLine("Ordering: compiled=" + c.Succeeded + " " + string.Join("; ", c.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            if (g is not null) {
                try { log.AppendLine("  tree=" + g.ReadsOf("Project", "Start").Render()); }
                catch (Exception ex) { log.AppendLine("  " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        {
            var (c, g) = Compile(PublisherDoc, "publisher");
            log.AppendLine("Publisher: compiled=" + c.Succeeded + " " + string.Join("; ", c.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            if (g is not null) {
                try { log.AppendLine("  tree=" + g.ReadsOf("Order", "Go").Render()); }
                catch (Exception ex) { log.AppendLine("  " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        {
            var (c, g) = Compile(ChainDoc, "chain");
            log.AppendLine("Chain: compiled=" + c.Succeeded + " " + string.Join("; ", c.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            if (g is not null) {
                try { log.AppendLine("  tree=" + g.ReadsOf("Watcher", "Add").Render()); }
                catch (Exception ex) { log.AppendLine("  " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        {
            var (c, g) = Compile(OneHopDoc, "onehop");
            log.AppendLine("OneHop: compiled=" + c.Succeeded + " " + string.Join("; ", c.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            if (g is not null) {
                try { log.AppendLine("  tree=" + g.ReadsOf("Watcher", "Add").Render()); }
                catch (Exception ex) { log.AppendLine("  " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        File.WriteAllText(Path.Combine(Out, "rows.txt"), log.ToString());
        await Assert.That(log.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task SimulateOrdering() {
        var harness = PolySurfaceHarness.New();
        var w = harness.Open("proj");
        var log = new StringBuilder("define=" + harness.Define(w, OrderingDoc).Success + "\n");
        var zone = harness.Create(w, "Zone", """{"Label":"north"}""").InstanceId;
        var region = harness.Create(w, "Region", """{"Name":"r"}""").InstanceId;
        log.AppendLine(harness.Link(w, region, "zone", zone).Summary);
        var project = harness.Create(w, "Project", "{}").InstanceId;
        log.AppendLine("Start: " + harness.Invoke(w, project, "Start", $$"""{"r":"{{region}}"}""").Summary);
        log.AppendLine("region after: " + harness.Get(w, region).Data?["instance"]?["properties"]?.ToJsonString());
        File.WriteAllText(Path.Combine(Out, "ordering-sim.txt"), log.ToString());
        await Assert.That(log.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Skips() {
        var lines = new List<string>();
        foreach (var corpus in Poly.Tests.DomainModeling.Lowering.MinimalApiGeneratorTests.ShippedCorpora()) {
            var sweep = Poly.Tests.DomainModeling.Lowering.MinimalApiGeneratorTests.SweepCorpus(corpus);
            lines.Add($"## {corpus}: documents={sweep.Documents} compiled={sweep.Compiled} actions={sweep.Actions} refused={sweep.Refused.Count}");
            lines.AddRange(sweep.Skipped.Select(x => "SKIP " + x));
            lines.AddRange(sweep.Refused.Select(x => "REFUSED " + x));
        }
        File.WriteAllLines(Path.Combine(Out, "skips.txt"), lines);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }
}