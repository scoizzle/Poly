namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// #572: a create (or a link) that puts a child into a collection sets the child's paired
/// singular inverse on both hosts. The org-chart shape from the issue: the parent's
/// <c>Create(reports: [kid])</c> leaves <c>boss.Reports</c> holding the kid, so
/// <c>kid.Manager</c> must be the boss rather than null.
/// </summary>
public class CreateCollectionInverseParityTests {
    const string OrgChart = """
        domain Org

        Employee: entity {
          Email: Text
          manager: Employee optional
          reports: many Employee via manager
        }
        """;

    [Test]
    public async Task CreateWithACollectionArgument_SetsTheChildsSingularInverse() {
        var scenario = ParityScenario.FromDsl(OrgChart, "ParityCreateCollectionInverse");

        var (simulate, printed) = scenario.Run(side => {
            var kid = side.Create("Employee", ("Email", "kid@x.com"));
            side.Create("Employee", ("Email", "boss@x.com"), ("reports", new[] { kid }));
            side.Use(kid);
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcomes in new[] { simulate, printed }) {
            await Assert.That(outcomes[1].Success).IsTrue()
                .Because($"create failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
            await Assert.That(outcomes[1].State["reports"]).IsEqualTo("1");
            await Assert.That(outcomes[2].State["manager"]).IsEqualTo("1");
        }
    }

    [Test]
    public async Task CreateWithACollectionArgument_RefusesAChildWithAnotherOwner() {
        var scenario = ParityScenario.FromDsl(OrgChart, "ParityCreateCollectionSecondOwner");

        var (simulate, printed) = scenario.Run(side => {
            var kid = side.Create("Employee", ("Email", "kid@x.com"));
            side.Create("Employee", ("Email", "first@x.com"), ("reports", new[] { kid }));
            side.Create("Employee", ("Email", "second@x.com"), ("reports", new[] { kid }));
            side.Use(kid);
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcomes in new[] { simulate, printed }) {
            await Assert.That(outcomes[1].Success).IsTrue()
                .Because($"create failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
            await Assert.That(outcomes[2].Success).IsFalse()
                .Because("a child already owned by another manager is not re-pointed");
            await Assert.That(outcomes[3].State["manager"]).IsEqualTo("1");
        }
    }
}