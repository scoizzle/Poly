namespace Poly.Tests.Polyc;

internal static class PolycTestSupport {
    public static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Poly.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Poly.slnx not found above the test directory.");
    }

    public static string Hotel() => File.ReadAllText(Path.Combine(RepoRoot(), "docs/probes/dogfood/hotel.poly"));

    public const string Orders = "domain Orders\n\nOrder: entity {\n  Number: Text unique\n}\n";

    /// <summary>
    /// The same document without <c>unique</c>, so a seed that omits <c>persistence</c> still
    /// compiles (C-05). The seed-selection tests use this one; a <c>unique</c> document is
    /// <c>DMLIB001</c> unless <c>persistence</c> or a vendor library is loaded.
    /// </summary>
    public const string UniquelessOrders = "domain Orders\n\nOrder: entity {\n  Number: Text\n}\n";

    /// <summary>Each pair yields one DMEFF011: a create-in that omits the child's required property.</summary>
    public static string ManyErrors(int count) =>
        "domain Broken\n\n" + string.Join("\n", Enumerable.Range(1, count).Select(i =>
            $"P{i}: entity {{\n  Name: Text\n  kids: many K{i}\n  Make: action {{\n    create in kids {{ }}\n  }}\n}}\n\nK{i}: entity {{\n  Need: Text\n}}\n"));

    public static string TempDir() {
        var path = Path.Combine(Path.GetTempPath(), "polyc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}