using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// Identity spike: the interpreter already knows each instance's identity, but the
/// lowered module cannot read it. This probe measures the host half — proving that a
/// scoped key can be computed and moved today — so the remaining work is the module/print
/// seam, not the runtime.
/// </summary>
public class IdentitySpikeTests {
    private static (ModuleInstanceExecutor Executor, ModuleInstance Tenant, ModuleInstance Product) Setup() {
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain IdentitySpike
            Tenant: entity {
              Name: Text
              products: many Product
            }
            Product: entity {
              Sku: Text
            }
            """);
        var executor = new ModuleInstanceExecutor(domain);
        var tenant = executor.Create("Tenant", Values(("Name", "Acme")));
        var product = executor.Create("Product", Values(("Sku", "X")));
        if (!tenant.Succeeded || !product.Succeeded)
            throw new InvalidOperationException(tenant.Error ?? product.Error);

        var linked = executor.Link("products", tenant.Instance!, product.Instance!);
        if (!linked.Succeeded)
            throw new InvalidOperationException(linked.Error);

        return (executor, tenant.Instance!, product.Instance!);
    }

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    private static (string ScopeId, string Value) Key(ModuleInstance parent, ModuleInstance child, string property) =>
        (parent.Id, (string)child.Bag[property]!);

    [Test]
    public async Task Identity_IsReadableFromTheInstanceBag_WithoutAModuleMember() {
        var (executor, tenant, product) = Setup();

        var found = executor.TryFind(product.Bag, out var byBag);
        var listed = executor.TryGet(tenant.Id, out var byId);

        await Assert.That(found).IsTrue();
        await Assert.That(byBag!.Id).IsEqualTo(product.Id);
        await Assert.That(listed).IsTrue();
        await Assert.That(byId!.Bag).IsSameReferenceAs(tenant.Bag);
    }

    [Test]
    public async Task Host_CanComputeAndMoveAScopedCompositeKey() {
        var (executor, acme, product) = Setup();
        var other = executor.Create("Tenant", Values(("Name", "Other")));

        var underAcme = Key(acme, product, "Sku");
        var again = Key(acme, product, "Sku");
        var underOther = Key(other.Instance!, product, "Sku");

        await Assert.That(again).IsEqualTo(underAcme)
            .Because("the same child under the same parent has the same key");
        await Assert.That(underOther).IsNotEqualTo(underAcme)
            .Because("the parent id is part of the key");

        var unlinked = executor.Unlink("products", acme, product);
        var relinked = executor.Link("products", other.Instance!, product);
        var moved = Key(other.Instance!, product, "Sku");

        await Assert.That(unlinked.Succeeded).IsTrue();
        await Assert.That(relinked.Succeeded).IsTrue();
        await Assert.That(moved).IsEqualTo(underOther)
            .Because("the host can release and re-record a key on reparent");
    }
}