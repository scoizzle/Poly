using System.Reflection;

using Poly.DomainModeling.Compile;

using Artifact = Poly.DomainModeling.Compile.Artifact;

namespace Poly.Tests.DomainModeling.Compile;

public sealed class ArtifactCatalogTests {
    private static Artifact Make(string path, string type, string producer = "Lower", object? payload = null,
        params string[] pointsAt) =>
        new(new ArtifactDescriptor(
                ArtifactId.Create(path.Split('/'), type),
                producer,
                [.. pointsAt.Select(ArtifactId.Parse)]),
            payload);

    /// <summary>A catalog with the artifact types most tests use declared, none allowed to point at another.</summary>
    private static ArtifactCatalog NewCatalog() {
        var catalog = new ArtifactCatalog();
        foreach (var type in new[] { "method", "module", "type", "stage-enum", "t", "z", "b" })
            catalog.DeclareType(type, mayPointAt: []);
        return catalog;
    }

    [Test]
    public async Task Register_AddsArtifacts_ListedInRegistrationOrder() {
        var catalog = NewCatalog();
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
        var catalog = NewCatalog();
        var first = Make("Hotel", "module", payload: "first");
        catalog.Register(first);

        await Assert.That(() => catalog.Register(Make("Hotel", "module", payload: "second")))
            .Throws<InvalidOperationException>();

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(1);
        await Assert.That(catalog.Find(first.Descriptor.Id)).IsEqualTo(first);
    }

    [Test]
    public async Task Register_SamePathWithDifferentType_IsAllowed() {
        var catalog = NewCatalog();

        catalog.Register(Make("Hotel/Reservation", "type"));
        catalog.Register(Make("Hotel/Reservation", "stage-enum"));

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Find_ReturnsRegisteredArtifact_OrNullWhenMissing() {
        var catalog = NewCatalog();
        var confirm = Make("Hotel/Reservation/Confirm", "method", payload: "tree");
        catalog.Register(confirm);

        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation/Confirm#method"))).IsEqualTo(confirm);
        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation/Confirm#type"))).IsNull();
        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation/Cancel#method"))).IsNull();
    }

    [Test]
    public async Task ToText_OneLinePerArtifact_SortedByIdWhateverTheRegistrationOrder() {
        var forward = NewCatalog();
        forward.Register(Make("Hotel/Room", "type"));
        forward.Register(Make("Hotel/Reservation/Confirm", "method"));
        var backward = NewCatalog();
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
    public async Task ToText_SortsOrdinallyByPathThenType_NotByTypeProducerOrCulture() {
        var catalog = NewCatalog();
        catalog.Register(Make("apple", "t", producer: "A"));
        catalog.Register(Make("a_b", "t", producer: "B"));
        catalog.Register(Make("a/b", "t", producer: "C"));
        catalog.Register(Make("a", "z", producer: "D"));
        catalog.Register(Make("a", "b", producer: "E"));
        catalog.Register(Make("Zebra", "t", producer: "Z"));

        // Uppercase sorts before lowercase, a path ties on its type, and "a#z" before "a/b#t" because '#' sorts below '/'.
        await Assert.That(catalog.ToText()).IsEqualTo(
            "t|Zebra|Z\nb|a|E\nz|a|D\nt|a/b|C\nt|a_b|B\nt|apple|A");
    }

    [Test]
    public async Task Register_Null_Throws() {
        await Assert.That(() => new ArtifactCatalog().Register(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Artifacts_CannotBeEmptiedOrChangedThroughACast() {
        var catalog = NewCatalog();
        catalog.Register(Make("Hotel", "module"));

        var list = (IList<Artifact>)catalog.Artifacts;

        await Assert.That(() => list.Clear()).Throws<NotSupportedException>();
        await Assert.That(() => list.RemoveAt(0)).Throws<NotSupportedException>();
        await Assert.That(() => list[0] = Make("Other", "module")).Throws<NotSupportedException>();
        await Assert.That(catalog.Artifacts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Register_TypeNotDeclared_Throws_AndAddsNothing() {
        var catalog = NewCatalog();

        await Assert.That(() => catalog.Register(Make("Hotel/Reservation", "made-up-type")))
            .Throws<InvalidOperationException>();

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(0);
        await Assert.That(catalog.Find(ArtifactId.Parse("Hotel/Reservation#made-up-type"))).IsNull();
    }

    [Test]
    public async Task DeclareType_SameTypeTwice_Throws_AndKeepsFirstDeclaration() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);

        await Assert.That(() => catalog.DeclareType("method", mayPointAt: ["module"]))
            .Throws<InvalidOperationException>();

        catalog.Register(Make("Hotel/Confirm", "method", pointsAt: "Hotel/Room#type"));
        await Assert.That(() => catalog.Register(Make("Hotel/Cancel", "method", pointsAt: "Hotel#module")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Register_PointingAtATypeItMayNotPointAt_Throws_AndAddsNothing() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);
        catalog.DeclareType("type", mayPointAt: []);
        catalog.DeclareType("module", mayPointAt: []);

        await Assert.That(() => catalog.Register(Make("Hotel/Confirm", "method", pointsAt: "Hotel#module")))
            .Throws<InvalidOperationException>();

        await Assert.That(catalog.Artifacts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FindDanglingOrWrongType_AllReferencesResolve_IsEmpty_WhateverTheRegistrationOrder() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);
        catalog.DeclareType("type", mayPointAt: []);

        catalog.Register(Make("Hotel/Confirm", "method", pointsAt: "Hotel/Room#type"));
        catalog.Register(Make("Hotel/Room", "type"));

        await Assert.That(catalog.FindDanglingOrWrongType()).IsEmpty();
    }

    [Test]
    public async Task FindDanglingOrWrongType_NoArtifactAtTheTargetPath_IsDangling() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);
        catalog.DeclareType("type", mayPointAt: []);
        catalog.Register(Make("Hotel/Confirm", "method", pointsAt: "Hotel/Room#type"));
        catalog.Register(Make("Hotel/Guest", "type"));

        var problems = catalog.FindDanglingOrWrongType();

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).IsEqualTo(new ArtifactReferenceProblem(
            ArtifactId.Parse("Hotel/Confirm#method"),
            ArtifactId.Parse("Hotel/Room#type"),
            ArtifactReferenceProblemKind.Dangling));
    }

    [Test]
    public async Task FindDanglingOrWrongType_TargetPathHasOnlyOtherTypes_IsWrongType() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);
        catalog.DeclareType("type", mayPointAt: []);
        catalog.DeclareType("module", mayPointAt: []);
        catalog.Register(Make("Hotel/Confirm", "method", pointsAt: "Hotel/Room#type"));
        catalog.Register(Make("Hotel/Room", "module"));

        var problems = catalog.FindDanglingOrWrongType();

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).IsEqualTo(new ArtifactReferenceProblem(
            ArtifactId.Parse("Hotel/Confirm#method"),
            ArtifactId.Parse("Hotel/Room#type"),
            ArtifactReferenceProblemKind.WrongType));
    }

