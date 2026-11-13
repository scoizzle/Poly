using System.Text.Json.Nodes;

using Poly.Compile;

using Polyc;

namespace Poly.Tests.Polyc;

public class PolycCliTests {
    private sealed class ThrowingReader : TextReader {
        public override string? ReadLine() => throw new InvalidOperationException("stdin was read");
        public override string ReadToEnd() => throw new InvalidOperationException("stdin was read");
        public override int Read() => throw new InvalidOperationException("stdin was read");
    }

    private static (int Exit, string Out, string Err) Run(string[] args, string? stdin = null) {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = PolycApp.Run(args, stdin is null ? new ThrowingReader() : new StringReader(stdin), stdin is not null, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static string Write(string dir, string name, string text) {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Test]
    public async Task Check_Errors_ExitsOneAndWritesNothing() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "broken.poly", PolycTestSupport.ManyErrors(2));
        var (exit, stdout, _) = Run(["check", path]);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(stdout).Contains($"{path}:");
        await Assert.That(stdout).Contains(": error DMEFF011: ");
        await Assert.That(Directory.GetFileSystemEntries(dir).Length).IsEqualTo(1);
        await Assert.That(Directory.Exists("polyc-out")).IsFalse();
    }

    [Test]
    public async Task Check_WarningsOnly_ExitsZeroUnlessWarnAsError() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "hotel.poly", PolycTestSupport.Hotel());
        var (exit, stdout, _) = Run(["check", path]);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(stdout).Contains(": warning DMREL002: ");
        await Assert.That(stdout).DoesNotContain("DMAS001");
        await Assert.That(Run(["check", path, "--warn-as-error"]).Exit).IsEqualTo(1);
        await Assert.That(Run(["check", path, "--verbosity", "hint"]).Out).Contains("DMAS001");
    }

    [Test]
    public async Task Json_AndText_NameTheSameCodes() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "broken.poly", PolycTestSupport.ManyErrors(12));
        var text = Run(["check", path]).Out;
        var diagnostics = JsonNode.Parse(Run(["check", path, "--format", "json"]).Out)!.AsArray();
        await Assert.That(diagnostics.Count).IsGreaterThanOrEqualTo(12);
        // Text shows errors and warnings by default; JSON is never filtered.
        var shown = diagnostics.Select(d => DiagnosticFormat.FromJson(d!)).Where(d => d.IsError || d.IsWarning).ToList();
        var textLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(textLines.Length).IsEqualTo(shown.Count);
        await Assert.That(diagnostics.Count).IsGreaterThan(shown.Count);
        for (var i = 0; i < shown.Count; i++)
            await Assert.That(textLines[i]).IsEqualTo(DiagnosticFormat.Text(shown[i]));
        await Assert.That(shown.Any(d => d.IsError)).IsTrue();
    }

    [Test]
    public async Task Build_WritesDomainDirectory() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);
        var outDir = Path.Combine(dir, "out");
        var (exit, _, _) = Run(["build", path, "--out", outDir]);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(outDir, "Orders", "Order.cs"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(outDir, "Orders", "Poly.Types.cs"))).IsTrue();
    }

    [Test]
    public async Task Build_ArtifactFilter_KeepsEachUnitsCatalog() {
        var dir = PolycTestSupport.TempDir();
        Write(dir, "alpha.poly", "domain Alpha\nuses sqlite\n\nWidget: entity {\n  Name: Text\n}\n");
        Write(dir, "beta.poly", "domain Beta\nuses sqlite\n\nGadget: entity {\n  Name: Text\n}\n");
        var outDir = Path.Combine(dir, "out");
        var (exit, _, _) = Run(["build", dir, "--out", outDir, "--artifact", "file"]);
        await Assert.That(exit).IsEqualTo(0);
        var alpha = Directory.GetFiles(Path.Combine(outDir, "Alpha")).Select(Path.GetFileName).ToList();
        await Assert.That(alpha).Contains("Widget.cs");
        await Assert.That(alpha).DoesNotContain("Gadget.cs");
        await Assert.That(alpha.Any(name => name!.Contains("Beta"))).IsFalse();
        var beta = Directory.GetFiles(Path.Combine(outDir, "Beta")).Select(Path.GetFileName).ToList();
        await Assert.That(beta).Contains("Gadget.cs");
        await Assert.That(beta).DoesNotContain("Widget.cs");
    }

    [Test]
    public async Task Json_IsAnArrayOfRecords_AndBuildWritesPathsToStderr() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);
        var check = JsonNode.Parse(Run(["check", path, "--format", "json"]).Out);
        await Assert.That(check).IsTypeOf<JsonArray>();

        var outDir = Path.Combine(dir, "out");
        var (exit, _, err) = Run(["build", path, "--out", outDir, "--format", "json"]);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(err).Contains(Path.Combine(outDir, "Orders", "Order.cs"));
    }

    [Test]
    public async Task Build_ArtifactFilter_WritesOnlyThatType() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);
        var outDir = Path.Combine(dir, "out");
        var (exit, _, _) = Run(["build", path, "--out", outDir, "--artifact", "analysis-report"]);
        await Assert.That(exit).IsEqualTo(0);
        var files = Directory.GetFiles(Path.Combine(outDir, "Orders")).Select(Path.GetFileName).ToList();
        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0]!).EndsWith(".analysis-report.json");
        await Assert.That(Run(["build", path, "--out", outDir, "--artifact", "nope"]).Exit).IsEqualTo(1);
    }

    [Test]
    public async Task Build_PathCollision_FailsClosedWritingNothing() {
        var dir = PolycTestSupport.TempDir();
        Write(dir, "a.poly", PolycTestSupport.Orders);
        Write(dir, "b.poly", PolycTestSupport.Orders);
        var outDir = Path.Combine(dir, "out");
        var (exit, stdout, _) = Run(["build", dir, "--out", outDir]);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(stdout).Contains("both write");
        await Assert.That(Directory.Exists(outDir)).IsFalse();
    }

    [Test]
    public async Task Directory_IsNotRecursive() {
        var dir = PolycTestSupport.TempDir();
        Write(dir, "a.poly", PolycTestSupport.Orders);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        Write(Path.Combine(dir, "sub"), "bad.poly", "not a domain @");
        var (exit, _, _) = Run(["check", dir]);
        await Assert.That(exit).IsEqualTo(0);
    }

    [Test]
    public async Task NoPaths_TtyStdin_PrintsUsageWithoutReadingStdin() {
        var (exit, _, err) = Run(["check"]);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(err).Contains("Usage:");
    }

    [Test]
    public async Task NoPaths_PipedStdin_IsOneDocument() {
        var ok = Run(["check"], PolycTestSupport.Orders);
        await Assert.That(ok.Exit).IsEqualTo(0);
        var bad = Run(["check"], PolycTestSupport.ManyErrors(1));
        await Assert.That(bad.Exit).IsEqualTo(1);
        await Assert.That(bad.Out).Contains("<stdin>:");
    }

    [Test]
    public async Task RemovedFlags_AreUsageErrors() {
        await Assert.That(Run(["build", "x.poly", "--mode", "db"]).Exit).IsEqualTo(1);
        await Assert.That(Run(["build", "x.poly", "--dbms", "sqlite"]).Exit).IsEqualTo(1);
        await Assert.That(Run(["check", "x.poly", "--seed", "nope"]).Err).Contains("Unknown seed");
    }

    [Test]
    public async Task Check_TargetFlag_ForwardsTheHostFact() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "posix.poly", "domain Shop\nuses posix\n\nOrder: entity { Total: Number }\n");

        // C-07 (ruling Q9): windows is not a shipped target, so the shipped catalog reports it as
        // unknown (DSL0005). The DMCON004 wrong-target behaviour is kept under test in
        // PolycCompileTests on a catalog that declares windows.
        var windows = Run(["check", path, "--target", "windows"]);
        await Assert.That(windows.Exit).IsEqualTo(1);
        await Assert.That(windows.Out).Contains("DSL0005");
        await Assert.That(windows.Out).Contains("windows");

        var posix = Run(["check", path, "--target", "posix"]);
        await Assert.That(posix.Exit).IsEqualTo(0);
    }

    [Test]
    public async Task Check_BogusTarget_ExitsOneListingKnownTargets() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);

        var control = Run(["check", path]);
        var bogus = Run(["check", path, "--target", "bogus"]);

        await Assert.That(control.Exit).IsEqualTo(0);
        await Assert.That(bogus.Exit).IsEqualTo(1);
        await Assert.That(bogus.Out).Contains("error DSL0005: Unknown host target 'bogus'. Known targets: posix.");
        await Assert.That(bogus.Out).Contains($"{path}:");
        await Assert.That(bogus.Err).DoesNotContain("Usage:");
    }

    [Test]
    public async Task Build_WindowsTarget_IsErrorAndWritesNothing() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);
        var okOut = Path.Combine(dir, "ok-out");
        var badOut = Path.Combine(dir, "bad-out");

        var control = Run(["build", path, "--out", okOut, "--target", "posix"]);
        var windows = Run(["build", path, "--out", badOut, "--target", "windows"]);

        await Assert.That(control.Exit).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(okOut, "Orders", "Order.cs"))).IsTrue();
        await Assert.That(windows.Exit).IsEqualTo(1);
        await Assert.That(windows.Out).Contains("DSL0005");
        await Assert.That(Directory.Exists(badOut)).IsFalse();
    }

    [Test]
    public async Task Check_BogusTarget_Json_IsOneUnplacedErrorRecord() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);

        var records = JsonNode.Parse(Run(["check", path, "--target", "bogus", "--format", "json"]).Out)!.AsArray();

        await Assert.That(records.Count).IsEqualTo(1);
        var record = DiagnosticFormat.FromJson(records[0]!);
        await Assert.That(record.Code).IsEqualTo("DSL0005");
        await Assert.That(record.Severity).IsEqualTo("error");
        await Assert.That(record.Placed).IsFalse();
        await Assert.That(record.File).IsEqualTo(path);
    }

    [Test]
    public async Task Usage_NamesTargetFlag_AndNoUnshippedTarget() {
        var (_, _, err) = Run([]);

        await Assert.That(err).Contains("Usage:");
        await Assert.That(err).Contains("--target");
        await Assert.That(err).DoesNotContain("windows");
    }

    /// <summary>C-07 pin: a known target is a host fact; it needs no loaded library to be accepted.</summary>
    [Test]
    public async Task Check_KnownTarget_NoLibraryLoaded_IsClean() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);

        var control = Run(["check", path]);
        var withTarget = Run(["check", path, "--target", "posix"]);

        await Assert.That(control.Exit).IsEqualTo(0);
        await Assert.That(withTarget.Exit).IsEqualTo(0);
        await Assert.That(withTarget.Out).IsEqualTo(control.Out);
    }

    /// <summary>C-07 pin: a blank target at the CLI stays a usage error, not a compile diagnostic.</summary>
    [Test]
    public async Task Target_Blank_IsAUsageErrorWithExitOne() {
        var blank = Run(["check", "--target", " "]);
        await Assert.That(blank.Exit).IsEqualTo(1);
        await Assert.That(blank.Err).Contains("--target needs a value");
        await Assert.That(blank.Out).DoesNotContain("DSL");
    }

    [Test]
    public async Task Target_NeedsANonBlankValue() {
        await Assert.That(Run(["check", "--target"]).Err).Contains("--target needs a value");
        await Assert.That(Run(["check", "--target", " "]).Err).Contains("--target needs a value");
    }

    /// <summary>
    /// C-03 R1: a document that lists no <c>uses</c> loads the implicit set
    /// <c>[storage, persistence]</c>, so <c>unique</c> is the storage-port guard in the
    /// module's own body and the wired catalog writes the DbContext.
    /// </summary>
    [Test]
    public async Task Build_NoUsesUniqueProperty_EmitsTheStoragePortGuard() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.Orders);
        var outDir = Path.Combine(dir, "out");
        var (exit, _, _) = Run(["build", path, "--out", outDir]);
        await Assert.That(exit).IsEqualTo(0);

        var order = File.ReadAllText(Path.Combine(outDir, "Orders", "Order.cs"));
        await Assert.That(order).Contains("ExistsWithNumber");
        await Assert.That(order).Contains("Unique constraint violated");
        await Assert.That(File.Exists(Path.Combine(outDir, "Orders", "OrdersDbContext.cs"))).IsTrue();
    }

    /// <summary>
    /// C-04 F3 (replaces the FX1 pin, whose expectation was wrong once the seeds fold):
    /// <c>--seed authoring</c> is an alias for the implicit set, so it writes the DbContext just
    /// as an omitted <c>--seed</c> does; <c>--seed language</c> loads no persistence and writes none.
    /// </summary>
    [Test]
    public async Task Build_ExplicitSeedAuthoring_IsTheImplicitSet() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "orders.poly", PolycTestSupport.UniquelessOrders);

        var explicitOut = Path.Combine(dir, "explicit");
        var (explicitExit, _, _) = Run(["build", path, "--out", explicitOut, "--seed", "authoring"]);
        await Assert.That(explicitExit).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(explicitOut, "Orders", "OrdersDbContext.cs"))).IsTrue();

        var implicitOut = Path.Combine(dir, "implicit");
        var (implicitExit, _, _) = Run(["build", path, "--out", implicitOut]);
        await Assert.That(implicitExit).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(implicitOut, "Orders", "OrdersDbContext.cs"))).IsTrue();

        var languageOut = Path.Combine(dir, "language");
        var (languageExit, _, _) = Run(["build", path, "--out", languageOut, "--seed", "language"]);
        await Assert.That(languageExit).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(languageOut, "Orders", "OrdersDbContext.cs"))).IsFalse();
    }

    [Test]
    public async Task Check_UnknownUses_ReportsDsl0002() {
        var dir = PolycTestSupport.TempDir();
        var path = Write(dir, "x.poly", "domain D\nuses nope\nA: entity {\n  X: Text\n}\n");
        var (exit, stdout, _) = Run(["check", path]);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(stdout).Contains($"{path}:2:6: error DSL0002: ");
    }
}