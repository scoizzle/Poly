// SCAFFOLDING (P-13b step 0): deleted in the last commit.
using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests;

public sealed class P13bStep0Dump {
    [Test]
    public async Task Dump() {
        var outDir = Environment.GetEnvironmentVariable("P13B_DUMP")!;
        Directory.CreateDirectory(outDir);
        var histogram = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var trees = new List<string>();
        foreach (var doc in GoldenDomains.Lowerable) {
            var catalog = ProductCatalog.Create("sqlite");
            var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) {
                StorageVendor = "sqlite",
                HostIds = ["http"],
            };
            var compiled = new PolyCompiler(catalog, options).Compile(doc.Poly, doc.Name + ".poly");
            if (!compiled.Succeeded) {
                File.AppendAllText(Path.Combine(outDir, "failed.txt"), doc.Path + ": " + string.Join("; ", compiled.Diagnostics.Where(d => d.IsError).Select(d => d.Message)) + "\n");
                continue;
            }
            var dir = Path.Combine(outDir, doc.Name);
            Directory.CreateDirectory(dir);
            foreach (var (name, source) in compiled.Files)
                File.WriteAllText(Path.Combine(dir, name), source);
            foreach (var artifact in compiled.Session!.ArtifactCatalog.Artifacts) {
                if (artifact.Descriptor.Id.Type is not ("entity" or "scaffolding"))
                    continue;
                foreach (var type in (IReadOnlyList<TypeDefinitionNode>)artifact.Payload!)
                    Walk(type, doc.Name);
            }
            var domain = compiled.Domain!;
            var analysis = compiled.Analysis!;
            var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
            var behavior = BehaviorMetadata.From(domain, analysis);
            var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
            var generator = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate,
                catalog: new CatalogIndex(compiled.Session!.ArtifactCatalog));
            foreach (var entity in behavior.Entities)
                foreach (var action in entity.Actions.Select(a => a.Name).Distinct())
                    trees.Add($"{doc.Name}: {entity.Name}.{action} = {generator.ReadsOf(entity.Name, action).Render()}");
        }
        File.WriteAllLines(Path.Combine(outDir, "kinds.txt"),
            histogram.Select(kv => $"{kv.Key}: {string.Join(",", kv.Value)}"));
        File.WriteAllLines(Path.Combine(outDir, "trees.txt"), trees);
        await Assert.That(histogram.Count).IsGreaterThan(0);

        void Walk(Node? node, string doc) {
            if (node is null)
                return;
            var name = node.GetType().Name;
            if (!histogram.TryGetValue(name, out var docs))
                histogram[name] = docs = new SortedSet<string>(StringComparer.Ordinal);
            docs.Add(doc);
            foreach (var child in node.Children)
                Walk(child, doc);
            if (node is PropertyDefinitionNode p) {
                Walk(p.Getter?.Body, doc);
            }
        }
    }
}