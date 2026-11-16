using System.Reflection;

namespace Poly.Tests.Runtime;

/// <summary>
/// R-06: the one kind→HTTP-status table and the optimistic-concurrency token in
/// <c>Poly.Runtime</c>. These types are landed before their consumers bind them
/// (P-09 binds <see cref="IVersioned"/>, P-14 calls <see cref="DomainFailureStatus.Http"/>),
/// so this suite pins the contract shape alone.
/// <para>
/// The expected split is written out as an independent literal, never derived from
/// <see cref="DomainFailureStatus.Http"/>: a table that defaults every kind, drops one arm,
/// or flips one row has to fail here.
/// </para>
/// </summary>
public class DomainFailureStatusTests {
    /// <summary>
    /// The one table, #677 R-06 (orchestrator ruling O-4 for <c>Cascade</c>): Unique, Link,
    /// Conflict and Stage are 409; Policy is 422; Cascade is 500; the other eight are 400 (owner rulings, #677).
    /// </summary>
    static readonly Dictionary<DomainFailureKind, int> Expected = new() {
        [DomainFailureKind.Required] = 400,
        [DomainFailureKind.Range] = 400,
        [DomainFailureKind.Length] = 400,
        [DomainFailureKind.Pattern] = 400,
        [DomainFailureKind.Enum] = 400,
        [DomainFailureKind.Unique] = 409,
        [DomainFailureKind.Stage] = 409,
        [DomainFailureKind.Policy] = 422,
        [DomainFailureKind.Link] = 409,
        [DomainFailureKind.Arithmetic] = 400,
        [DomainFailureKind.Argument] = 400,
        [DomainFailureKind.Cascade] = 500,
        [DomainFailureKind.Conflict] = 409,
        [DomainFailureKind.Constraint] = 400,
    };

    [Test]
    public async Task EveryKind_HasStatus_AndTheSplitIsExactlyFourEightOneOne() {
        var kinds = Enum.GetValues<DomainFailureKind>();

        // 14 is pinned: an appended kind has to force a decision at the table, not inherit one.
        await Assert.That(kinds.Length).IsEqualTo(14);
        await Assert.That(string.Join(",", Enum.GetNames<DomainFailureKind>()))
            .IsEqualTo(string.Join(",", Expected.Keys.Select(kind => kind.ToString())));

        foreach (var kind in kinds) {
            await Assert.That(Expected.ContainsKey(kind)).IsTrue()
                .Because($"{kind} needs an explicit expected row in this test");
            await Assert.That(DomainFailureStatus.Http(kind)).IsEqualTo(Expected[kind])
                .Because($"{kind} maps to {Expected[kind]}");
        }

        var byStatus = kinds
            .GroupBy(DomainFailureStatus.Http)
            .ToDictionary(group => group.Key, group => group.OrderBy(kind => kind.ToString()).ToArray());

        await Assert.That(string.Join(",", byStatus[409].Select(kind => kind.ToString())))
            .IsEqualTo("Conflict,Link,Stage,Unique");
        await Assert.That(byStatus[400].Length).IsEqualTo(8);
        await Assert.That(string.Join(",", byStatus[422].Select(kind => kind.ToString())))
            .IsEqualTo("Policy");
        await Assert.That(string.Join(",", byStatus[500].Select(kind => kind.ToString())))
            .IsEqualTo("Cascade");
        await Assert.That(byStatus.Count).IsEqualTo(4);
    }

    [Test]
    public async Task UnknownKind_Throws_AndTheMessageNamesTheKindAndItsNumber() {
        // 999 is past the closed set. It must fail closed, never fall back to 400.
        await Assert.That(() => DomainFailureStatus.Http((DomainFailureKind)999))
            .ThrowsExactly<ArgumentOutOfRangeException>()
            .WithMessageContaining("999");

        await Assert.That(() => DomainFailureStatus.Http((DomainFailureKind)(-1)))
            .ThrowsExactly<ArgumentOutOfRangeException>()
            .WithMessageContaining("-1");
    }

    [Test]
    public async Task IVersioned_IsAReadOnlyLongToken() {
        var versioned = typeof(IVersioned);

        await Assert.That(versioned.IsInterface).IsTrue();
        await Assert.That(versioned.IsPublic).IsTrue();
        await Assert.That(versioned.Namespace).IsEqualTo("Poly.Runtime");

        var properties = versioned.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        await Assert.That(properties.Length).IsEqualTo(1);

        var version = properties[0];
        await Assert.That(version.Name).IsEqualTo("Version");
        await Assert.That(version.PropertyType).IsEqualTo(typeof(long));
        await Assert.That(version.CanRead).IsTrue();
        await Assert.That(version.CanWrite).IsFalse();

        // No default implementation and no other member: the token is a shape, not behaviour.
        await Assert.That(versioned.GetMethods(BindingFlags.Public | BindingFlags.Instance).Length)
            .IsEqualTo(1);
    }
}