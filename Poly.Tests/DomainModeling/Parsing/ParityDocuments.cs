using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Packs.Product;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// The valid documents G-26b's parity oracles run over: every shipped <c>.poly</c> (the 42 under <c>demo/</c> and <c>docs/</c>) and
/// G-13a's five sweep documents, each resolved through the same session door as <c>ResolverBaselineTests</c>.
/// </summary>
internal static class ParityDocuments {
    public static IReadOnlyList<(string Key, string Text)> All { get; } = Load();

    private static List<(string, string)> Load() {
        var root = GuideBlocks.RepoRoot();
        var documents = new List<(string, string)>();
        foreach (var folder in new[] { "demo", "docs" }) {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, folder), "*.poly", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                documents.Add((Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
        }

        foreach (var (name, text) in ResolverBaselineTests.SweepDocuments)
            documents.Add(("sweep:" + name, text));
        return documents;
    }

    public static List<DomainChange> Resolve(string text) {
        var session = DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring, ProductCatalog.Catalog);
        return new DomainResolver(DslSyntaxTree.Parse(text), session).Resolve();
    }
}