using Poly.DomainModeling.Compile;

namespace Poly.Tests.DomainModeling.Compile;

public sealed class ArtifactIdTests {
    [Test]
    public async Task Create_FormatsNamePathAndType() {
        var id = ArtifactId.Create(["Hotel", "Reservation", "Confirm"], "method");

        await Assert.That(id.ToString()).IsEqualTo("Hotel/Reservation/Confirm#method");
        await Assert.That(id.Path).IsEqualTo("Hotel/Reservation/Confirm");
        await Assert.That(id.Segments).IsEquivalentTo(new[] { "Hotel", "Reservation", "Confirm" });
        await Assert.That(id.Type).IsEqualTo("method");
    }

    [Test]
    [Arguments("Hotel/Reservation/Confirm#method")]
    [Arguments("Hotel#module")]
    [Arguments("Hotel/Reservation#stage-enum")]
    [Arguments("Hotel/Reservation/Confirm#Method")]
    public async Task Parse_ThenToString_RoundTrips(string text) {
        await Assert.That(ArtifactId.Parse(text).ToString()).IsEqualTo(text);
    }

    [Test]
    public async Task Equality_SameNamePathAndType_IsEqual() {
        var parsed = ArtifactId.Parse("Hotel/Reservation/Confirm#method");
        var created = ArtifactId.Create(["Hotel", "Reservation", "Confirm"], "method");

        await Assert.That(parsed).IsEqualTo(created);
        await Assert.That(parsed.GetHashCode()).IsEqualTo(created.GetHashCode());
    }

    [Test]
    [Arguments("Hotel/Reservation/Confirm#method", "Hotel/Reservation/Confirm#type")]
    [Arguments("Hotel/Reservation/Confirm#method", "Hotel/Reservation/Cancel#method")]
    [Arguments("Hotel/Reservation/Confirm#method", "Hotel/Reservation#method")]
    [Arguments("Hotel/Reservation/Confirm#method", "hotel/reservation/confirm#method")]
    [Arguments("Hotel/Reservation/Confirm#method", "Hotel/Reservation/Confirm#Method")]
    public async Task Equality_DifferentPathTypeOrCase_IsNotEqual(string left, string right) {
        await Assert.That(ArtifactId.Parse(left)).IsNotEqualTo(ArtifactId.Parse(right));
    }

    [Test]
    [Arguments("")]
    [Arguments("Hotel/Reservation/Confirm")]
    [Arguments("#method")]
    [Arguments("Hotel#")]
    [Arguments("Hotel/Reservation/Confirm#method#extra")]
    [Arguments("Hotel//Confirm#method")]
    [Arguments("/Hotel#method")]
    [Arguments("Hotel/#method")]
    [Arguments("Hotel /Confirm#method")]
    [Arguments("Hotel/Confirm# method")]
    [Arguments("Hotel/Confirm#me thod")]
    [Arguments(" Hotel/Confirm#method")]
    [Arguments("Hotel/Confirm#method\n")]
    public async Task Parse_Malformed_Throws(string text) {
        await Assert.That(() => ArtifactId.Parse(text)).Throws<FormatException>();
    }

    [Test]
    public async Task Create_EmptyPath_Throws() {
        await Assert.That(() => ArtifactId.Create([], "method")).Throws<FormatException>();
    }

    [Test]
    [Arguments("Hotel/Reservation", "method")]
    [Arguments("Hotel#Reservation", "method")]
    [Arguments("Hotel Reservation", "method")]
    [Arguments("", "method")]
    [Arguments("Hotel", "")]
    [Arguments("Hotel", "me/thod")]
    [Arguments("Hotel", "me#thod")]
    public async Task Create_InvalidNameOrType_Throws(string name, string type) {
        await Assert.That(() => ArtifactId.Create([name], type)).Throws<FormatException>();
    }
}