    [Test]
    public async Task FindDanglingOrWrongType_ListsEveryProblem_InRegistrationOrder_AsAReadOnlyView() {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType("method", mayPointAt: ["type"]);
        catalog.DeclareType("type", mayPointAt: []);
        catalog.Register(Make("A", "method", pointsAt: ["X#type", "Y#type"]));
        catalog.Register(Make("B", "method", pointsAt: "Z#type"));

        var problems = catalog.FindDanglingOrWrongType();

        await Assert.That(string.Join(" ", problems.Select(p => p.Target))).IsEqualTo("X#type Y#type Z#type");
        await Assert.That(() => ((IList<ArtifactReferenceProblem>)problems).Clear()).Throws<NotSupportedException>();
    }

    [Test]
    public async Task PublicSurface_IsExactlyTheAllowList() {
        var type = typeof(ArtifactCatalog);
        var members = type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => $"{m.MemberType} {m.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Adding is the only way to change a catalog; a changed domain gets a new catalog.
        // A new public member (a remove, a mutable collection, a static helper) must be added here on purpose.
        await Assert.That(members).IsEquivalentTo(new[] {
            "Constructor .ctor",
            "Method DeclareType",
            "Method Find",
            "Method FindDanglingOrWrongType",
            "Method Register",
            "Method ToText",
            "Method get_Artifacts",
            "Property Artifacts",
        });
        await Assert.That(type.GetInterfaces()).IsEmpty();
        await Assert.That(type.GetProperty(nameof(ArtifactCatalog.Artifacts))!.PropertyType)
            .IsEqualTo(typeof(IReadOnlyList<Artifact>));
    }
}
