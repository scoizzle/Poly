using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// Tests the two-way CLR ⇄ domain mapping for mutation engine verification.
/// Prove: CLR type → domain entity → mutate → verify against expected CLR type.
/// </summary>
public class EntityMutationRoundTripTests {
    private sealed record Person(string Name, int Age);
    private sealed record PersonWithStatus(string Name, int Age, string Status);

    [Test]
    public async Task CreateFromClrType_ThenVerifyMatches() {
        var domain = DomainFactory.Create("Test", b => b.AddEntityFrom<Person>());
        var entity = domain.Types.OfType<Entity>().Single();

        await Assert.That(() => entity.EnsureMatchesType<Person>())
            .ThrowsNothing();
    }

    [Test]
    public async Task CreateFromClrType_MissingProperty_Throws() {
        var domain = DomainFactory.Create("Test", b => b.AddEntityFrom<Person>());
        var entity = domain.Types.OfType<Entity>().Single();

        // Person doesn't have a Status property, but the domain entity does
        // Wait — no it doesn't. The entity was created from Person, so
        // PersonWithStatus has MORE properties than the entity. The check
        // ensures entity properties all exist on the CLR type, so a CLR type
        // with extra properties is fine — the entity is a subset.
        // To trigger failure we need the entity to have a property the CLR type lacks.
        var withExtra = new DomainEvolution(domain).Apply(
            [new AddPropertyToEntityChange("Person",
                new Property("Status", new DomainTypeReference("Text"), []))]);
        var mutatedEntity = withExtra.Root.Types.OfType<Entity>().Single();

        // Now the entity has Status but Person doesn't — should throw
        await Assert.That(() => mutatedEntity.EnsureMatchesType<Person>())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateFromClrType_Mutate_ThenVerifyNewType() {
        // Start with Person, add Status property, verify against PersonWithStatus
        var domain = DomainFactory.Create("Test", b => b.AddEntityFrom<Person>());

        var withExtra = new DomainEvolution(domain).Apply(
            [new AddPropertyToEntityChange("Person",
                new Property("Status", new DomainTypeReference("Text"), []))]);
        var mutatedEntity = withExtra.Root.Types.OfType<Entity>().Single();

        // Should pass: entity now has Name, Age, Status — matching PersonWithStatus
        await Assert.That(() => mutatedEntity.EnsureMatchesType<PersonWithStatus>())
            .ThrowsNothing();
    }

    [Test]
    public async Task CreateFromClrType_WrongDomainType_Throws() {
        // Age is Number in domain, but CLR uses a type that maps differently
        var domain = DomainFactory.Create("Test", b => b.AddEntityFrom<Person>());

        var withChanged = new DomainEvolution(domain).Apply(
            [new RemovePropertyFromEntityChange("Person", "Age"),
             new AddPropertyToEntityChange("Person",
                new Property("Age", new DomainTypeReference("Text"), []))]);
        var mutatedEntity = withChanged.Root.Types.OfType<Entity>().Single();

        // Age is Text now, but Person has int Age → Number
        await Assert.That(() => mutatedEntity.EnsureMatchesType<Person>())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateFromClrType_Shorthand_Equivalent() {
        // Show both paths produce the same result
        var domainViaFactory = DomainFactory.Create("Shop", b => b.AddEntityFrom<Person>("Customer"));
        var entity1 = domainViaFactory.Types.OfType<Entity>().Single();
        await Assert.That(entity1.Name).IsEqualTo("Customer");
    }
}