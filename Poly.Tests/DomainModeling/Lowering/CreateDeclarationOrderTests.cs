using System.Reflection;

using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using TUnit.Assertions.Enums;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-07 (#558, #683): entity members, Create parameters and the create guard /
/// invariant order follow DECLARATION order. Since L-08b every Create parameter is
/// <c>T? x = default</c>, one declaration-ordered list (<c>CreateSlots</c>).
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
        var fact = analysis.GetStructure(entity)!.CreateSlots.Select(s => s.Name);
        await Assert.That(fact).IsEquivalentTo(["Price", "OnHand", "Sku"], CollectionOrdering.Matching)
            .Because("ESM.CreateSlots is declaration order, not ordinal");

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
        var message = (string?)failure.GetType().GetProperty("ErrorMessage", PrintedMembers.Any)!.GetValue(failure);

        await Assert.That(message).Contains("'Zed'");
    }

    // ── S2: the create-in sequence cannot shift (green before and after) ─────────

    [Test]
    public async Task CreateNav_ForwardsDefaultedOverridesBySlot() {
        // A4: invoke CreateWidgets BY NAME, so the override must land on its own slot.
        // Create{Nav} and the target's Create both read the published CreateSlots.
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
            "CreateWidgets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

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
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();

        var widgets = (System.Collections.IEnumerable)shelf.GetType()
            .GetField("_widgets", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(shelf)!;
        var widget = widgets.Cast<object>().Single();

        // The Abe override must land on Abe, not on its declared neighbour Zed.
        await Assert.That(widget.GetType().GetProperty("Zed", PrintedMembers.Any)!.GetValue(widget)).IsEqualTo(1L);
        await Assert.That(widget.GetType().GetProperty("Abe", PrintedMembers.Any)!.GetValue(widget)).IsEqualTo(42L);
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
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();

        var instance = result.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(result)!;
        foreach (var (name, expected) in new (string, object)[] { ("Sku", "A"), ("Price", 95L), ("OnHand", 5L), ("Reserved", 3L) })
            await Assert.That(instance.GetType().GetProperty(name, PrintedMembers.Any)!.GetValue(instance)).IsEqualTo(expected)
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

    // ── L-08b: every Create parameter is `T? x = default`, in declaration order ──

    [Test]
    public async Task Create_SignatureIsDeclarationOrderAllOptional() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Owner: entity { Name: Text }
            Pet: entity {
              Name: Text
              owner: Owner
              Age: Number
              Nick: Text optional
              Legs: Number default(4)
            }
            """);

        var asm = ExportedCSharp.CompileAndLoad(Print(domain, analysis));
        var parameters = CreateMethodInfo(asm.GetType("Pet")!).GetParameters()
            .Where(p => p.ParameterType != typeof(Poly.Runtime.IStoragePort))
            .ToArray();
        var nullability = new NullabilityInfoContext();

        await Assert.That(parameters.Select(p => p.Name!)).IsEquivalentTo(["name", "owner", "age", "nick", "legs"], CollectionOrdering.Matching);
        foreach (var parameter in parameters) {
            await Assert.That(parameter.HasDefaultValue && parameter.DefaultValue is null).IsTrue()
                .Because($"'{parameter.Name}' defaults to default");
            var nullable = Nullable.GetUnderlyingType(parameter.ParameterType) is not null
                || (!parameter.ParameterType.IsValueType && nullability.Create(parameter).WriteState == NullabilityState.Nullable);
            await Assert.That(nullable).IsTrue().Because($"'{parameter.Name}' is T?");
        }
    }

    /// <summary>
    /// Amendment 5: the lowered Create's own parameters (the storage port aside) are the published
    /// <c>CreateSlots</c>, in order, for every golden domain, so a caller built from the fact lines up.
    /// </summary>
    [Test]
    public async Task Create_LoweredParametersEqualCreateSlots_ForEveryGoldenDomain() {
        var mismatches = new List<string>();
        foreach (var golden in GoldenDomains.Lowerable) {
            var result = Poly.Tests.Docs.GuideBlocks.Analyze(golden.Poly);
            if (result.Domain is null || result.Analysis is null || result.Session is null)
                throw new InvalidOperationException($"{golden.Name} did not analyze.");
            var module = result.Session.Lower(result.Domain, result.Analysis);
            foreach (var entity in result.Domain.Types.OfType<Entity>()) {
                var create = module.Single(t => t.Name == entity.Name).Methods!.Single(m => m.Name == "Create");
                var lowered = (create.Parameters ?? [])
                    .Where(p => p.TypeReference is not ClrTypeReference { RuntimeType: var t } || t != typeof(Poly.Runtime.IStoragePort))
                    .Select(p => p.Name);
                var slots = result.Analysis.GetStructure(entity)!.CreateSlots.Select(s => DomainToCSharpExporter.ToCamelCase(s.Name));
                if (!lowered.SequenceEqual(slots))
                    mismatches.Add($"{golden.Name}.{entity.Name}: [{string.Join(", ", lowered)}] vs slots [{string.Join(", ", slots)}]");
            }
        }
        await Assert.That(mismatches).IsEmpty();
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
        type.GetMethods(PrintedMembers.Any)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
}