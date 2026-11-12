using Poly.Tests.TestHelpers;

namespace Poly.Tests.Quality;

/// <summary>A golden domain that cannot be booted as an app: <paramref name="Issue"/> names the open issue,
/// <paramref name="Owner"/> the slice that fixes it, and <paramref name="Reason"/> starts <c>#&lt;issue&gt;:</c>.</summary>
public sealed record NoBootDomain(string Domain, int Issue, string Owner, string Reason);

/// <summary>
/// The golden corpus as <see cref="PackOracleDomain"/> fixtures. A golden domain declares neither
/// <c>uses http</c> nor <c>uses sqlite</c>, so each fixture is derived: <c>uses persistence</c> is replaced by
/// <c>uses sqlite</c> (the two both register the storage pass and must never load together), the other
/// <c>uses</c> lines are kept, and <c>uses http</c> and then the vendor are added last.
/// </summary>
public static class GoldenPackDomains {
    public const string Provider = "sqlite";

    /// <summary>Domains that cannot boot as an app. Empty until Step 0 observes one.</summary>
    public static IReadOnlyList<NoBootDomain> NoBoot { get; } = [];

    /// <summary>The PR-tier boot set: exactly two golden domains, both with a unique-key root entity and an action.</summary>
    public static IReadOnlyList<GoldenDomain> Subset { get; } =
        [.. GoldenDomains.Compilable.Where(d => d.Name is "mcp-library" or "warehouse")];

    /// <summary>Every <see cref="GoldenDomains.Compilable"/> domain that boots.</summary>
    public static IReadOnlyList<GoldenDomain> Full { get; } =
        [.. GoldenDomains.Compilable.Where(d => NoBoot.All(n => n.Domain != d.Name))];

    public static PackOracleDomain FromGolden(GoldenDomain golden) {
        ArgumentNullException.ThrowIfNull(golden);
        return new PackOracleDomain("golden-" + golden.Name, golden.Poly, RequestMatrix.For(golden.Name).Entities[0].Route);
    }
}