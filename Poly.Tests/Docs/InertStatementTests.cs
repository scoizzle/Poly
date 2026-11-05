using System.Text.RegularExpressions;

using Poly.Ast.Nodes;
using Poly.Interpretation;

namespace Poly.Tests.Docs;

/// <summary>
/// <see cref="Comment"/> is the inert-statement fixture the VM contract is tested against, and
/// `AGENTS.md` forbids it as a lowering output ("a <c>Comment</c> for meaning that failed to lower"
/// is a hard no). Nothing enforced the second half: a lowering path could start synthesising one
/// and every test would still pass, because a Comment is inert.
/// <para>
/// This is that guard. It reads the shipped sources and fails when anything outside the test
/// project constructs a <c>Comment</c>. It is a source scan rather than a reflection check because
/// the question is "who writes this node", not "who references the type".
/// </para>
/// </summary>
public partial class InertStatementTests {
    /// <summary>Assemblies that ship: anything here constructing a Comment is a lowering output.</summary>
    private static readonly string[] ShippedRoots = ["Poly", "Poly.Mcp", "src"];

    [Test]
    public async Task NoShippedSource_ConstructsAComment() {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var scope in ShippedRoots) {
            var directory = Path.Combine(root, scope);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)) {
                if (IsBuildOutput(file))
                    continue;
                var number = 0;
                foreach (var line in File.ReadLines(file)) {
                    number++;
                    // Skip comment lines: prose about the rule is not a producer.
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
            .Because("a Comment is the inert fixture, not a lowering output: model the effect or fail closed");
    }

    /// <summary>
    /// The type must stay registered, or the totality test would not know it emits nothing and
    /// the emitter's tolerating arms would become unreachable code nobody notices.
    /// </summary>
    [Test]
    public async Task Comment_IsRegisteredAsInert() {
        await Assert.That(NodeFacts.All.TryGetValue(typeof(Comment), out var fact)).IsTrue();
        await Assert.That(fact!.Effects).IsEqualTo(NodeEffect.None);
        await Assert.That(NodeFacts.Throws(new Comment("x"))).IsFalse();
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