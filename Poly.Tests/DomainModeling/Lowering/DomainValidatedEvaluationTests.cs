using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Proves the simplest domain-modeling concept through the full pipeline:
/// domain entity property references are validated against the domain model
/// before reaching CLR evaluation.
/// </summary>
public class DomainValidatedEvaluationTests {
    private record Person(string Name, int Age);

    [Test]
    public async Task DomainEntity_Property_ValidatedAndEvaluated_VmPipeline() {
        // Domain created from C# record shape via DomainTypeMapper
        var domain = DomainTypeMapper.CreateDomainWithEntity<Person>("Demo");
        var entity = domain.Types.OfType<Entity>().Single();

        // Add a policy referencing a property that exists on the entity
        // (DomainFactory Create cannot inline policies, so we evolve after)
        var withPolicy = new DomainEvolution(domain).Apply(
            [new AddPolicyToEntityChange("Person",
                new Policy("Adult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18))))]);

        entity = withPolicy.Root.Types.OfType<Entity>().Single();
        var policy = entity.Policies.Single();

        // Validate & evaluate
        var result = policy.CompileVMPredicate<Person>(entity);
        await Assert.That(result(new Person("Alice", 25))).IsTrue();
        await Assert.That(result(new Person("Bob", 15))).IsFalse();
    }

    [Test]
    public async Task DomainEntity_MissingProperty_ThrowsClearError() {
        var domain = DomainTypeMapper.CreateDomainWithEntity<Person>("Demo");

        // Add a policy referencing a non-existent property — now caught at analysis time
        var withPolicy = new DomainEvolution(domain).Apply(
            [new AddPolicyToEntityChange("Person",
                new Policy("HasAge",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("MissingProp"),
                        DomainExpression.Literal(18))))]);

        // The policy references "MissingProp" which doesn't exist on Person.
        // The PolicyConstraintAnalyzer now catches this at analysis time,
        // causing the evolution to roll back.
        await Assert.That(withPolicy.Succeeded).IsFalse();
        await Assert.That(withPolicy.FailureSummary).IsNotNull();
        await Assert.That(withPolicy.FailureSummary!.Contains("MissingProp")).IsTrue();
    }

    [Test]
    public async Task DomainEntity_PropertyAccess_CollectsAllReferences() {
        var expr = DomainExpression.And(
            DomainExpression.GreaterThan(
                DomainExpression.Property("Total"),
                DomainExpression.Literal(100)),
            DomainExpression.Equal(
                DomainExpression.Property("Status"),
                DomainExpression.Literal("Active")));

        var refs = PolicyEvaluator.GetReferencedProperties(expr);
        await Assert.That(refs.Count).IsEqualTo(2);
        await Assert.That(refs).Contains("Total");
        await Assert.That(refs).Contains("Status");
    }

    [Test]
    public async Task DomainEntity_PropertyAccess_SingleProperty() {
        var expr = DomainExpression.Equal(
            DomainExpression.Property("Age"),
            DomainExpression.Literal(18));

        var refs = PolicyEvaluator.GetReferencedProperties(expr);
        await Assert.That(refs.Count).IsEqualTo(1);
        await Assert.That(refs).Contains("Age");
    }
}