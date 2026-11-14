// SCAFFOLDING (P-13b fix round): every tree of every shipped corpus; deleted in the last commit.
using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.Packs.Product;
using Poly.Tests.Docs;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests;

public sealed class P13bTreeDump {
    [Test]
    public async Task Dump() {
        var outFile = Environment.GetEnvironmentVariable("P13B_TREES")!;
        var lines = new List<string>();
        var counts = new List<string>();
        foreach (var corpus in new[] { "files", "guide", "demo-samples", "quickstart", "pack-oracle" }) {
            int documents = 0, compiled = 0, actions = 0, refused = 0;
            foreach (var (label, poly) in Documents(corpus)) {
                documents++;
                var catalog = ProductCatalog.Create("sqlite");
                var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite", HostIds = ["http"] };
                var result = new PolyCompiler(catalog, options).Compile(poly);
                if (!result.Succeeded)
                    continue;
                compiled++;
                var domain = result.Domain!;
                var analysis = result.Analysis!;
                var behavior = BehaviorMetadata.From(domain, analysis);
                var generator = new MinimalApiGenerator(domain, analysis,
                    analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage, behavior,
                    analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate,
                    catalog: new CatalogIndex(result.Session!.ArtifactCatalog));
                foreach (var entity in behavior.Entities) {
                    foreach (var action in entity.Actions.Select(a => a.Name).Distinct()) {
                        actions++;
                        string tree;
                        try {
                            tree = generator.ReadsOf(entity.Name, action).Render();
                        }
                        catch (InvalidOperationException refusal) {
                            refused++;
                            tree = "REFUSED " + refusal.Message;
                        }
                        lines.Add($"{corpus} | {label} | {entity.Name}.{action} = {tree}");
                    }
                }
            }
            counts.Add($"{corpus}: documents={documents} compiled={compiled} skipped={documents - compiled} actions={actions} refused={refused}");
        }
        File.WriteAllLines(outFile, [.. counts, "", .. lines]);
        await Assert.That(lines.Count).IsGreaterThan(0);
    }

    private static IEnumerable<(string Label, string Poly)> Documents(string corpus) {
        switch (corpus) {
            case "files":
                foreach (var file in CorpusVerdictSweepTests.PolyFiles())
                    yield return (file, File.ReadAllText(Path.Combine(GuideBlocks.RepoRoot(), file)));
                break;
            case "guide":
                var guide = GuideBlocks.Load();
                foreach (var block in guide.Blocks.Where(b => b.Marker is not { Kind: GuideMarkerKind.Skip, Code: null }))
                    yield return ($"guide fence {block.FirstLine}", GuideBlocks.Compose(block, guide.Wrappers).Text);
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
        }
    }
}