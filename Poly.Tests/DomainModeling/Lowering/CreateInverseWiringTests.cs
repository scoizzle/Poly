using System.Reflection;

using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A <c>Create</c> that supplies a singular navigation argument wires both sides of the pair: the
/// peer's paired inverse is attached, whether that inverse is a collection (already wired) or
/// singular (#587).
/// </summary>
public sealed class CreateInverseWiringTests {
    private static Assembly Print(string dsl) {
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        return ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
    }

    [Test]
    public async Task CreateWithASingularArgument_SetsThePeersSingularInverse() {
        var asm = Print("""
            domain Links
            Customer: entity {
              Name: Text
              profile: owned Profile optional
            }
            Profile: entity {
              Vip: Boolean
              owner: Customer optional
            }
            """);
        var ann = ExportedCSharp.CreateEntity(asm, "Customer", ("name", "Ann"));
        var profile = ExportedCSharp.CreateEntity(asm, "Profile", ("vip", true), ("owner", ann));

        var attached = asm.GetType("Customer")!
            .GetProperty("Profile", PrintedMembers.Any)!.GetValue(ann);
        await Assert.That(attached).IsSameReferenceAs(profile);
    }

    [Test]
    public async Task CreateWithACollectionArgument_StillSetsTheCollectionInverse() {
        var asm = Print("""
            domain Links
            Customer: entity {
              Name: Text
              orders: many owned Order
            }
            Order: entity {
              Total: Number
              customer: Customer
            }
            """);
        var ann = ExportedCSharp.CreateEntity(asm, "Customer", ("name", "Ann"));
        var order = ExportedCSharp.CreateEntity(asm, "Order", ("total", 5L), ("customer", ann));

        var orders = (System.Collections.IEnumerable)asm.GetType("Customer")!
            .GetProperty("Orders", PrintedMembers.Any)!.GetValue(ann)!;
        await Assert.That(orders.Cast<object>().Single()).IsSameReferenceAs(order);
    }
}