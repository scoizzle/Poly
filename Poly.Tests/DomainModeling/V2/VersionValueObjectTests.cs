using Poly.DomainModeling.V2.Core;

namespace Poly.Tests.DomainModeling.V2;

public class VersionValueObjectTests {
    [Test]
    public async Task ModelVersion_Parse_Valid()
    {
        var version = new ModelVersion("1.2.3");

        await Assert.That(version.Major).IsEqualTo(1);
        await Assert.That(version.Minor).IsEqualTo(2);
        await Assert.That(version.Patch).IsEqualTo(3);
        await Assert.That(version.ToString()).IsEqualTo("1.2.3");
    }

    [Test]
    public async Task RuleSetVersion_Parse_Valid()
    {
        var version = new RuleSetVersion("2.0.1");

        await Assert.That(version.Major).IsEqualTo(2);
        await Assert.That(version.Minor).IsEqualTo(0);
        await Assert.That(version.Patch).IsEqualTo(1);
    }

    [Test]
    public async Task Versions_CompareInSemVerOrder()
    {
        var a = new ModelVersion("1.2.0");
        var b = new ModelVersion("1.3.0");
        var c = new ModelVersion("2.0.0");

        await Assert.That(a.CompareTo(b) < 0).IsTrue();
        await Assert.That(b.CompareTo(c) < 0).IsTrue();
    }

    [Test]
    public async Task InvalidVersion_Throws()
    {
        await Assert.That(() => new ModelVersion("1.2")).Throws<ArgumentException>();
        await Assert.That(() => new RuleSetVersion("x.y.z")).Throws<ArgumentException>();
    }
}