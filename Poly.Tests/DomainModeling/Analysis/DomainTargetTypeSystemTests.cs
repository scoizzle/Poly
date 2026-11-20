using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// The domain asks its session's target type system (#998 T4d; docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md):
/// the domain type model's assignment verdict, and the operator stamps T4a moved onto the analysis' target.
/// </summary>
public class DomainTargetTypeSystemTests {
    private static (Domain Domain, DomainExpression Sum) Build() {
        var sum = DomainExpression.Add(DomainExpression.Property("N"), DomainExpression.Property("M"));
        var host = new Entity("H",
            [
                new Property("N", new DomainTypeReference("Number"), []),
                new Property("M", new DomainTypeReference("Number"), []),
            ],
            [],
            [new Policy("Check", DomainExpression.Equal(sum, DomainExpression.Literal(1L)))],
            []);
        return (new Domain("T", [
            new DmPrimitive("Number", PrimitiveSemantics.Number, []),
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
            host]), sum);
    }

    [Test]
    public async Task Model_Assign_AsksTheTargetSystem() {
        var (domain, _) = Build();
        var maps = new TypeMappingRegistry();

        var csharp = new DomainTypeModel(domain, maps, CSharpTypeSystem.Shared);
        await Assert.That(csharp.Assign(csharp.Resolve("Number"), csharp.Resolve("Decimal"))).IsEqualTo(Assignability.Widening)
            .Because("C# widens long to decimal implicitly");

        var ledger = new DomainTypeModel(domain, maps, new LedgerHost());
        await Assert.That(ledger.Assign(ledger.Resolve("Number"), ledger.Resolve("Decimal"))).IsEqualTo(Assignability.None)
            .Because("the ledger has no conversion between two CLR numbers");
    }

    [Test]
    public async Task Session_TargetIsTheBuildersOption_ElseTheDomainOverCSharp() {
        var byDefault = SessionBuilder.CreateEmpty().LoadCore().Build().TargetTypeSystem;
        await Assert.That(byDefault).IsTypeOf<DomainTypeSystem>().Because("the domain language is a type system layered on C# (T5)");
        await Assert.That(byDefault.Target).IsSameReferenceAs(CSharpTypeSystem.Shared);
        var ledger = new LedgerHost();
        await Assert.That(SessionBuilder.CreateEmpty().LoadCore().WithTargetTypeSystem(ledger).Build().TargetTypeSystem)
            .IsSameReferenceAs(ledger);
    }

    [Test]
    public async Task Session_TargetDecidesTheDomainsOperatorStamp() {
        var (domain, sum) = Build();
        var underCSharp = SessionBuilder.CreateEmpty().LoadCore().Build().Analyze(domain);
        await Assert.That(underCSharp.GetMetadata<TypedExpressionMetadata>(sum)?.Binary).IsNotNull()
            .Because("C# has long + long, so the domain stamps the sum's arithmetic");

        (domain, sum) = Build();
        var underLedger = SessionBuilder.CreateEmpty().LoadCore().WithTargetTypeSystem(new LedgerHost()).Build().Analyze(domain);
        await Assert.That(underLedger.TypeSystem.Target).IsTypeOf<LedgerHost>();
        await Assert.That(underLedger.GetMetadata<TypedExpressionMetadata>(sum)?.Binary).IsNull()
            .Because("the ledger has no operator for two CLR numbers, so nothing is stamped");
    }

    [Test]
    public async Task Session_TargetSurvivesEveryRebind() {
        // A session rebound to a domain or a host target is the same session: its target type system goes with it.
        var ledger = new LedgerHost();
        var built = SessionBuilder.CreateEmpty().LoadCore().WithTargetTypeSystem(ledger).Build();

        await Assert.That(built.WithDomain(new Domain("T", [])).TargetTypeSystem).IsSameReferenceAs(ledger);
        await Assert.That(built.WithTarget("host").TargetTypeSystem).IsSameReferenceAs(ledger);
    }

    [Test]
    public async Task Family_IsTheDefinitionsTheDomainsWordsBind_NotWhatAForeignTypeAnswers() {
        // The ledger's amount answers that it is a decimal number, but no domain word binds it: it joins no family
        // (#998 T5, decision §3.1). The CLR's decimal is bound by "Decimal" and stays in it.
        var ledger = new LedgerHost();
        var (domain, _) = Build();
        var model = new DomainTypeModel(domain, new TypeMappingRegistry(), CSharpTypeSystem.Shared);

        await Assert.That(model.KindOf(new ExprType(ledger.Amount, Optional: false, DomainName: null))).IsEqualTo(NumericKind.None);
        await Assert.That(model.KindOf(model.Resolve("Decimal"))).IsEqualTo(NumericKind.Decimal);
        await Assert.That(model.KindOf(model.Resolve("Number"))).IsEqualTo(NumericKind.Integer64);
    }

