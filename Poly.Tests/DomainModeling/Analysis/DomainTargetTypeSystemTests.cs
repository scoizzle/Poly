using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

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
    public async Task Session_TargetIsTheBuildersOption_ElseCSharp() {
        await Assert.That(SessionBuilder.CreateEmpty().LoadCore().Build().TargetTypeSystem).IsSameReferenceAs(CSharpTypeSystem.Shared);
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
        await Assert.That(underLedger.TypeSystem).IsTypeOf<LedgerHost>();
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
}