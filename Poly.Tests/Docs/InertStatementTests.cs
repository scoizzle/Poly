using System.Text.RegularExpressions;

namespace Poly.Tests.Docs;

/// <summary>
/// <see cref="Poly.Ast.Nodes.Comment"/> is a source comment the C# reader keeps and the writer
/// prints. It is **not** a lowering output: `AGENTS.md` forbids a placeholder that records "what was
/// not lowered". Nothing enforced that half, and a lowering pass could synthesise one and every test
/// would still pass, because a comment is inert.
/// <para>
/// The guard is scoped to the producers that must never emit one — `Poly/DomainModeling` and
/// `Poly.Mcp`. The C# reader is a legitimate producer. A source scan, not reflection, because the
/// question is "who writes this node".
/// </para>
/// </summary>
public partial class InertStatementTests {
    /// <summary>Assemblies where a Comment would be a lowering-gap marker.</summary>
    private static readonly string[] LoweringRoots = ["Poly/DomainModeling", "Poly.Mcp", "src"];

    [Test]
    public async Task NoLoweringSource_ConstructsAComment() {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var scope in LoweringRoots) {
            var directory = Path.Combine(root, scope.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)) {
                if (IsBuildOutput(file))
                    continue;
                var number = 0;
                foreach (var line in File.ReadLines(file)) {
                    number++;
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || trimmed.StartsWith("/*", StringComparison.Ordinal))
                        continue;
                    if (Constructor().IsMatch(line))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{number}: {line.Trim()}");
                }
            }
        }
        await Assert.That(string.Join("\n", offenders)).IsEqualTo(string.Empty)
            .Because("lowering models the effect or fails closed; a comment is source, not meaning");
    }

    /// <summary>The reader is the producer the node exists for, so it must stay one.</summary>
    [Test]
    public async Task TheCSharpReader_ProducesComments() {
        var body = Poly.Interpretation.CSharp.CSharpReader
            .Read("class C { void M() { // note\n } }")
            .Types.OfType<Poly.Ast.Nodes.TypeDefinitionNode>().First()
            .Methods!.First().Body as Poly.Ast.Nodes.Block;
        await Assert.That(body!.Nodes.OfType<Poly.Ast.Nodes.Comment>().Count()).IsEqualTo(1);
    }

    private static bool IsBuildOutput(string path) {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }

    /// <summary>`new Comment(` — the only way shipped code can synthesise one.</summary>
    [GeneratedRegex(@"\bnew\s+Comment\s*\(")]
    private static partial Regex Constructor();

    private static string RepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Poly.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Poly.slnx not found above the test output directory");
    }
}