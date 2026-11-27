using System.Collections;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-22d (#807): a public entity's many-to-many navigation to an internal entity prints <c>internal</c>
/// (<see cref="Poly.DomainModeling.Lowering.DomainAccess.NavigationAccess"/>), so EF's convention discovery never
/// sees it and the pack maps it explicitly. The mapping must be a skip navigation over a join table —
/// <c>HasMany(…).WithMany(…)</c>, never <c>WithOne()</c> — and the generated model must boot and round-trip.
/// Many-to-many is not expressible in the DSL, so the fixture is built through the model API.
/// </summary>
public sealed class ManyToManySkipNavigationTests {
    /// <summary>Public <c>Order</c> with an unpaired many-to-many navigation to internal <c>Item</c>.</summary>
    private static Domain IssueShape(string uses = "persistence") {
        var item = new Entity("Item", [], [], [], []);
        var order = new Entity("Order", [], [], [], []) with { Visibility = Visibility.Public };
        var navigation = new Relationship(
            "items", new DomainTypeReference("Order"), new DomainTypeReference("Item"),
            RelationshipCardinality.ManyToMany, []);
        return DomainTestFactory.Create("Shop", [item, order], [navigation])
            with { Extensions = [uses] };
    }

    private static MethodDefinitionNode OnModelCreating(Domain domain) {
        var unit = GenerationAssertions.DbContextIr(domain);
        return unit.FindType($"{domain.Name}DbContext")?.FindMethod("OnModelCreating")
            ?? throw new InvalidOperationException("The generated DbContext has no OnModelCreating.");
    }

    [Test]
    public async Task ExplicitMapping_UnpairedManyToManyToInternalEntity_IsASkipNavigation() {
        var omc = OnModelCreating(IssueShape());

        var shapes = omc.FindInvocations("WithOne").Concat(omc.FindInvocations("WithMany"))
            .Select(mapping => string.Join(".", Enumerable.Reverse(mapping.GetFluentChain())))
            .ToList();
        await Assert.That(shapes).IsEquivalentTo(["HasMany.WithMany"]).Because(string.Join(",", shapes));

        // EF's navigation lookup does not see a skip navigation: the access-mode call must ask for the right one.
        var lookups = omc.FindInvocations("FindSkipNavigation").Concat(omc.FindInvocations("FindNavigation"))
            .Select(lookup => lookup.ResolveMethodName())
            .ToList();
        await Assert.That(lookups).Contains("Metadata.FindSkipNavigation");
    }

    [Test]
    public async Task GeneratedModel_UnpairedManyToManyToInternalEntity_BootsWithAJoinTable() {
        using var model = EfModel.Create(IssueShape("sqlite"));

        await Assert.That(model.BuildError).IsNull().Because(model.BuildError ?? "");
        model.EnsureSchema();

        var skip = model.Entity("Order").GetSkipNavigations()
            .Single(n => n.Name == "Items");
        await Assert.That(skip.TargetEntityType.ClrType.Name).IsEqualTo("Item");
        await Assert.That(skip.JoinEntityType.GetTableName()).IsNotNull();
        await Assert.That(model.Entity("Order").GetNavigations().Any(n => n.Name == "Items")).IsFalse();
    }

    [Test]
    public async Task GeneratedModel_UnpairedManyToManyToInternalEntity_WritesAndReadsBothSides() {
        using var model = EfModel.Create(IssueShape("sqlite"));
        await Assert.That(model.BuildError).IsNull().Because(model.BuildError ?? "");
        model.EnsureSchema();

        var orderType = model.Entity("Order").ClrType;
        var itemType = model.Entity("Item").ClrType;
        var first = Construct(itemType);
        var second = Construct(itemType);
        var items = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
        items.Add(first);
        items.Add(second);
        var order = Construct(orderType, items);

        model.Db.Add(order);
        model.Db.AddRange(first, second);
        await model.Db.SaveChangesAsync();
        var key = model.Db.Entry(order).Property("Id").CurrentValue;
        model.Db.ChangeTracker.Clear();

        var read = (await model.Db.FindAsync(orderType, key))!;
        // The skip navigation is not lazy-loaded: a read has to name it, and the property prints internal on a
        // public owner, so this test names it the way another assembly could not.
        var collection = model.Db.Entry(read).Collection("Items");
        await collection.LoadAsync();
        var loaded = (System.Collections.IEnumerable)collection.CurrentValue!;
        await Assert.That(loaded.Cast<object>().Count()).IsEqualTo(2);
    }

    /// <summary>The emitted entity's own factory: its constructor is private and <c>Create</c> is the door.</summary>
    private static object Construct(Type type, params object[] values) {
        var factory = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
        var arguments = factory.GetParameters()
            .Select(parameter => values.FirstOrDefault(v => parameter.ParameterType.IsInstanceOfType(v)))
            .ToArray();
        var created = factory.Invoke(null, arguments)!;
        if (type.IsInstanceOfType(created))
            return created;
        var value = created.GetType().GetProperty("Value")?.GetValue(created);
        return value is not null && type.IsInstanceOfType(value)
            ? value
            : throw new InvalidOperationException($"{type.Name}.Create returned {created.GetType().Name}.");
    }
}