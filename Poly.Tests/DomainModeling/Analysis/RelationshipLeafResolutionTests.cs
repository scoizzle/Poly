using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>A <see cref="RelationshipNavigation"/> leaf (<c>cust Amount</c>) belongs to the RELATED entity.
/// It is evaluated against that entity's declared constraints, never against a same-named property of the
/// caller; a relationship that does not resolve to exactly one target entity yields Unknown.</summary>
public class RelationshipLeafResolutionTests {
    private static EvolutionResult Parse(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());

    private static ValueRange? TotalRange(EvolutionResult r) {
        var run = r.Root.Types.OfType<Entity>().SingleOrDefault(e => e.Name == "Order")?.Actions.SingleOrDefault(a => a.Name == "Run");
        if (run is null) return null;
        return r.Analysis.GetActionInvariants(run)?.StageContexts().SingleOrDefault()?.Postconditions
            .SingleOrDefault(p => p.TargetProperty == "Total")?.ValueRange;
    }

    private static string Summary(EvolutionResult r) =>
        $"{TotalRange(r)?.Min}..{TotalRange(r)?.Max}";

    private static List<string> Problems(EvolutionResult r) =>
        [.. r.Analysis.Diagnostics
            .Where(d => d.Severity != DiagnosticSeverity.Hint)
            .Select(d => $"{d.Severity} {d.Code}: {d.Message}")];

    private static string Order(string callerAmount, string related, string rhs) => $$"""
        domain Test
        Customer: entity { {{related}} }
        Order: entity {
          {{callerAmount}}
          Total: Number range(0, 200)
          cust: one Customer
          Run: action { assign Total to {{rhs}} }
        }
        """;

    [Test]
    public async Task Assign_CallerAmountSmaller_UsesRelatedDeclaredRange() {
        var r = Parse(Order("Amount: Number range(0, 100)", "Amount: Number range(1000, 2000)", "cust Amount"));
        var range = TotalRange(r);
        await Assert.That(range!.Min).IsEqualTo(1000d);
        await Assert.That(range.Max).IsEqualTo(2000d);
    }

    [Test]
    public async Task Assign_CallerAmountSmaller_DerivedValueIsFlaggedAgainstRelatedRange() {
        var r = Parse(Order("Amount: Number range(0, 100)", "Amount: Number range(1000, 2000)", "cust Amount + 0"));
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(Problems(r).Any(p => p.Contains("DMEFF008") && p.Contains("[1000, 2000]") && p.Contains("range(0, 200)"))).IsTrue();
    }

