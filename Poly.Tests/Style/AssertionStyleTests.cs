using System.Text.RegularExpressions;

namespace Poly.Tests.Style;

public class AssertionStyleTests
{
    private static readonly Regex[] BannedPatterns =
    [
        // Direct old-style assertions
        new(@"\bAssert\.(Equal|NotEqual|True|False|Null|NotNull|AreEqual|IsNull|IsNotNull)\s*\(", RegexOptions.Compiled),
        // NUnit Is usage
        new(@"using\s+static\s+NUnit\.Framework\.Is\s*;", RegexOptions.Compiled),
        new(@"\bIs\.(EqualTo|Not|Null|NotNull)\b", RegexOptions.Compiled),
        // TUnit old EqualTo without the Is prefix
        new(@"\.EqualTo\s*\(", RegexOptions.Compiled)
    ];

    [Test]
    public async Task No_Banned_Assertion_Patterns()
    {
        var root = GetTestsRoot();
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains("/bin/") && !p.Contains("/obj/"))
            .ToArray();

        var violations = new List<string>();

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file);
            foreach (var regex in BannedPatterns)
            {
                foreach (Match m in regex.Matches(text))
                {
                    violations.Add($"{file}: '{m.Value.Trim()}'");
                }
            }

            // Ensure Assert.That calls are awaited (simple line-based check)
            var lines = await File.ReadAllLinesAsync(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("Assert.That(") && !line.Contains("await "))
                {
                    violations.Add($"{file}({i + 1}): 'Assert.That' should be awaited");
                }
            }
        }

        await Assert.That(violations.Count).IsEqualTo(0);
    }

    private static string GetTestsRoot()
    {
        // During test runs, BaseDirectory is typically bin/<config>/<tfm>.
        // Go up to the project directory.
        var baseDir = AppContext.BaseDirectory;
        var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        return projectDir;
    }
}