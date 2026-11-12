using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Packs.Sqlite;
using Poly.Tests.TestHelpers;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// C10 oracle: the interpreter runs the tree the lowering catalog published. Each probe is
/// lowered once; the executor then creates instances and invokes actions, and every
/// method it compiled is held against the entity artifact's method of the same name.
/// A parameterless body must be the very node the catalog holds; a parameterised body is
/// the catalog body with its typed parameters bound, so it must print the same and have
/// the same node count.
/// </summary>
public class CatalogArtifactOracleTests {
    // Minimum (entity, method) pairs that must have compiled across all probes. A floor,
    // so an oracle whose synthesized instances all fail to create cannot pass vacuously.
    const int MinimumCompiledPairs = 150;

    static readonly ExtensionCatalog ProbeCatalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());
    static readonly string[] ProbeSeed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    public static IEnumerable<string> Samples() =>
        GoldenDomains.Lowerable.Select(d => d.Path).Order(StringComparer.Ordinal);

    [Test]
    public async Task Skipped_StoredValueTypeProperty_StillProducesDmstr006() {
        var domain = GoldenDomains.Skipped.Single(d => d.Name == "restaurant");
        var (succeeded, errors) = Analyze(File.ReadAllText(domain.FullPath));
        await Assert.That(succeeded).IsFalse().Because(domain.SkipReason!);
        await Assert.That(errors.Any(e => e.Code == "DMSTR006")).IsTrue().Because(domain.SkipReason!);
    }

    [Test]
    public async Task Skipped_NestedInvokeTypeMismatch_StillFailsWithATypeError() {
        var domain = GoldenDomains.Skipped.Single(d => d.Name == "nested-invoke-type-mismatch");
        var (succeeded, errors) = Analyze(File.ReadAllText(domain.FullPath));
        await Assert.That(succeeded).IsFalse().Because(domain.SkipReason!);
        await Assert.That(errors.Any(e => e.Code == "DMSEM003")).IsTrue().Because(domain.SkipReason!);
    }

    [Test]
    public async Task EveryProbeOnDisk_IsLowerableOrSkipped() {
        var accounted = GoldenDomains.Lowerable.Select(d => d.Path)
            .Concat(GoldenDomains.Skipped.Select(d => d.Path))
            .ToHashSet(StringComparer.Ordinal);
        var unaccounted = new[] { "docs/probes", "demo/live" }
            .SelectMany(dir => Directory.EnumerateFiles(
                Path.Combine(GoldenDomains.RepoRoot, dir), "*.poly", SearchOption.AllDirectories))
            .Where(path => !path.StartsWith(GoldenDomains.SwarmDirectory, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(GoldenDomains.RepoRoot, path).Replace('\\', '/'))
            .Where(path => !accounted.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();
        await Assert.That(unaccounted).IsEmpty()
            .Because("every probe on disk must be in GoldenDomains.Lowerable or Skipped: "
                + string.Join(", ", unaccounted));
    }

    [Test]
    public async Task ExecutedBodies_AreTheCatalogArtifactBodies() {
        var mismatches = new List<string>();
        var compiled = 0;
        foreach (var relativePath in Samples())
            compiled += Check(relativePath, mismatches);
        await Assert.That(mismatches).IsEmpty();
        await Assert.That(compiled).IsGreaterThanOrEqualTo(MinimumCompiledPairs);
    }

    [Test]
    public async Task ExecutedBody_AfterSecondInvoke_IsStillTheCatalogBody() {
        const string poly = """
            domain Sub
            Order: entity {
              Code: Text
              Total: Number default(0)
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { }
            }
            Ledger: entity {
              Name: Text
              orders: many Order
              Seen: Number default(0)
              when orders Closed {
                assign Seen to Seen + 1
              }
              Add: action { create in orders { Code: "a" } }
            }
            """;
        var (domain, catalog) = LowerSample(poly);
        var executor = ModuleExec.For(domain);
        var ledger = executor.Create("Ledger", new Dictionary<string, object?> { ["Name"] = "l" }).Instance!;
        _ = executor.Invoke(ledger, "Add");
        _ = executor.Invoke(ledger, "Add");
        var body = executor.CompiledBodyFor("Ledger", "Add");
        var artifact = MethodsOf(catalog, "Ledger").Single(m => m.Name == "Add" && !m.IsStatic);
        await Assert.That(body).IsNotNull();
        await Assert.That(ReferenceEquals(body, artifact.Body)).IsTrue();
    }

    // ── harness ───────────────────────────────────────────────────────────────

    static int Check(string relativePath, List<string> mismatches) {
        var poly = File.ReadAllText(Path.Combine(GoldenDomains.RepoRoot, relativePath));
        Domain domain;
        ArtifactCatalog catalog;
        try {
            (domain, catalog) = LowerSample(poly);
        }
        catch (InvalidOperationException ex) {
            mismatches.Add($"{relativePath}: does not lower: {ex.Message}");
            return 0;
        }
        var executor = ModuleExec.For(domain);
        Exercise(executor, domain);

        var count = 0;
        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var method in MethodsOf(catalog, entity.Name)) {
                var body = executor.CompiledBodyFor(entity.Name, method.Name);
                if (body is null || method.Body is null)
                    continue;
                count++;
                var where = $"{relativePath}: {entity.Name}.{method.Name}";
                if ((method.Parameters?.Count ?? 0) == 0) {
                    if (!ReferenceEquals(body, method.Body))
                        mismatches.Add($"{where}: executed body is not the catalog body");
                    continue;
                }
                var expected = ModuleMethodBinding.BindTypedParameters(entity.Name, method).Tree;
                var printedExpected = new CSharpGenerator().Generate(expected);
                var printedExecuted = new CSharpGenerator().Generate(body);
                if (!string.Equals(printedExpected, printedExecuted, StringComparison.Ordinal)
                    || NodeCount(expected) != NodeCount(body))
                    mismatches.Add($"{where}: executed body differs from the catalog body with parameters bound");
            }
        }
        return count;
    }

    static (Domain Domain, ArtifactCatalog Catalog) LowerSample(string poly) {
        var session = DomainSession.ForSource(poly, ProbeSeed, ProbeCatalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), ProbeSeed);
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!outcome.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                outcome.Analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        _ = session.Lower(outcome.Root!, outcome.Analysis);
        return (outcome.Root!, session.ArtifactCatalog);
    }

    // The analysis verdict a skipped probe is held to, without lowering it.
    static (bool Succeeded, List<Poly.Analysis.Diagnostic> Errors) Analyze(string poly) {
        var session = DomainSession.ForSource(poly, ProbeSeed, ProbeCatalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), ProbeSeed);
        var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        var errors = outcome.Analysis.Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .ToList();
        return (outcome.Succeeded, errors);
    }

    static IEnumerable<MethodDefinitionNode> MethodsOf(ArtifactCatalog catalog, string entityName) =>
        catalog.Artifacts
            .Where(a => a.Descriptor.Id.Type == DomainSession.EntityType
                && a.Payload is IEnumerable<TypeDefinitionNode>)
            .SelectMany(a => (IEnumerable<TypeDefinitionNode>)a.Payload!)
            .Where(t => t.Name == entityName)
            .SelectMany(t => t.Methods ?? []);

    // Creates one instance per entity (two passes, so entity-typed values find a target)
    // and invokes every action on it. Failures are expected and ignored: the executor
    // compiles the method before it runs it, and the compiled body is what is read.
    static void Exercise(ModuleInstanceExecutor executor, Domain domain) {
        var entities = domain.Types.OfType<Entity>().ToList();
        var made = new Dictionary<string, ModuleInstance>(StringComparer.Ordinal);
        for (var pass = 0; pass < 2; pass++) {
            foreach (var entity in entities) {
                if (made.ContainsKey(entity.Name))
                    continue;
                var values = entity.Properties.ToDictionary(
                    p => p.Name, p => Synthesize(domain, made, p.Type.TypeName), StringComparer.OrdinalIgnoreCase);
                try {
                    if (executor.Create(entity.Name, values) is { Succeeded: true, Instance: { } instance })
                        made[entity.Name] = instance;
                }
                catch (Exception) { }
            }
        }
        foreach (var entity in entities) {
            if (!made.TryGetValue(entity.Name, out var instance))
                continue;
            foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))) {
                var args = ArgsOf(domain, made, action);
                for (var i = 0; i < 2; i++) {
                    try { _ = executor.Invoke(instance, action.Name, args); }
                    catch (Exception) { }
                }
            }
        }
    }

    static Dictionary<string, object?> ArgsOf(Domain domain, Dictionary<string, ModuleInstance> made, Action action) =>
        action.Parameters.ToDictionary(
            p => p.Name, p => Synthesize(domain, made, p.Type.TypeName), StringComparer.OrdinalIgnoreCase);

    static object? Synthesize(Domain domain, Dictionary<string, ModuleInstance> made, string typeName) {
        if (made.TryGetValue(typeName, out var instance))
            return instance.Id;
        if (domain.Types.OfType<EnumType>().FirstOrDefault(e => e.Name == typeName) is { MemberNames: [var first, ..] })
            return first;
        return typeName switch {
            "Text" => "x",
            "Number" => 1,
            "Decimal" => 1m,
            "Boolean" => true,
            "Date" => "2026-01-01",
            "DateTime" => "2026-01-01T00:00:00Z",
            "Time" => "00:00:00",
            "Duration" => "1",
            "Guid" => Guid.NewGuid().ToString(),
            _ => null
        };
    }

    static int NodeCount(Node node) =>
        1 + node.Children.Where(c => c is not null).Sum(c => NodeCount(c!));
}