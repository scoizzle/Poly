using System.Reflection;

using Poly.DomainModeling.Compile;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

public sealed class ArtifactCatalogTests {
    private static Artifact Make(string path, string type, string producer = "Lower", object? payload = null) =>
        new(new ArtifactDescriptor(ArtifactId.Create(path.Split('/'), type), producer), payload);

    [Test]
    public async Task Register_AddsArtifacts_ListedInRegistrationOrder() {
        var catalog = new ArtifactCatalog();
        var confirm = Make("Hotel/Reservation/Confirm", "method");
        var module = Make("Hotel", "module");

        catalog.Register(confirm);
        catalog.Register(module);

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(2);
        await Assert.That(catalog.Artifacts[0]).IsEqualTo(confirm);
        await Assert.That(catalog.Artifacts[1]).IsEqualTo(module);
    }

    [Test]
    public async Task Register_SameId_Throws_AndKeepsFirst() {
        var catalog = new ArtifactCatalog();
        var first = Make("Hotel", "module", payload: "first");
        catalog.Register(first);

        await Assert.That(() => catalog.Register(Make("Hotel", "module", payload: "second")))
            .Throws<InvalidOperationException>();

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(1);
        await Assert.That(catalog.Find(first.Descriptor.Id)).IsEqualTo(first);
    }

    [Test]
    public async Task Register_SamePathWithDifferentType_IsAllowed() {
        var catalog = new ArtifactCatalog();

        catalog.Register(Make("Hotel/Reservation", "type"));
        catalog.Register(Make("Hotel/Reservation", "stage-enum"));

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Find_ReturnsRegisteredArtifact_OrNullWhenMissing() {
        var catalog = new ArtifactCatalog();
        var confirm = Make("Hotel/Reservation/Confirm", "method", payload: "tree");
        catalog.Register(confirm);

        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation/Confirm#method"))).IsEqualTo(confirm);
        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation/Confirm#type"))).IsNull();
        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation/Cancel#method"))).IsNull();
    }

    [Test]
    public async Task ToText_OneLinePerArtifact_SortedByIdWhateverTheRegistrationOrder() {
        var forward = new ArtifactCatalog();
        forward.Register(Make("Hotel/Room", "type"));
        forward.Register(Make("Hotel/Reservation/Confirm", "method"));
        var backward = new ArtifactCatalog();
        backward.Register(Make("Hotel/Reservation/Confirm", "method"));
        backward.Register(Make("Hotel/Room", "type"));

        const string expected = "method|Hotel/Reservation/Confirm|Lower\ntype|Hotel/Room|Lower";
        await Assert.That(forward.ToText()).IsEqualTo(expected);
        await Assert.That(backward.ToText()).IsEqualTo(expected);
    }

    [Test]
    public async Task ToText_EmptyCatalog_IsEmpty() {
        await Assert.That(new ArtifactCatalog().ToText()).IsEqualTo("");
    }

    [Test]
    public async Task PublicSurface_HasNoRemoveReplaceOrSetters() {
        var type = typeof(ArtifactCatalog);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var setters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        // Adding is the only way to change a catalog; a changed domain gets a new catalog.
        await Assert.That(methods).IsEquivalentTo(new[] { "Find", "Register", "ToText" });
        await Assert.That(setters).IsEmpty();
        await Assert.That(type.GetProperty(nameof(ArtifactCatalog.Artifacts))!.PropertyType)
            .IsEqualTo(typeof(IReadOnlyList<Artifact>));
    }
}
