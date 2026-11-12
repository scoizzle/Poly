using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// R-03a: the VM binds the shared <c>Poly.Runtime</c> result family. The generic
/// name erases to <c>DomainResult&lt;object&gt;</c>, the void name is the non-generic
/// <c>DomainResult</c>, and the executor reads both through <c>IDomainResult</c>.
/// </summary>
public sealed class DomainResultErasureTests {
    [Test]
    public async Task DomainResultTypeProvider_AnswersTheArityKey() {
        var provider = DomainResultTypeProvider.Wrap(ClrTypeDefinitionRegistry.Shared);

        var definition = provider.GetTypeDefinition(AstTypeRegistry.Key("DomainResult", 1));

        await Assert.That(definition is IClrTypeDefinition).IsTrue();
        await Assert.That(((IClrTypeDefinition)definition!).RuntimeType)
            .IsEqualTo(typeof(Poly.Runtime.DomainResult<object>));
    }

    [Test]
    public async Task Vm_BindsGenericName_ToDomainResultOfObject() {
        var resolved = Resolve(new NamedTypeReference(
            "DomainResult", TypeArguments: [new NamedTypeReference("Entry")]));

        await Assert.That(resolved.GetRuntimeType()).IsEqualTo(typeof(Poly.Runtime.DomainResult<object>));
    }

    [Test]
    public async Task Vm_BindsVoidName_ToDomainResult() {
        var resolved = Resolve(new NamedTypeReference("DomainResult"));

        await Assert.That(resolved.GetRuntimeType()).IsEqualTo(typeof(Poly.Runtime.DomainResult));
    }

    [Test]
    public async Task PolyAssembly_NoLongerDeclaresDomainResult() {
        var assembly = typeof(ModuleInstanceExecutor).Assembly;

        await Assert.That(assembly.GetType("Poly.DomainModeling.Runtime.DomainResult")).IsNull();
        await Assert.That(assembly.GetTypes().Any(t => t.Name is "DomainResult" or "DomainResult`1")).IsFalse();
    }

    private const string Parking = """
        domain Parking
        Permit: entity { Plate: Text }
        Lot: entity {
          permits: many Permit
          Issue: action {
            create in permits { Plate: "P1" }
          }
        }
        """;

    [Test]
    public async Task CreateChild_IsRegisteredAndReturned_ThroughTheErasedResult() {
        var executor = new ModuleInstanceExecutor(Lower(Parking));
        var lot = executor.Create("Lot", new Dictionary<string, object?>()).Instance!;

        var outcome = executor.Invoke(lot, "Issue");

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.CreatedChildren.Count).IsEqualTo(1);
        var permit = outcome.CreatedChildren[0];
        await Assert.That(permit.EntityName).IsEqualTo("Permit");
        await Assert.That(permit.Bag["Plate"]).IsEqualTo("P1");
        await Assert.That(executor.TryGet(permit.Id, out _)).IsTrue();
    }

    private const string Ranged = """
        domain Gauge
        Reading: entity {
          Level: Number range(1, 10)
        }
        """;

    [Test]
    public async Task Create_ConstraintFailure_ReachesModuleOutcomeFail_WithTheMessage() {
        var executor = new ModuleInstanceExecutor(Lower(Ranged));

        var outcome = executor.Create("Reading", new Dictionary<string, object?> { ["Level"] = 99L });

        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error).IsNotNull();
        await Assert.That(outcome.Error!.Length).IsGreaterThan(0);
        await Assert.That(outcome.Error).Contains("Level");
        await Assert.That(executor.Instances.Count).IsEqualTo(0);
    }

    /// <summary>Resolves a property type on a one-property holder through the VM provider stack.</summary>
    private static ITypeDefinition Resolve(Node reference) {
        var entry = new TypeDefinitionNode("Entry");
        var holder = new TypeDefinitionNode(
            "Holder", Properties: [new PropertyDefinitionNode("Slot", reference)]);
        var ctx = new AnalysisContext(
            DomainResultTypeProvider.Wrap(AnalysisContext.CreateDefault().TypeDefinitions));
        new TypeDefinitionNodeAnalyzer().Analyze(ctx, new Block([entry, holder]));
        var resolved = ctx.GetMetadata<TypeDefinitionMetadata>(holder)!.TypeDefinition;
        return resolved.Properties.Single(p => p.Name == "Slot").MemberTypeDefinition;
    }

    private static Domain Lower(string poly) => LowerWithSession(poly).Domain;

    /// <summary>The sample catalog's mcp-library domain: the only entity golden that
    /// throws <c>ConstraintFailureException</c> from a void body.</summary>
    private static string McpLibraryPoly() =>
        GoldenDomains.Compilable.Single(d => d.Name == "mcp-library").Poly;

    /// <summary>
    /// One report path for both twins-present (S2b) and twins-absent (S3): the module
    /// exactly as the runtime host analyzes it. S2b retargets this single call to
    /// <c>DomainSession.AnalyzeModule</c>, like the other module-analysis sites.
    /// </summary>
    private static (int Errors, int Warnings, IReadOnlyList<TypeDefinitionNode> Module)
        ModuleReport(string poly) {
        var (domain, analysis, session) = LowerWithSession(poly);
        var module = RuntimeAnalysisCache.GetOrLower(domain, session, analysis);
        var vm = DomainSession.AnalyzeModule(module);

        return (
            vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
            vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
            module);
    }

    [Test]
    public async Task ModuleAnalysis_TwinsPresent_IsCleanAndAstWins() {
        // R8a / I3: while the twins exist, the module's own types win and the report is clean.
        var (errors, warnings, module) = ModuleReport(McpLibraryPoly());

        await Assert.That(errors).IsEqualTo(0);
        await Assert.That(warnings).IsEqualTo(0);
        await Assert.That(module.Any(t => t.Name == "DomainResult")).IsTrue();
    }

    [Test]
    public async Task ModuleAnalysis_TwinsAbsent_ResolvesPolyRuntime() {
        // R8b / I4: with the twins gone the same report is clean only through Poly.Runtime.
        var (errors, warnings, module) = ModuleReport(McpLibraryPoly());

        await Assert.That(errors).IsEqualTo(0);
        await Assert.That(warnings).IsEqualTo(0);
        await Assert.That(module.Any(t => t.Name == "DomainResult")).IsFalse();
    }

    [Test]
    public async Task AnalyzeModule_ModuleWithoutResultTwins_ResolvesPolyRuntime() {
        // S2b seam pin: the seam wraps the default registry with DomainResultTypeProvider, so a
        // module that declares none of the result family still analyzes clean. Run while the
        // real module still carries the twins, so this proves the wiring before S3 drops them.
        var (domain, analysis, session) = LowerWithSession(McpLibraryPoly());
        var module = RuntimeAnalysisCache.GetOrLower(domain, session, analysis);
        var withoutTwins = module
            .Where(t => t.Name is not ("DomainResult" or "ConstraintFailureException"))
            .ToList();
        await Assert.That(withoutTwins.Count).IsLessThan(module.Count);

        var vm = DomainSession.AnalyzeModule(withoutTwins);

        await Assert.That(vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)).IsEqualTo(0);
        await Assert.That(vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)).IsEqualTo(0);
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session)
        LowerWithSession(string poly) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        _ = RuntimeAnalysisCache.GetOrLower(result.Root!, session, result.Analysis);
        return (result.Root!, result.Analysis, session);
    }
}