using System.Collections;
using System.Reflection;

using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A <c>many</c> navigation is printed as <c>IReadOnlyList&lt;T&gt;</c>. The getter must not hand out the
/// live private <c>List&lt;T&gt;</c>: a caller can downcast that and mutate the aggregate, skipping every
/// generated guard, subscription registration and constraint (#556, #541).
/// </summary>
public sealed class CollectionNavigationReadOnlyTests {
    private const string Dsl = """
        domain Shop
        Line: entity {
          Qty: Number
        }
        Cart: entity {
          lines: many Line
        }
        """;

    private static Assembly Print() {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        return ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
    }

    [Test]
    public async Task CollectionNavigation_Getter_ReturnsACopyThatCannotMutateTheAggregate() {
        var asm = Print();
        var line = ExportedCSharp.CreateEntity(asm, "Line", ("qty", 1L));
        var cart = ExportedCSharp.CreateEntity(asm, "Cart");
        var lines = (IList)asm.GetType("Cart")!
            .GetProperty("Lines", PrintedMembers.Any)!.GetValue(cart)!;

        lines.Add(line);

        var after = (IEnumerable)asm.GetType("Cart")!
            .GetProperty("Lines", PrintedMembers.Any)!.GetValue(cart)!;
        await Assert.That(after.Cast<object>().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task CollectionNavigation_Getter_StillExposesTheAttachedChildren() {
        var asm = Print();
        var line = ExportedCSharp.CreateEntity(asm, "Line", ("qty", 1L));
        var cart = ExportedCSharp.CreateEntity(asm, "Cart");
        asm.GetType("Cart")!
            .GetMethod("AttachLines", PrintedMembers.Any)!
            .Invoke(cart, [line]);

        var lines = (IEnumerable)asm.GetType("Cart")!
            .GetProperty("Lines", PrintedMembers.Any)!.GetValue(cart)!;

        await Assert.That(lines.Cast<object>().Count()).IsEqualTo(1);
    }
}