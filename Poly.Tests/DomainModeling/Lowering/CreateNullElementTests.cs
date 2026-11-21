using Poly.Ast.Nodes;
using Poly.Compile;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #564: a <c>many</c> create argument copies its elements into the stored list without looking at them, so a null
/// element was accepted. A policy that counts the collection (<c>count legs &gt; 0</c>) was then satisfied by a null, and
/// the next read of an element threw. The refusal belongs in the lowered <c>Create</c>, which both hosts run: the printed
/// C# and the interpreter execute one body, so the guard is pinned where it is written and proved on the printed side.
/// </summary>
public class CreateNullElementTests {
    /// <summary>The issue's shape, narrowed to the collection: an owned <c>many</c>, a policy that counts it, and a
    /// unique scalar so the factory takes the storage port exactly as the reported signature did.</summary>
    private const string Poly = """
        domain Logistics
        uses sqlite

        Leg: entity {
          Code: Text length(1, 8)
          shipment: Shipment via legs
        }

        Shipment: entity {
          Reference: Text unique length(4, 20)
          WeightKg: Number range(1, 40000)
          legs: many owned Leg
          HasLegs: policy { count legs > 0 }
        }
        """;

    private const string NullElementMessage = "'legs' contains a null element.";
    private const string NullElementPath = "Shipment.legs";

    private static CompileResult Compile() {
        var catalog = ProductCatalog.Create("sqlite");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite" };
        var compiled = new PolyCompiler(catalog, options).Compile(Poly);
        if (!compiled.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                compiled.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return compiled;
    }

    /// <summary>The printed <c>Create</c> run with a null element is the reported failure: <c>IsSuccess</c> was true and
    /// the null reached <c>_legs</c>. It answers a positioned <c>Required</c> on the collection instead.</summary>
    [Test]
    public async Task PrintedCreate_RefusesANullCollectionElement() {
        var compiled = Compile();
        var assembly = ExportedCSharp.CompileAndLoad(compiled.Files, "LogisticsDomain");
        var shipment = assembly.GetType("Logistics.Shipment")
            ?? throw new InvalidOperationException("The printed domain has no 'Logistics.Shipment'.");
        var leg = assembly.GetType("Logistics.Leg")
            ?? throw new InvalidOperationException("The printed domain has no 'Logistics.Leg'.");

        // The reporter's call: Shipment.Create(5m, "SHIP-6", …, legs: new Leg?[] { null }).
        var legs = Array.CreateInstance(leg, 1);
        var result = ExportedCSharp.InvokeCreate(shipment,
            ("weightKg", 5L), ("reference", "SHIP-6"), ("legs", legs), ("db", new InMemoryStoragePort()));

        var isSuccess = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        await Assert.That(isSuccess).IsFalse();

        var error = result.GetType().GetProperty("Error")!.GetValue(result)!;
        await Assert.That(error.GetType().GetProperty("Kind")!.GetValue(error)!.ToString()).IsEqualTo("Required");
        await Assert.That((string?)error.GetType().GetProperty("Message")!.GetValue(error)).IsEqualTo(NullElementMessage);
        await Assert.That((string?)error.GetType().GetProperty("ElementPath")!.GetValue(error)).IsEqualTo(NullElementPath);

        // An omitted collection is still no element at all: the constructor's empty list, not a refusal.
        var omitted = ExportedCSharp.InvokeCreate(shipment,
            ("weightKg", 5L), ("reference", "SHIP-7"), ("db", new InMemoryStoragePort()));
        await Assert.That((bool)omitted.GetType().GetProperty("IsSuccess")!.GetValue(omitted)!).IsTrue();

        // An empty collection is the same answer, so the guard tests elements and not the collection.
        var empty = ExportedCSharp.InvokeCreate(shipment,
            ("weightKg", 5L), ("reference", "SHIP-8"), ("legs", Array.CreateInstance(leg, 0)),
            ("db", new InMemoryStoragePort()));
        await Assert.That((bool)empty.GetType().GetProperty("IsSuccess")!.GetValue(empty)!).IsTrue();
    }

    /// <summary>
    /// The guard is in the lowered <c>Create</c> both hosts execute, not in the printed emitter: the interpreter runs the
    /// same body, so the simulator's answer comes from the same nodes. A null element is never stored, and an omitted
    /// collection is skipped rather than iterated.
    /// </summary>
    [Test]
    public async Task LoweredCreate_CarriesTheNullElementGuard() {
        var compiled = Compile();
        var create = LoweredCreate(compiled);

        var failureMessages = Descendants(create)
            .OfType<Invoke>()
            .Where(i => i.Delegate is Member { MemberName: "Required" }
                && i.Arguments.Length == 2
                && i.Arguments[0] is Constant { Value: string })
            .Select(i => (((Constant)i.Arguments[0]).Value as string)!)
            .ToList();
        await Assert.That(failureMessages).Contains(NullElementMessage);

        var paths = Descendants(create)
            .OfType<Invoke>()
            .Where(i => i.Delegate is Member { MemberName: "Required" } && i.Arguments.Length == 2
                && i.Arguments[1] is Constant { Value: string })
            .Select(i => (((Constant)i.Arguments[1]).Value as string)!)
            .ToList();
        await Assert.That(paths).Contains(NullElementPath);

        // One iteration over the collection slot, and a null test on its item: the guard is not an empty loop.
        var loop = Descendants(create).OfType<ForEachLoop>()
            .SingleOrDefault(l => l.Collection is Variable { Name: "legs" });
        await Assert.That(loop).IsNotNull();
        await Assert.That(loop!.LoopVariable.Name).IsNotEqualTo("legs");
    }

    private static MethodDefinitionNode LoweredCreate(CompileResult compiled) {
        var modules = compiled.Catalog!.Artifacts
            .Where(a => a.Descriptor.Id.Type is "entity" or "scaffolding")
            .SelectMany(a => (IReadOnlyList<TypeDefinitionNode>)a.Payload!)
            .ToList();
        var type = modules.FirstOrDefault(t => t.Name == "Shipment")
            ?? throw new InvalidOperationException("The operation module has no 'Shipment' type.");
        // The static Create is a scope wrapper; its guards live in the __BodyOfCreate partner both hosts run.
        var create = type.Methods?.FirstOrDefault(m => m.Name == "__BodyOfCreate")
                   ?? type.Methods?.FirstOrDefault(m => m.Name == "Create" && m.IsStatic)
                   ?? throw new InvalidOperationException("'Shipment' has no lowered Create body.");
        return create;
    }

    private static IEnumerable<Node> Descendants(Node node) {
        yield return node;
        foreach (var child in node.Children)
            if (child is not null)
                foreach (var inner in Descendants(child))
                    yield return inner;
    }
}