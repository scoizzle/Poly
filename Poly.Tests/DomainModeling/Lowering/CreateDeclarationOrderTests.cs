using System.Reflection;

using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

using TUnit.Assertions.Enums;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-07 (#558, #683): entity members, Create parameters within each block, and the
/// create guard / invariant order follow DECLARATION order. The required/trailing
/// block split itself stays until L-08 emits every Create parameter as an optional
/// <c>T? x = default</c>; within each block this pins declaration order.
/// </summary>
public sealed class CreateDeclarationOrderTests {
    /// <summary>Members are declared Price, OnHand, Sku; ordinal order is OnHand, Price, Sku.</summary>
    private const string ProductDsl = """
        domain Shop
        Product: entity {
          Price: Number range(0, 100)
          OnHand: Number range(0, 10)
          Sku: Text
        }
        """;

    // ── S1 (a)-(c): the published fact, the printed parameters, the member block ──

    [Test]
    public async Task Create_ParametersFollowDeclarationOrder_AndPropertiesEmitInDeclarationOrder() {
        var (domain, analysis) = EvolvedDomain.FromDsl(ProductDsl);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Product");

        // (a) the published fact every Create consumer reads.
        var fact = analysis.GetStructure(entity)!.ConstructorParameters.Select(p => p.Name);
        await Assert.That(fact).IsEquivalentTo(["Price", "OnHand", "Sku"], CollectionOrdering.Matching)
            .Because("ESM.ConstructorParameters is declaration order, not ordinal");

        // (b) the printed Create factory's parameters.
        var create = CreateMethod(domain, analysis, "Product");
        await Assert.That(create.Parameters!.Select(p => p.Name)).IsEquivalentTo(["price", "onHand", "sku"], CollectionOrdering.Matching);

        // (c) the emitted member block.
        var product = TypeNode(domain, analysis, "Product");
        await Assert.That(product.Properties!.Select(p => p.Name)).IsEquivalentTo(["Price", "OnHand", "Sku"], CollectionOrdering.Matching);
    }

    // ── S1 (d): the trailing defaulted block ─────────────────────────────────────

    [Test]
    public async Task Create_TrailingDefaultedParametersFollowDeclarationOrder() {
        // Zed then Abe: ordinal order is Abe, Zed.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Tag: entity {
              Zed: Number default(1)
              Abe: Number default(2)
            }
            """);

        var create = CreateMethod(domain, analysis, "Tag");
        await Assert.That(create.Parameters!.Select(p => p.Name)).IsEquivalentTo(["zed", "abe"], CollectionOrdering.Matching);
    }

    // ── A1: create guard order is declaration order ──────────────────────────────

    [Test]
    public async Task Create_WhenTwoConstraintsAreViolated_TheFirstDeclaredMessageWins() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Gate: entity {
              Zed: Number range(0, 1)
              Abe: Number range(0, 1)
            }
            """);

        // The guard order is now Zed, Abe, so a call violating both reports Zed.
        var asm = ExportedCSharp.CompileAndLoad(Print(domain, analysis));
        var create = CreateMethodInfo(asm.GetType("Gate")!);
        var failure = create.Invoke(null, [5L, 5L])!;
        var message = (string?)failure.GetType().GetProperty("ErrorMessage")!.GetValue(failure);

        await Assert.That(message).Contains("'Zed'");
    }

    // ── S2: the create-in sequence cannot shift (green before and after) ─────────

    [Test]
    public async Task CreateNav_ForwardsDefaultedOverridesBySlot() {
        // A4: invoke CreateWidgets BY NAME. The trailing block has two independent
        // producers (Properties.cs defaultedCtorParams and Create.cs
        // TrailingOmittableProperties); they agree only because both walk
        // entity.Properties. This is the only guard that they do.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Shelf: entity {
              Name: Text
              widgets: many Widget
              Add: action { create in widgets { } }
            }
            Widget: entity {
              Zed: Number default(1)
              Abe: Number default(2)
            }
            """);

        var createWidgets = TypeNode(domain, analysis, "Shelf").Methods!
            .Single(m => m.Name == "CreateWidgets");

        var asm = ExportedCSharp.CompileAndLoad(Print(domain, analysis));
        var shelf = ExportedCSharp.CreateEntity(asm, "Shelf", ("name", "s"));
        var method = shelf.GetType().GetMethod(
            "CreateWidgets", BindingFlags.Public | BindingFlags.Instance)!;

        // Named values, so a reordered parameter list still finds both slots.
        var args = new object?[method.GetParameters().Length];
        for (var i = 0; i < method.GetParameters().Length; i++) {
            args[i] = method.GetParameters()[i].Name switch {
                "zed" => 1L,
                "abe" => 42L,
                var other => throw new InvalidOperationException($"unexpected CreateWidgets parameter '{other}'")
            };
        }

        var result = method.Invoke(shelf, args)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();

        var widgets = (System.Collections.IEnumerable)shelf.GetType()
            .GetField("_widgets", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(shelf)!;
        var widget = widgets.Cast<object>().Single();

        // The Abe override must land on Abe, not on its declared neighbour Zed.
        await Assert.That(widget.GetType().GetProperty("Zed")!.GetValue(widget)).IsEqualTo(1L);
        await Assert.That(widget.GetType().GetProperty("Abe")!.GetValue(widget)).IsEqualTo(42L);
    }

    // ── S4: #558's literal repro — positional args in declaration order ──────────

    [Test]
    public async Task Create_PositionalArgsInDeclarationOrder_BindToDeclaredProperties() {
        // #558's minimal reproduction, unchanged: the equal ranges mean a swap is
        // silent in everything but the bound values.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Commerce
            uses persistence
            Product: entity {
              Sku: Text unique
              Price: Number range(0, 100000)
              OnHand: Number range(0, 100000)
              Reserved: Number range(0, 100000)
            }
            """);

        var create = CreateMethod(domain, analysis, "Product");
        await Assert.That(create.Parameters!.Select(p => p.Name)).IsEquivalentTo(["sku", "price", "onHand", "reserved"], CollectionOrdering.Matching);

        var asm = ExportedCSharp.CompileAndLoad(Print(domain, analysis));
        var printed = CreateMethodInfo(asm.GetType("Product")!);
        var result = printed.Invoke(null, ["A", 95L, 5L, 3L])!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();

        var instance = result.GetType().GetProperty("Value")!.GetValue(result)!;
        foreach (var (name, expected) in new (string, object)[] { ("Sku", "A"), ("Price", 95L), ("OnHand", 5L), ("Reserved", 3L) })
            await Assert.That(instance.GetType().GetProperty(name)!.GetValue(instance)).IsEqualTo(expected)
                .Because($"positional argument in declaration order binds {name}");

        // The simulator takes the same declared values by name; both sides hold the
        // same state, so the printed order is the only variable here.
        var outcome = ModuleExec.For(domain).Create("Product", new Dictionary<string, object?> {
            ["Sku"] = "A",
            ["Price"] = 95L,
            ["OnHand"] = 5L,
            ["Reserved"] = 3L
        });
        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Instance!.Bag["Sku"]).IsEqualTo("A");
        await Assert.That(outcome.Instance.Bag["Price"]).IsEqualTo(95L);
        await Assert.That(outcome.Instance.Bag["OnHand"]).IsEqualTo(5L);
        await Assert.That(outcome.Instance.Bag["Reserved"]).IsEqualTo(3L);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string Print(Domain domain, AnalysisResult analysis) =>
        new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));

    private static TypeDefinitionNode TypeNode(Domain domain, AnalysisResult analysis, string name) =>
        DomainProgramProjection.ToSyntax(domain, analysis).Single(t => t.Name == name);

    private static MethodDefinitionNode CreateMethod(Domain domain, AnalysisResult analysis, string entityName) =>
        TypeNode(domain, analysis, entityName).Methods!.Single(m => m.Name == "Create");

    /// <summary>The widest public static Create factory (one per entity).</summary>
    private static MethodInfo CreateMethodInfo(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
}