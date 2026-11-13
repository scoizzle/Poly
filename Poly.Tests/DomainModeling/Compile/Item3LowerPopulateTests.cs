using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Item 3: policies, OnEntry/OnExit batches, and subscription bodies are
/// populated at session.Lower / GetOrLower — Domain-bound hot path binds those
/// trees (cache-identity oracles, fail-closed on miss).
/// </summary>
public class Item3LowerPopulateTests {
    [Test]
    public async Task Policy_EvaluatePolicy_UsesCachedBody_NotAgeExpression() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number
              IsAdult: policy { Age >= 18 }
            }
            """);
        session.Lower(domain, analysis);
        var exec = new ModuleInstanceExecutor(domain);
        await Assert.That(RuntimeAnalysisCache.TryGetPolicyBody(domain, "Gate", "IsAdult", out var before))
            .IsTrue();
        await Assert.That(before).IsNotNull();

        // Sentinel: constant true — Age expression would yield false for Age=16.
        RuntimeAnalysisCache.ReplacePolicyBody(domain, "Gate", "IsAdult", new Constant(true));

        var gateE = domain.Types.OfType<Entity>().First(e => e.Name == "Gate");
        var minor = exec.Create((gateE).Name, new Dictionary<string, object?> { ["Age"] = 16L }).Instance!;
        var policy = gateE.Policies.First(p => p.Name == "IsAdult");
        await Assert.That(exec.EvaluatePolicy(minor, (policy).Name)).IsTrue();

        RuntimeAnalysisCache.ReplacePolicyBody(domain, "Gate", "IsAdult", new Constant(false));
        var adult = exec.Create((gateE).Name, new Dictionary<string, object?> { ["Age"] = 21L }).Instance!;
        await Assert.That(exec.EvaluatePolicy(adult, (policy).Name)).IsFalse();
    }

    [Test]
    public async Task Subscription_FiresFromCachedBody_OnPeerTransition() {
        var (domain, analysis, session) = Evolve("""
            domain Watch
            Order: entity {
              Open: stage { }
              Active: stage { }
              Activate: action { transition to Active }
            }
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active {
                  assign Status to "Triggered"
                }
              }
            }
            """);
        session.Lower(domain, analysis);
        var exec = new ModuleInstanceExecutor(domain);

        var pending = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker")
            .Stages.First(s => s.Name == "Pending");
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending)
            ?? throw new InvalidOperationException("missing subscription plan");
        var entry = plan.ByRelationshipName.Values.SelectMany(e => e).First();

        RuntimeAnalysisCache.ReplaceSubscriptionBody(
            domain, entry,
            AssignEntityProp("Tracker", "Status", "sentinel-sub"));

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var order = exec.Create((orderE).Name, new Dictionary<string, object?>()).Instance!;
        var tracker = exec.Create((trackerE).Name, new Dictionary<string, object?> { ["Status"] = "Idle" }).Instance!;
        exec.Link("Tracks", tracker, order);

        var activated = exec.Invoke(order, "Activate");
        await Assert.That(activated.Succeeded).IsTrue();
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("sentinel-sub");
    }

    [Test]
    public async Task DomainBound_MissingPolicyBody_Throws_DoesNotRelower() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number
              IsAdult: policy { Age >= 18 }
            }
            """);
        session.Lower(domain, analysis);
        var exec = new ModuleInstanceExecutor(domain);
        RuntimeAnalysisCache.ClearPolicyBody(domain, "Gate", "IsAdult");

        var gateE = domain.Types.OfType<Entity>().First(e => e.Name == "Gate");
        var adult = exec.Create((gateE).Name, new Dictionary<string, object?> { ["Age"] = 21L }).Instance!;
        var policy = gateE.Policies.First(p => p.Name == "IsAdult");
        await Assert.That(() => exec.EvaluatePolicy(adult, (policy).Name))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Policy body 'IsAdult' is missing");
    }

    [Test]
    public async Task DomainBound_ClearedSubscriptionBody_Throws_DoesNotRelower() {
        var (domain, analysis, session) = Evolve("""
            domain Watch
            Order: entity {
              Open: stage { }
              Active: stage { }
              Activate: action { transition to Active }
            }
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active {
                  assign Status to "Triggered"
                }
              }
            }
            """);
        session.Lower(domain, analysis);
        var exec = new ModuleInstanceExecutor(domain);

        var pending = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker")
            .Stages.First(s => s.Name == "Pending");
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending)!;
        var entry = plan.ByRelationshipName.Values.SelectMany(e => e).First();
        RuntimeAnalysisCache.ClearSubscriptionBody(domain, entry);

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var order = exec.Create((orderE).Name, new Dictionary<string, object?>()).Instance!;
        var tracker = exec.Create((trackerE).Name, new Dictionary<string, object?> { ["Status"] = "Idle" }).Instance!;
        exec.Link("Tracks", tracker, order);

        await Assert.That(() => exec.Invoke(order, "Activate"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Subscription body is missing");
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("Idle");
    }

    [Test]
    public async Task GetOrLower_Twice_ReturnsSameModuleReference() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number
              IsAdult: policy { Age >= 18 }
            }
            """);
        var first = session.Lower(domain, analysis);
        var second = RuntimeAnalysisCache.GetOrLower(
            domain, RuntimeAnalysisCache.Session(domain), analysis);
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task GetOrLower_OneArg_ReturnsSameModuleAsThreeArg() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number
              IsAdult: policy { Age >= 18 }
            }
            """);
        var threeArg = RuntimeAnalysisCache.GetOrLower(domain, session, analysis);
        var oneArg = RuntimeAnalysisCache.GetOrLower(domain);
        await Assert.That(ReferenceEquals(threeArg, oneArg)).IsTrue();
    }

    [Test]
    public async Task Index_AfterGetOrLowerOnly_MethodMatchesTryGetModuleMethod() {
        var (domain, _, _) = Evolve("""
            domain Library
            Patron: entity {
              Name: Text
              Active: stage {
                AssessByType: action { }
              }
            }
            """);
        RuntimeAnalysisCache.GetOrLower(domain);
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Patron", "AssessByType", out var fromModule)).IsTrue();
        var fromIndex = RuntimeAnalysisCache.Index(domain).Method("Patron", "AssessByType");
        await Assert.That(fromIndex).IsSameReferenceAs(fromModule);
    }

    [Test]
    public async Task Analysis_AfterOneCall_IsSameReferenceAsGetOrAnalyze() {
        var (domain, _, _) = Evolve("""
            domain Library
            Patron: entity {
              Name: Text
            }
            """);
        var fromAnalysis = RuntimeAnalysisCache.Analysis(domain);
        var fromGetOrAnalyze = RuntimeAnalysisCache.GetOrAnalyze(domain);
        await Assert.That(fromAnalysis).IsSameReferenceAs(fromGetOrAnalyze);
    }

    [Test]
    public async Task Index_AfterGetOrLowerOnly_TreeAndNavigationAreCatalogNodes() {
        var (domain, _, _) = Evolve("""
            domain Library
            Patron: entity {
              Name: Text
              fines: many Fine
            }
            Fine: entity {
              Amount: Number
            }
            """);
        RuntimeAnalysisCache.GetOrLower(domain);
        var index = RuntimeAnalysisCache.Index(domain);
        var catalog = RuntimeAnalysisCache.Session(domain).ArtifactCatalog;
        var tree = catalog.Find(ArtifactId.Create(["Library", "Patron"], "entity"));
        await Assert.That(index.Tree("Patron")).IsSameReferenceAs(tree);
        var fines = ((IReadOnlyList<TypeDefinitionNode>)tree!.Payload!)
            .SelectMany(t => t.Properties ?? [])
            .Single(p => p.Name == "Fines");
        await Assert.That(index.Navigation("Patron", "Fines")).IsSameReferenceAs(fines);
    }

    [Test]
    public async Task Index_Twice_ReturnsSameIndex() {
        var (domain, _, _) = Evolve("""
            domain Library
            Patron: entity {
              Name: Text
            }
            """);
        var first = RuntimeAnalysisCache.Index(domain);
        var second = RuntimeAnalysisCache.Index(domain);
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task Subscription_PeerBinding_MaterializesInsideTryCatchFinally() {
        // Stage transition in subscription effects emits TryCatchFinally; peer
        // Parameter reads in the same body must still Materialize (F3 walk).
        var (domain, analysis, session) = Evolve("""
            domain Watch
            Order: entity {
              Plate: Text
              Open: stage { }
              Active: stage { }
              Activate: action { transition to Active }
            }
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active as order {
                  assign Status to order Plate
                  transition to Done
                }
              }
              Done: stage { }
            }
            """);
        session.Lower(domain, analysis);
        var exec = new ModuleInstanceExecutor(domain);

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var order = exec.Create((orderE).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ-9" }).Instance!;
        var tracker = exec.Create((trackerE).Name, new Dictionary<string, object?> { ["Status"] = "Idle" }).Instance!;
        exec.Link("Tracks", tracker, order);

        var activated = exec.Invoke(order, "Activate");
        await Assert.That(activated.Succeeded).IsTrue();
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("XYZ-9");
        await Assert.That(ModuleReads.Stage(tracker)).IsEqualTo("Done");
    }

    private static Node AssignEntityProp(string entityName, string prop, string value) =>
        new Block([
            new Assignment(
                new Member(new Parameter("entity", new TypeReference(entityName)), prop),
                new Constant(value)),
            // A handler returns a result; the sentinel body must too.
            new Return(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")))
        ]);

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, [.. ExtensionCatalog.ProductAuthoring, "persistence"]);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}