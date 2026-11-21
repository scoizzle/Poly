namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #584: a create argument pairs with one navigation on the peer, chosen by the declaration, never by
/// the target's type. On a self-referential entity every navigation targets the same type, so the old
/// type-only rule paired <c>referrer</c> with <c>friends</c> and put the new instance in a collection the
/// caller never named. The lowered module is the one body both hosts run, so both agree on the answer.
/// </summary>
public class SelfReferentialCreatePairingParityTests {
    // A self-relationship with two candidate navigations back and no `via`: the pairing is ambiguous,
    // so nothing is wired. `bob.Friends` must stay empty.
    const string AmbiguousDsl = """
        domain Links
        Person: entity {
          Name: Text
          referrer: Person optional
          friends: many Person
        }
        """;

    // The `via` partner settles the same shape: `friends` declares `referrer`, so a `referrer`
    // argument lands in `bob.Friends`.
    const string DeclaredDsl = """
        domain Links
        Person: entity {
          Name: Text
          referrer: Person optional
          friends: many Person via referrer
        }
        """;

    [Test]
    public async Task CreateWithSelfNavigation_NoDeclaredPair_WiresNoUnrelatedCollection() {
        var scenario = ParityScenario.FromDsl(AmbiguousDsl, "ParitySelfReferentialAmbiguous");

        var (simulate, printed) = scenario.Run(side => {
            var bob = side.Create("Person", ("Name", "Bob"));
            side.Create("Person", ("Name", "Kid"), ("referrer", bob));
            side.Use(bob);
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcomes in new[] { simulate, printed }) {
            await Assert.That(outcomes[1].Success).IsTrue()
                .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
            await Assert.That(outcomes[1].State["referrer"]).IsEqualTo("1");
            await Assert.That(outcomes[2].State["friends"]).IsEqualTo("0");
        }
    }

    [Test]
    public async Task CreateWithSelfNavigation_DeclaredViaPair_LandsInTheDeclaredCollection() {
        var scenario = ParityScenario.FromDsl(DeclaredDsl, "ParitySelfReferentialDeclaredVia");

        var (simulate, printed) = scenario.Run(side => {
            var bob = side.Create("Person", ("Name", "Bob"));
            side.Create("Person", ("Name", "Kid"), ("referrer", bob));
            side.Use(bob);
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcomes in new[] { simulate, printed }) {
            await Assert.That(outcomes[1].Success).IsTrue()
                .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
            await Assert.That(outcomes[2].State["friends"]).IsEqualTo("1");
        }
    }
}