    [Test]
    public async Task DomainSystem_BindsItsWords_OnItsTarget_AndSpellsThemBack() {
        var domain = new DomainTypeSystem(new TypeMappingRegistry(), CSharpTypeSystem.Shared);
        var clr = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;

        await Assert.That(domain.Target).IsSameReferenceAs(CSharpTypeSystem.Shared);
        await Assert.That(domain.IsSame(domain.Bind("Number")!, clr.GetTypeDefinition(typeof(long)))).IsTrue();
        await Assert.That(domain.IsSame(domain.Bind("Int")!, clr.GetTypeDefinition(typeof(long)))).IsTrue().Because("an alias binds the same definition");
        await Assert.That(domain.Spell(clr.GetTypeDefinition(typeof(long)))).IsEqualTo("Number");
        await Assert.That(domain.Spell(clr.GetTypeDefinition(typeof(nint)))).IsNull().Because("no domain word binds nint");
        await Assert.That(domain.Bind("short")).IsNull()
            .Because("the interim table (DomainTypeMapping, until S3) binds the domain's words and the CLR names they lower to, nothing else");
        await Assert.That(domain.Operator(Poly.Introspection.OperatorKind.Add, domain.Bind("Number")!, domain.Bind("Number")!)).IsNotNull()
            .Because("what the domain does not own, its target answers");
    }

    [Test]
    public async Task DefaultTarget_FollowsTheSessionsWords_AcrossAUsesChange() {
        // A session reloaded for a domain with other `uses` has other words; its default domain system binds them.
        var core = SessionBuilder.CreateEmpty().LoadCore().Build();
        await Assert.That(core.TargetTypeSystem.Bind("Date")).IsNull().Because("core alone has no Date word");

        var temporal = core.WithDomain(new Domain("T", []) { Extensions = ["temporal"] });
        await Assert.That(temporal.TargetTypeSystem.Bind("Date")).IsNotNull().Because("the temporal library binds Date");
    }

    [Test]
    public async Task ConstraintApplicability_IsTheDomainsRegisteredRule_KeyedByWhatTheWordBinds() {
        // A range applies to a property whose type a domain number word binds, whatever the spelling (Number, Int,
        // Int64, Decimal); a length to one Text binds; neither to a type no domain word binds (#998 T7, decision §3.2).
        var domain = new DomainTypeSystem(new TypeMappingRegistry(), CSharpTypeSystem.Shared);
        var rule = domain.Rule<IConstraintApplicability>();
        var range = new RangeConstraint(0L, 10L);
        var length = new LengthConstraint(1, 5);
        var clr = Poly.Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;

        await Assert.That(rule).IsNotNull();
        foreach (var word in new[] { "Number", "Int", "Int64", "Int32", "Decimal", "Float" })
            await Assert.That(rule!.Applies(range, domain.Bind(word)!)).IsTrue().Because($"'{word}'");
        await Assert.That(rule!.Applies(range, domain.Bind("Text")!)).IsFalse();
        await Assert.That(rule.Applies(length, domain.Bind("String")!)).IsTrue();
        await Assert.That(rule.Applies(length, domain.Bind("Number")!)).IsFalse();
        await Assert.That(rule.Applies(range, clr.GetTypeDefinition(typeof(nint)))).IsFalse().Because("no domain word binds nint");
        await Assert.That(rule.Applies(range, new LedgerHost().Amount)).IsFalse();
        await Assert.That(CSharpTypeSystem.Shared.Rule<IConstraintApplicability>()).IsNull().Because("the rule is the domain's");
    }

    [Test]
    public async Task GuardCoverage_AsksTheDomainsRule_NotTheSpelling() {
        // Int64 is a domain number word; its range is guard-covered like Number's. Before #998 T7 only the spellings
        // Number, Int and Decimal were.
        static Entity Host(string typeName) => new("H",
            [new Property("X", new DomainTypeReference(typeName), [new RangeConstraint(0L, 10L)])], [], [], []);

        foreach (var spelling in new[] { "Number", "Int64" }) {
            var entity = Host(spelling);
            var result = GuardClosedProperties.Compute(entity, new Domain("T", [entity]));
            await Assert.That(result.Blockers.GetValueOrDefault("X") ?? "").DoesNotContain("has no constraint guard coverage")
                .Because($"'{spelling}' is a domain number word");
            await Assert.That(result.Closed).Contains("X").Because($"'{spelling}': its range is guarded at create");
        }
    }
}