// SCAFFOLDING (P-13b fix round, F1 probe): deleted in the last commit.
using System.Text;
using System.Text.Json;

using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.Packs.Product;

namespace Poly.Tests;

public sealed class P13bF1Probe {
    private const string Poly = """
        domain Watch
        uses sqlite
        uses http
        Zone: entity { Label: Text }
        Region: entity {
          Name: Text
          zone: Zone
          SetZone: action -> Zone { create in zone { Label: "north" } }
          Copy: action { assign Name to zone Label }
        }
        Watcher: entity {
          Code: Text unique
          region: Region
          Seen: Text optional
          items: many Item
          when items Open { invoke region.Copy }
          SetRegion: action -> Region { create in region { Name: "r" } }
          Add: action -> Item { create in items { } }
        }
        Item: entity {
          Tag: Text optional
          watcher: Watcher via items
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    private static string Out => Environment.GetEnvironmentVariable("P13B_DUMP")!;

    [Test]
    public async Task Lowered() {
        Directory.CreateDirectory(Out);
        var catalog = ProductCatalog.Create("sqlite");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite", HostIds = ["http"] };
        var compiled = new PolyCompiler(catalog, options).Compile(Poly);
        var log = new StringBuilder();
        log.AppendLine("succeeded=" + compiled.Succeeded + " " + string.Join("; ", compiled.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        if (compiled.Succeeded) {
            foreach (var (name, source) in compiled.Files)
                File.WriteAllText(Path.Combine(Out, "watch-" + name), source);
            var domain = compiled.Domain!;
            var analysis = compiled.Analysis!;
            var generator = new MinimalApiGenerator(domain, analysis,
                analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage,
                BehaviorMetadata.From(domain, analysis),
                analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate,
                catalog: new CatalogIndex(compiled.Session!.ArtifactCatalog));
            log.AppendLine("Watcher.Add = " + generator.ReadsOf("Watcher", "Add").Render());
        }
        File.WriteAllText(Path.Combine(Out, "watch-probe.txt"), log.ToString());
        await Assert.That(compiled.Succeeded).IsTrue();
    }

    [Test]
    public async Task Simulator() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("watch");
        var defined = harness.Define(workspace, Poly);
        var log = new StringBuilder("define=" + defined.Success + "\n");
        var watcher = harness.Create(workspace, "Watcher", """{"Code":"W1"}""").InstanceId;
        var region = harness.Invoke(workspace, watcher, "SetRegion");
        log.AppendLine("SetRegion: " + region.Summary);
        var regionId = region.Result.ReturnInstanceId!;
        log.AppendLine("regionId=" + regionId);
        var zone = harness.Invoke(workspace, regionId, "SetZone");
        log.AppendLine("SetZone: " + zone.Summary);
        var add = harness.Invoke(workspace, watcher, "Add");
        log.AppendLine("Add: " + add.Summary + " succeeded=" + add.Result.Succeeded + " " + add.Result.ErrorMessage);
        log.AppendLine("watcher after: " + harness.Get(workspace, watcher).Data?.ToJsonString());
        log.AppendLine("region after: " + harness.Get(workspace, regionId).Data?.ToJsonString());
        File.WriteAllText(Path.Combine(Out, "watch-sim.txt"), log.ToString());
        await Assert.That(defined.Success).IsTrue();
    }

    [Test]
    public async Task Http() {
        var fixture = new PackOracleDomain("WatchF1", Poly, "/api/watchers");
        await using var oracle = await PackOracle.BootAsync(fixture);
        static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
        var log = new StringBuilder();
        async Task Step(string label, Task<HttpResponseMessage> call) {
            using var response = await call;
            log.AppendLine($"{label}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
        await Step("create watcher", oracle.Client.PostAsync("/api/watchers", Json("""{"Code":"W1"}""")));
        await Step("setregion", oracle.Client.PostAsync("/api/watchers/W1/setregion", Json("{}")));
        await Step("regions", oracle.Client.GetAsync("/api/watchers/W1/region"));
        await Step("setzone", oracle.Client.PostAsync("/api/watchers/W1/region/setzone", Json("{}")));
        await Step("add", oracle.Client.PostAsync("/api/watchers/W1/add", Json("{}")));
        await Step("watcher", oracle.Client.GetAsync("/api/watchers/W1"));
        await Step("region after", oracle.Client.GetAsync("/api/watchers/W1/region"));
        await Step("items after", oracle.Client.GetAsync("/api/watchers/W1/items"));
        log.AppendLine("stderr: " + oracle.ErrorTail);
        File.WriteAllText(Path.Combine(Out, "watch-http.txt"), log.ToString());
        await Assert.That(log.Length).IsGreaterThan(0);
    }
}