    [Test]
    public async Task Assign_CallerAmountLarger_UsesRelatedDeclaredRange_NoFalseError() {
        var r = Parse(Order("Amount: Number range(1000, 2000)", "Amount: Number range(0, 100)", "cust Amount + 0"));
        var range = TotalRange(r);
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(100d);
        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(Problems(r).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Assign_DistinctName_ReadsRelatedDeclaredRange() {
        var r = Parse(Order("Other: Number range(0, 5)", "Credit: Number range(1000, 2000)", "cust Credit"));
        var range = TotalRange(r);
        await Assert.That(range!.Min).IsEqualTo(1000d);
        await Assert.That(range.Max).IsEqualTo(2000d);
    }

    [Test]
    public async Task Verdict_IsIdentical_WhetherOrNotCallerHasAPropertyOfThatName() {
        const string related = "Amount: Number range(1000, 2000)";
        foreach (var rhs in new[] { "cust Amount", "cust Amount + 0" }) {
            var withCollision = Parse(Order("Amount: Number range(0, 100)", related, rhs));
            var withoutCollision = Parse(Order("Unrelated: Number range(0, 100)", related, rhs));
            await Assert.That(Summary(withCollision)).IsEqualTo(Summary(withoutCollision));
            await Assert.That(string.Join("|", Problems(withCollision))).IsEqualTo(string.Join("|", Problems(withoutCollision)));
        }
    }

    private const string InvokeProgram = """
        domain Test
        Customer: entity {
          Level: Number range(0, 10)
          Slot: Number range(0, 10)
          Act: action (P: Number) { assign Slot to P }
        }
        Order: entity {
          {0}
          cust: one Customer
          Run: action { invoke cust.Act(P: cust Level) }
        }
        """;

    [Test]
    public async Task Invoke_Binding_ReadsRelatedLevel_NotCallers() {
        // Caller Level is [50,60] (outside Slot's range(0,10)); the related Level is [0,10]. The argument is
        // the related one, so there is no DMEFF008.
        var r = Parse(InvokeProgram.Replace("{0}", "Level: Number range(50, 60)"));
        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(Problems(r).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_Binding_VerdictIdenticalWithoutCallerLevel() {
        var with = Parse(InvokeProgram.Replace("{0}", "Level: Number range(50, 60)"));
        var without = Parse(InvokeProgram.Replace("{0}", "Other: Number range(50, 60)"));
        await Assert.That(string.Join("|", Problems(with))).IsEqualTo(string.Join("|", Problems(without)));
    }

    private static ValueRange? SlotRange(EvolutionResult r, string entity, string action, string property) {
        var act = r.Root.Types.OfType<Entity>().Single(e => e.Name == entity).Actions.Single(a => a.Name == action);
        return r.Analysis.GetActionInvariants(act)?.StageContexts().SelectMany(c => c.Postconditions)
            .Where(p => p.TargetProperty == property).Select(p => p.ValueRange).FirstOrDefault(v => v is not null);
    }

    [Test]
    public async Task SelfInvoke_Binding_ReadsRelatedLevel_NotCallers() {
        // Caller Level [50,60] collides with Customer Level [0,10]; the bound argument is the related one, so the
        // callee's Slot postcondition range is [0,10] (Unknown, i.e. null, if the domain is not threaded).
        var r = Parse("""
            domain Test
            Customer: entity { Level: Number range(0, 10) }
            Order: entity {
              Level: Number range(50, 60)
              Slot: Number range(0, 100)
              cust: one Customer
              Act: action (P: Number) { assign Slot to P }
              Run: action { invoke Act(P: cust Level) }
            }
            """);
        var range = SlotRange(r, "Order", "Run", "Slot");
        await Assert.That(range).IsNotNull();
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(10d);
    }

    [Test]
    public async Task ForEachInvoke_Binding_ReadsRelatedLevel_NotCallers() {
        // The DSL only lets a for-each binding dereference its binder (DMEFF007), so the caller-rooted
        // relationship leaf is built through the API. Caller Level [50,60] collides with Customer Level [0,10].
        static Property Num(string n, double min, double max) =>
            new(n, new DomainTypeReference("Number"), [new RangeConstraint(min, max)]);
        var take = new Poly.DomainModeling.Ontology.Action("Take", InvocationResult.Void, [Num("x", 0, 1000)],
            [new AssignEffect(DomainExpression.Property("Mirror"), new ParameterAccess("x"))], []);
        var line = new Entity("Line", [Num("Mirror", 0, 100)], [take], [], []);
        var customer = new Entity("Customer", [Num("Level", 0, 10)], [], [], []);
        var run = new Poly.DomainModeling.Ontology.Action("Run", InvocationResult.Void, [], [
            new ForEachInvokeEffect("lines", "l", null, "Take",
                [new PropertyBinding("x", new RelationshipNavigation("cust", DomainExpression.Property("Level")))])], []);
        var order = new Entity("Order", [Num("Level", 50, 60)], [run], [], []) {
            Navigations = [
                new Relationship("cust", new DomainTypeReference("Order"), new DomainTypeReference("Customer"), RelationshipCardinality.OneToOne, []),
                new Relationship("lines", new DomainTypeReference("Order"), new DomainTypeReference("Line"), RelationshipCardinality.OneToMany, [])]
        };
        var analysis = DomainModelAnalyzer.Analyze(new Domain("D", [customer, line, order]));
        var range = analysis.GetActionInvariants(run)?.StageContexts().SelectMany(c => c.Postconditions)
            .Where(p => p.TargetProperty == "Mirror").Select(p => p.ValueRange).FirstOrDefault(v => v is not null);
        await Assert.That(range).IsNotNull();
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(10d);
    }

    // The validator requires a for-each binding's relationship-navigation root to be the BINDER name, but
    // lets the binder share a name with one of the CALLER's relationships. The binder-rooted argument
    // (`cust Level`) must resolve against the ITERATED entity, never the caller's same-named relationship.
    private const string ForEachCollisionProgram = """
        domain Test
        Customer: entity { Level: Number range(0, 10) }
        Line: entity {
          Level: Number range(500, 600)
          Mirror: Number range(0, 1000)
          Take: action (X: Number) { assign Mirror to X }
        }
        Order: entity {
          cust: one Customer
          lines: many Line
          Run: action { for lines as cust invoke cust.Take(X: cust Level) }
        }
        """;

    [Test]
    public async Task ForEachInvoke_DslBinderRootedBinding_ResolvesAgainstIteratedEntity_NotCallersSameNamedRelationship() {
        var r = Parse(ForEachCollisionProgram);
        var range = SlotRange(r, "Order", "Run", "Mirror");
        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(range).IsNotNull();
        await Assert.That($"{range!.Min}..{range.Max}").IsEqualTo("500..600");
    }

    [Test]
    public async Task ForEachInvoke_DslBinderRootedArithmetic_ResolvesAgainstIteratedEntity() {
        var r = Parse(ForEachCollisionProgram.Replace("X: cust Level)", "X: cust Level + 1)"));
        var range = SlotRange(r, "Order", "Run", "Mirror");
        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(range).IsNotNull();
        await Assert.That($"{range!.Min}..{range.Max}").IsEqualTo("501..601");
    }

    // ── Fail closed: an unresolvable relationship is Unknown, never the caller's property ──

    private static (Entity Caller, Domain Domain) Build(Relationship? rel, params Entity[] others) {
        var caller = new Entity("Order",
            [new Property("Amount", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [], [], []) { Navigations = rel is null ? [] : [rel] };
        return (caller, new Domain("D", [caller, .. others]));
    }

    private static Entity Customer() => new("Customer",
        [new Property("Amount", new DomainTypeReference("Number"), [new RangeConstraint(1000d, 2000d)])],
        [], [], []);

    private static AbstractValue EvalLeaf(Entity caller, Domain? domain, string relationship) {
        var env = new Dictionary<string, AbstractValue> {
            ["Amount"] = AbstractValue.From([new RangeConstraint(0d, 100d)])
        };
        return EffectInvariantAnalyzer.Eval(
            new RelationshipNavigation(relationship, DomainExpression.Property("Amount")),
            caller, null, env, domain: domain);
    }

    private static Relationship Rel(string name, RelationshipCardinality card, string target = "Customer") =>
        new(name, new DomainTypeReference("Order"), new DomainTypeReference(target), card, []);

    [Test]
    public async Task Eval_ResolvableToOneRelationship_ReadsRelatedDeclaredRange() {
        var (caller, domain) = Build(Rel("cust", RelationshipCardinality.OneToOne), Customer());
        var v = EvalLeaf(caller, domain, "cust");
        await Assert.That(v.NumericRange!.Min).IsEqualTo(1000d);
        await Assert.That(v.NumericRange.Max).IsEqualTo(2000d);
    }

    [Test]
    public async Task Eval_ResolvableManyToOneRelationship_ReadsRelatedDeclaredRange() {
        var (caller, domain) = Build(Rel("cust", RelationshipCardinality.ManyToOne), Customer());
        var v = EvalLeaf(caller, domain, "cust");
        await Assert.That(v.NumericRange!.Min).IsEqualTo(1000d);
        await Assert.That(v.NumericRange.Max).IsEqualTo(2000d);
    }

    [Test]
    public async Task Eval_TwoHopNavigation_SecondHopResolvesAgainstFirstHopTarget_NotTheCaller() {
        static Entity E(string name, double min, double max, params Relationship[] navs) => new(name,
            [new Property("X", new DomainTypeReference("Number"), [new RangeConstraint(min, max)])],
            [], [], []) { Navigations = [.. navs] };
        static Relationship R(string from, string name, string to) =>
            new(name, new DomainTypeReference(from), new DomainTypeReference(to), RelationshipCardinality.OneToOne, []);

        // Root also has a `b` (to Decoy, [5000,6000]) so resolving hop two against the root gives a different range.
        var root = E("Root", 0, 10, R("Root", "a", "A"), R("Root", "b", "Decoy"));
        var a = E("A", 100, 200, R("A", "b", "B"));
        var b = E("B", 1000, 2000);
        var decoy = E("Decoy", 5000, 6000);
        var domain = new Domain("D", [root, a, b, decoy]);

        var v = EffectInvariantAnalyzer.Eval(
            new RelationshipNavigation("a", new RelationshipNavigation("b", DomainExpression.Property("X"))),
            root, null, new Dictionary<string, AbstractValue>(), domain: domain);
        await Assert.That(v.NumericRange!.Min).IsEqualTo(1000d);
        await Assert.That(v.NumericRange.Max).IsEqualTo(2000d);
    }

    [Test]
    public async Task Eval_UnknownRelationship_IsUnknown_NotTheCallersProperty() {
        var (caller, domain) = Build(Rel("cust", RelationshipCardinality.OneToOne), Customer());
        await Assert.That(EvalLeaf(caller, domain, "nope").NumericRange).IsNull();
    }

    [Test]
    public async Task Eval_ToManyRelationship_IsUnknown() {
        foreach (var card in new[] { RelationshipCardinality.OneToMany, RelationshipCardinality.ManyToMany }) {
            var (caller, domain) = Build(Rel("cust", card), Customer());
            await Assert.That(EvalLeaf(caller, domain, "cust").NumericRange).IsNull();
        }
    }

    [Test]
    public async Task Eval_NoDomain_IsUnknown() {
        var (caller, _) = Build(Rel("cust", RelationshipCardinality.OneToOne), Customer());
        await Assert.That(EvalLeaf(caller, null, "cust").NumericRange).IsNull();
    }

    [Test]
    public async Task Eval_MissingTargetEntity_IsUnknown() {
        var (caller, domain) = Build(Rel("cust", RelationshipCardinality.OneToOne, "Ghost"), Customer());
        await Assert.That(EvalLeaf(caller, domain, "cust").NumericRange).IsNull();
    }

    [Test]
    public async Task Eval_AmbiguousRelationshipName_IsUnknown() {
        var caller = new Entity("Order",
            [new Property("Amount", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])],
            [], [], []) {
            Navigations = [Rel("cust", RelationshipCardinality.OneToOne), Rel("cust", RelationshipCardinality.OneToOne)]
        };
        await Assert.That(EvalLeaf(caller, new Domain("D", [caller, Customer()]), "cust").NumericRange).IsNull();
    }

    [Test]
    public async Task Eval_AmbiguousTargetEntity_IsUnknown() {
        var (caller, domain) = Build(Rel("cust", RelationshipCardinality.OneToOne), Customer(), Customer());
        await Assert.That(EvalLeaf(caller, domain, "cust").NumericRange).IsNull();
    }
}