using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// End-to-end tests: V3 domain with a Policy → lower guard expression → VM execute.
///
/// This is the primary WS8 product proof: a real Policy attached to a domain entity,
/// evaluated via the VM path with C# record instances.
/// </summary>
public class PolicyVmEvaluationTests {
    private record Person(string Name, int Age);
    private record Order(string Status, decimal Total);
    private record Product(string Name, int Stock);

    [Test]
    public async Task Policy_AgeGuard_EvaluatesOnVm_TrueForAdult() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property(nameof(Person.Age)),
                DomainExpression.Literal(18)));

        var adult = policy.CompileVMPredicate<Person>()(new Person("Alice", 25));
        var minor = policy.CompileVMPredicate<Person>()(new Person("Bob", 15));

        await Assert.That(adult).IsTrue();
        await Assert.That(minor).IsFalse();
    }

    [Test]
    public async Task Policy_AgeGuard_EvaluatesOnVm_BoundaryValues() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property(nameof(Person.Age)),
                DomainExpression.Literal(18)));

        await Assert.That(policy.CompileVMPredicate<Person>()(new Person("A", 18))).IsTrue();
        await Assert.That(policy.CompileVMPredicate<Person>()(new Person("B", 17))).IsFalse();
        await Assert.That(policy.CompileVMPredicate<Person>()(new Person("C", 0))).IsFalse();
    }

    [Test]
    public async Task Policy_CompositeGuard_AndCondition() {
        var policy = new Policy("AdultAlice",
            DomainExpression.And(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(Person.Age)),
                    DomainExpression.Literal(18)),
                DomainExpression.Equal(
                    DomainExpression.Property(nameof(Person.Name)),
                    DomainExpression.Literal("Alice"))));

        var vm = policy.CompileVMPredicate<Person>();
        await Assert.That(vm(new Person("Alice", 25))).IsTrue();
        await Assert.That(vm(new Person("Bob", 25))).IsFalse();
        await Assert.That(vm(new Person("Alice", 15))).IsFalse();
    }

    [Test]
    public async Task Policy_CompositeGuard_OrCondition() {
        var policy = new Policy("VipOrSenior",
            DomainExpression.Or(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(Person.Age)),
                    DomainExpression.Literal(65)),
                DomainExpression.Equal(
                    DomainExpression.Property(nameof(Person.Name)),
                    DomainExpression.Literal("VIP"))));

        var vm = policy.CompileVMPredicate<Person>();
        await Assert.That(vm(new Person("VIP", 30))).IsTrue();
        await Assert.That(vm(new Person("Senior", 70))).IsTrue();
        await Assert.That(vm(new Person("Regular", 30))).IsFalse();
    }

    [Test]
    public async Task Policy_NegatedGuard_FlipsResult() {
        var policy = new Policy("NotAdult",
            DomainExpression.Not(
                DomainExpression.GreaterThanOrEqual(
                    DomainExpression.Property(nameof(Person.Age)),
                    DomainExpression.Literal(18))));

        var vm = policy.CompileVMPredicate<Person>();
        await Assert.That(vm(new Person("Minor", 15))).IsTrue();
        await Assert.That(vm(new Person("Adult", 18))).IsFalse();
    }

    [Test]
    public async Task Policy_PropertyInequality_StringEquality() {
        var policy = new Policy("ActiveOrder",
            DomainExpression.Equal(
                DomainExpression.Property(nameof(Order.Status)),
                DomainExpression.Literal("Active")));

        var vm = policy.CompileVMPredicate<Order>();
        await Assert.That(vm(new Order("Active", 100))).IsTrue();
        await Assert.That(vm(new Order("Cancelled", 50))).IsFalse();
    }

    [Test]
    public async Task Policy_ProductStock_NonNegative() {
        var policy = new Policy("PositiveStock",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property(nameof(Product.Stock)),
                DomainExpression.Literal(0)));

        var vm = policy.CompileVMPredicate<Product>();
        await Assert.That(vm(new Product("Widget", 10))).IsTrue();
        await Assert.That(vm(new Product("Broken", -1))).IsFalse();
        await Assert.That(vm(new Product("Empty", 0))).IsTrue();
    }

    [Test]
    public async Task Policy_NotEqualGuard_RejectsExactValue() {
        var policy = new Policy("NotEmptyString",
            DomainExpression.NotEqual(
                DomainExpression.Property(nameof(Person.Name)),
                DomainExpression.Literal("")));

        var vm = policy.CompileVMPredicate<Person>();
        await Assert.That(vm(new Person("Alice", 30))).IsTrue();
        await Assert.That(vm(new Person("", 25))).IsFalse();
    }

    [Test]
    public async Task Policy_LessThan_Guard() {
        var policy = new Policy("Junior",
            DomainExpression.LessThan(
                DomainExpression.Property(nameof(Person.Age)),
                DomainExpression.Literal(18)));

        var vm = policy.CompileVMPredicate<Person>();
        await Assert.That(vm(new Person("Child", 12))).IsTrue();
        await Assert.That(vm(new Person("Adult", 18))).IsFalse();
        await Assert.That(vm(new Person("Adult", 25))).IsFalse();
    }

    // ── Domain-attached policy tests (WS8 review finding) ────────────────
    //
    // These tests go through the full DomainFactory → evolve → AddPolicyToEntity
    // path, then extract the Policy from the domain graph and evaluate on VM.

    [Test]
    public async Task Policy_DomainAttached_EvaluatesFromDomainGraph() {
        var domain = DomainFactory.Create("TestDomain",
            builder => builder
                .AddEntity("Person")
                .AddPropertyToEntity("Person",
                    new Property("Age", new DomainTypeReference("Number"), []))
                .AddPolicyToEntity("Person", "Adult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18))));

        // Find the policy on the entity in the domain graph
        var entity = domain.Types.OfType<Entity>().Single();
        var policy = entity.Policies.Single(p => p.Name == "Adult");
        await Assert.That(policy).IsNotNull();

        // Evaluate on VM with CLR record
        var vm = policy.CompileVMPredicate<Person>();
        await Assert.That(vm(new Person("Adult", 25))).IsTrue();
        await Assert.That(vm(new Person("Minor", 15))).IsFalse();
        await Assert.That(vm(new Person("Boundary", 18))).IsTrue();
    }

    [Test]
    public async Task Policy_DomainAttached_ComplexGuardExtractedFromDomain() {
        var domain = DomainFactory.Create("Shop",
            builder => builder
                .AddEntity("Order")
                .AddPropertyToEntity("Order",
                    new Property("Total", new DomainTypeReference("Number"), []))
                .AddPropertyToEntity("Order",
                    new Property("Status", new DomainTypeReference("Text"), []))
                .AddPolicyToEntity("Order", "LargeActive",
                    DomainExpression.And(
                        DomainExpression.GreaterThan(
                            DomainExpression.Property("Total"),
                            DomainExpression.Literal(100)),
                        DomainExpression.Equal(
                            DomainExpression.Property("Status"),
                            DomainExpression.Literal("Active")))));

        var entity = domain.Types.OfType<Entity>().Single();
        var policy = entity.Policies.Single(p => p.Name == "LargeActive");

        var vm = policy.CompileVMPredicate<Order>();
        await Assert.That(vm(new Order("Active", 200))).IsTrue();
        await Assert.That(vm(new Order("Active", 50))).IsFalse();
        await Assert.That(vm(new Order("Cancelled", 200))).IsFalse();
    }

    // ── Slice 2 — Policy runtime (direct API only) ─────────────────

    // 2.3: Evaluate product path — true and false on same policy
    [Test]
    public async Task Evaluate_AgePolicy_TrueAndFalse_ExpectedResults() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        await Assert.That(policy.Evaluate(new Person("Alice", 25))).IsTrue();
        await Assert.That(policy.Evaluate(new Person("Bob", 15))).IsFalse();
        await Assert.That(policy.Evaluate(new Person("Boundary", 18))).IsTrue();
    }

    // 2.4: Property name alignment — mismatch gives wrong result
    [Test]
    public async Task PropertyName_Mismatch_GivesIncorrectResult() {
        // Policy references "Years" (not "Age") but subject has property "Age".
        // The VM reads "Years" from the record, but records don't have "Years".
        // This should produce a wrong/zero result — documenting that names must match.
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Years"), // does NOT match Person.Age
                DomainExpression.Literal(18)));

        // Compiles but evaluates incorrectly because "Years" is not a property
        var fn = policy.CompileVMPredicate<Person>();
        // A nonexistent property reads as 0 (default long), so 0 >= 18 is false
        await Assert.That(fn(new Person("Adult", 25))).IsFalse();
        await Assert.That(fn(new Person("Child", 5))).IsFalse();
    }

    // 2.5: Domain-attached policy on canonical Person entity
    // (Primary coverage in DomainValidatedEvaluationTests.DomainEntity_Property_ValidatedAndEvaluated_VmPipeline)
    // This test uses the ClrTypeEntityMapping path for entity creation.
    [Test]
    public async Task DomainAttached_CanonicalPerson_EvaluatesTrueAndFalse() {
        var domain = DomainFactory.Create("Demo",
            b => b.AddEntityFrom<Person>("Person")
                .AddPolicyToEntity("Person", "Adult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18))));

        var entity = domain.Types.OfType<Entity>().Single();
        var policy = entity.Policies.Single();

        // Evaluate through domain-validated overload
        var vm = policy.CompileVMPredicate<Person>(entity);
        await Assert.That(vm(new Person("Adult", 25))).IsTrue();
        await Assert.That(vm(new Person("Minor", 15))).IsFalse();
        await Assert.That(vm(new Person("Boundary", 18))).IsTrue();
    }
}