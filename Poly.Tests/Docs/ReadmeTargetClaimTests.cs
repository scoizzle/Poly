using System.Text.RegularExpressions;

using Poly.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.Docs;

/// <summary>
/// C-07: the README's host-target claims are the one hand-kept copy of the catalog's target
/// list. These tests read the real README and <see cref="ProductCatalog.Catalog"/>, the catalog
/// <c>polyc</c> compiles against, so the prose cannot drift from it.
/// </summary>
public sealed class ReadmeTargetClaimTests {
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The README is hard-wrapped, so every claim is read from whitespace-normalised text.</summary>
    private static string ReadmeText() =>
        Whitespace.Replace(File.ReadAllText(Path.Combine(GuideBlocks.RepoRoot(), "README.md")), " ");

    private static string TargetBullet() {
        var text = ReadmeText();
        const string marker = "- **`--target <id>`**";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("The README has no --target bullet.");
        var next = text.IndexOf(" - **", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    [Test]
    public async Task Readme_KnownTargetsSentence_EqualsTheCatalog() {
        var bullet = TargetBullet();
        var at = bullet.IndexOf("Known targets:", StringComparison.Ordinal);
        await Assert.That(at).IsGreaterThanOrEqualTo(0);

        var sentence = bullet[(at + "Known targets:".Length)..];
        sentence = sentence[..sentence.IndexOf('.', StringComparison.Ordinal)];
        var listed = Regex.Matches(sentence, "`([^`]+)`").Select(m => m.Groups[1].Value).ToList();

        var known = ProductCatalog.Catalog.KnownTargets;
        await Assert.That(known.Count).IsGreaterThan(0);
        await Assert.That(listed.Count).IsGreaterThan(0);
        await Assert.That(listed.SequenceEqual(known, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Readme_TargetBullet_NamesNoUnshippedTarget() {
        var bullet = TargetBullet();

        await Assert.That(bullet).Contains("`--target <id>`");
        await Assert.That(bullet).DoesNotContain("windows");
    }

    [Test]
    public async Task Readme_CodesSentence_NamesTheUnknownTargetCode() {
        var text = ReadmeText();
        var start = text.IndexOf("Codes:", StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThanOrEqualTo(0);
        var end = text.IndexOf(" - **", start, StringComparison.Ordinal);
        var codes = end < 0 ? text[start..] : text[start..end];

        await Assert.That(codes).Contains("`DSL0004`");
        await Assert.That(codes).DoesNotContain("--target");
        await Assert.That(codes).Contains($"`{DiagnosticCodes.UnknownTarget}`");
    }
}